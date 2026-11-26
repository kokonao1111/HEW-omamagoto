using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Omamagoto.Events;
using UnityEngine;

namespace Omamagoto.Network
{
    /// <summary>今のサーバーとのつながり</summary>
    public enum ConnectionState
    {
        Connecting,    // はじめてつなごうとしている
        Connected,     // つながっている
        Reconnecting,  // 一度つながったあとに切れて、つなぎ直そうとしている
    }

    /// <summary>
    /// サーバー（server/、Go）に WebSocket でつなぎ、届いたイベントを EventRouter に渡す。
    ///
    /// - つながったら {"device":"screen","event":"hello"} を 1 回送る（サーバーが今の火を送り返す）
    /// - 切れたら、1 秒・1.6 秒・2.6 秒…と少しずつ間をあけて、自動でつなぎ直す（最大 10 秒）
    /// - つなぎ直したら、前の seq は忘れる（サーバーは起動するたびに seq を 1 から数える）
    ///
    /// await のあとの処理は Unity のメインスレッドに戻ってくるので、
    /// 届いたイベントはそのまま GameObject を動かすのに使ってよい。
    /// </summary>
    [RequireComponent(typeof(EventRouter))]
    public class ServerConnection : MonoBehaviour
    {
        [Tooltip("つなぐ先。別の PC のサーバーにつなぐときは ws://192.168.x.x:9000/screen のようにする")]
        [SerializeField] string serverUrl = "ws://localhost:9000/screen";

        [Tooltip("つなぎ直すまでの、いちばん長い待ち時間（秒）")]
        [SerializeField] float maxRetrySeconds = 10f;

        public ConnectionState State { get; private set; } = ConnectionState.Connecting;
        public bool IsConnected => State == ConnectionState.Connected;
        /// <summary>つなぐ先。変えたら、次につなぐときから使う</summary>
        public string ServerUrl
        {
            get => serverUrl;
            set => serverUrl = value;
        }

        /// <summary>サーバーにつながった（つなぎ直した）ときに呼ばれる</summary>
        public event Action OnConnected;

        const string HelloMessage = "{\"device\":\"screen\",\"event\":\"hello\"}";
        const string ResetMessage = "{\"event\":\"reset\"}";
        const int ConnectTimeoutMilliseconds = 3000;

        EventRouter router;
        ClientWebSocket socket;
        CancellationTokenSource stopSource;

        void Awake()
        {
            router = GetComponent<EventRouter>();
        }

        void OnEnable()
        {
            stopSource = new CancellationTokenSource();
            _ = KeepConnectedAsync(stopSource.Token);
        }

        void OnDisable()
        {
            // 再生を止めたときなどに、つなぎ直しをやめて接続を閉じる
            stopSource.Cancel();
            CloseSocket();
        }

        /// <summary>
        /// サーバーに「リセットして」と頼む。サーバーが全部の画面に reset を送り返す。
        /// つながっていなくて送れなかったときは false を返す。
        /// </summary>
        public bool SendReset()
        {
            if (!IsConnected) return false;

            _ = SendTextAsync(ResetMessage);
            return true;
        }

        // ---------------- ここから下は、つなぐ・受け取る・送る の中身 ----------------

        /// <summary>止めるまで、つなぐ → 受け取る → 切れたら待ってつなぎ直す、をくり返す</summary>
        async Task KeepConnectedAsync(CancellationToken stop)
        {
            float waitSeconds = 1f;
            bool hasEverConnected = false;

            while (!stop.IsCancellationRequested)
            {
                State = hasEverConnected ? ConnectionState.Reconnecting : ConnectionState.Connecting;

                if (await TryConnectAsync(stop))
                {
                    hasEverConnected = true;
                    waitSeconds = 1f;
                    await ReceiveUntilClosedAsync(stop);
                    CloseSocket();

                    if (stop.IsCancellationRequested) break;
                    Debug.Log("[サーバー] 切れました。つなぎ直します");
                    State = ConnectionState.Reconnecting;
                }

                // 少し待ってから、もう一度つなぐ
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(waitSeconds), stop);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                waitSeconds = Mathf.Min(maxRetrySeconds, waitSeconds * 1.6f);
            }
        }

        /// <summary>1 回だけつないでみる。つながったら hello を送って true を返す</summary>
        async Task<bool> TryConnectAsync(CancellationToken stop)
        {
            socket = new ClientWebSocket();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                timeout.CancelAfter(ConnectTimeoutMilliseconds);
                await socket.ConnectAsync(new Uri(serverUrl), timeout.Token);
            }
            catch (Exception)
            {
                // サーバーが起動していないなど。あとでまたつなぐ
                CloseSocket();
                return false;
            }

            State = ConnectionState.Connected;
            router.ForgetSeq();
            Debug.Log($"[サーバー] つながりました：{serverUrl}");

            await SendTextAsync(HelloMessage);
            OnConnected?.Invoke();
            return true;
        }

        /// <summary>切れるまでメッセージを受け取り、1 件ずつ HandleMessage に渡す</summary>
        async Task ReceiveUntilClosedAsync(CancellationToken stop)
        {
            var buffer = new byte[8192];
            using var message = new MemoryStream(); // 1 件のメッセージが何回かに分かれて届くことがあるので、ためておく

            while (socket != null && socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), stop);
                }
                catch (Exception)
                {
                    return; // 切れた・止めた
                }

                if (result.MessageType == WebSocketMessageType.Close) return;

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;

                string json = Encoding.UTF8.GetString(message.ToArray());
                message.SetLength(0);
                HandleMessage(json);
            }
        }

        /// <summary>届いた JSON を ScreenEvent にして、EventRouter に渡す</summary>
        void HandleMessage(string json)
        {
            ScreenEvent screenEvent;
            try
            {
                screenEvent = JsonConvert.DeserializeObject<ScreenEvent>(json);
            }
            catch (JsonException)
            {
                Debug.LogWarning($"[サーバー] 読めない JSON を捨てました：{json}");
                return;
            }

            router.Receive(screenEvent);
        }

        async Task SendTextAsync(string text)
        {
            if (socket == null || socket.State != WebSocketState.Open) return;

            try
            {
                var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(text));
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[サーバー] 送れませんでした：{ex.Message}");
            }
        }

        void CloseSocket()
        {
            if (socket == null) return;

            socket.Abort();
            socket.Dispose();
            socket = null;
        }
    }
}
