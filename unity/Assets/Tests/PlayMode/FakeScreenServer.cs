using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Omamagoto.Tests
{
    /// <summary>
    /// テストの中だけで動く、にせのサーバー。本物のサーバー（server/）の /screen のふりをする。
    /// 空いているポートで待ち、画面がつないできたら、送られてきた文字を Received にためる。
    ///
    /// Unity の .NET では HttpListener の WebSocket が使えない（NotImplemented）ので、
    /// TCP で待って、WebSocket の最初のあいさつ（ハンドシェイク）だけ自分で返している。
    /// </summary>
    public class FakeScreenServer : IDisposable
    {
        public int Port { get; }
        public string Url => $"ws://localhost:{Port}/screen";

        /// <summary>画面から届いたメッセージ（届いた順）</summary>
        public List<string> Received { get; } = new List<string>();

        /// <summary>今までに画面がつないできた回数</summary>
        public int ConnectionCount { get; private set; }

        public bool HasClient => socket != null && socket.State == WebSocketState.Open;

        // WebSocket の決まりで、あいさつの返事を作るときに足す文字（RFC 6455）
        const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        readonly TcpListener listener;
        TcpClient client;
        WebSocket socket;
        bool disposed;

        public FakeScreenServer()
        {
            listener = new TcpListener(IPAddress.Loopback, 0); // 0 = 空いているポートを使う
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = AcceptLoopAsync();
        }

        /// <summary>画面にメッセージを送る</summary>
        public Task SendAsync(string text)
        {
            var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(text));
            return socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        /// <summary>サーバーの側から接続を切る（画面がつなぎ直すかを確かめるため）</summary>
        public void DisconnectClient()
        {
            socket?.Abort();
            client?.Close();
        }

        public void Dispose()
        {
            if (disposed) return; // 2 回呼ばれても大丈夫なように
            disposed = true;

            DisconnectClient();
            listener.Stop();
        }

        /// <summary>止めるまで、画面がつないでくるのを待ち、つないできたら受け取る</summary>
        async Task AcceptLoopAsync()
        {
            while (!disposed)
            {
                try
                {
                    client = await listener.AcceptTcpClientAsync();
                    Stream stream = client.GetStream();
                    await AnswerHandshakeAsync(stream);

                    socket = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(30));
                    ConnectionCount++;
                    await ReceiveLoopAsync(socket);
                }
                catch (Exception)
                {
                    // 止めた・切れた。止めていなければ、次の接続を待つ
                }
            }
        }

        /// <summary>画面からの「WebSocket にしたい」というお願いを読んで、「いいよ」と返す</summary>
        static async Task AnswerHandshakeAsync(Stream stream)
        {
            string request = await ReadHttpHeaderAsync(stream);

            string key = null;
            foreach (string line in request.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                {
                    key = line.Substring("Sec-WebSocket-Key:".Length).Trim();
                }
            }

            string accept;
            using (var sha1 = SHA1.Create())
            {
                accept = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
            }

            string response =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(bytes, 0, bytes.Length);
        }

        /// <summary>空行（\r\n\r\n）まで 1 バイトずつ読む（あいさつの後ろの WebSocket のデータを読みすぎないように）</summary>
        static async Task<string> ReadHttpHeaderAsync(Stream stream)
        {
            var header = new StringBuilder();
            var oneByte = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n"))
            {
                int read = await stream.ReadAsync(oneByte, 0, 1);
                if (read == 0) throw new IOException("あいさつの途中で切れた");
                header.Append((char)oneByte[0]);
            }
            return header.ToString();
        }

        async Task ReceiveLoopAsync(WebSocket webSocket)
        {
            var buffer = new byte[8192];
            while (webSocket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) return;

                Received.Add(Encoding.UTF8.GetString(buffer, 0, result.Count)); // テストのメッセージは短いので 1 回で届く
            }
        }
    }
}
