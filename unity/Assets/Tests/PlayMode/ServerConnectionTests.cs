using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Omamagoto.Events;
using Omamagoto.Network;
using UnityEngine;
using UnityEngine.TestTools;

namespace Omamagoto.Tests
{
    /// <summary>ServerConnection が、サーバーと正しくやり取りするか（にせサーバーを相手に）</summary>
    public class ServerConnectionTests
    {
        const string Hello = "{\"device\":\"screen\",\"event\":\"hello\"}";
        const string Reset = "{\"event\":\"reset\"}";

        FakeScreenServer server;
        GameObject gameObject;
        ServerConnection connection;
        List<ScreenEvent> received;

        [SetUp]
        public void SetUp()
        {
            server = new FakeScreenServer();
            CreateConnection(server.Url);
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(gameObject);
            server.Dispose();
        }

        /// <summary>つなぐ先を決めてから、つなぎはじめる</summary>
        void CreateConnection(string url)
        {
            if (gameObject != null) Object.Destroy(gameObject);

            gameObject = new GameObject("ServerConnectionTest");
            gameObject.SetActive(false); // つなぐ先を変えるまで、つなぎはじめないように
            var router = gameObject.AddComponent<EventRouter>();
            connection = gameObject.AddComponent<ServerConnection>();
            connection.ServerUrl = url;

            received = new List<ScreenEvent>();
            router.OnAnyEvent += received.Add;
            gameObject.SetActive(true);
        }

        static string ServerMessage(long seq, string body)
        {
            return $"{{\"device\":\"server\",\"ts\":1,\"seq\":{seq},{body}}}";
        }

        [UnityTest]
        public IEnumerator つながったらhelloを送る()
        {
            yield return Wait.Until(() => server.Received.Count > 0, 5, "hello が届かない");

            Assert.AreEqual(Hello, server.Received[0]);
            Assert.IsTrue(connection.IsConnected);
            Assert.AreEqual(ConnectionState.Connected, connection.State);
        }

        [UnityTest]
        public IEnumerator サーバーのイベントを受け取り_重複と壊れたJSONは捨てる()
        {
            yield return Wait.Until(() => server.HasClient && connection.IsConnected, 5, "つながらない");
            LogAssert.Expect(LogType.Warning, new Regex("読めない JSON"));

            server.SendAsync(ServerMessage(1, "\"event\":\"fire\",\"level\":2"));
            server.SendAsync("{broken json");
            server.SendAsync(ServerMessage(1, "\"event\":\"fire\",\"level\":3")); // 同じ seq。捨てる
            server.SendAsync(ServerMessage(2, "\"event\":\"cut\",\"item\":\"carrot\",\"count\":2"));

            yield return Wait.Until(() => received.Exists(e => e.Type == EventNames.Cut), 5, "cut が届かない");

            Assert.AreEqual(2, received.Count);
            Assert.AreEqual(EventNames.Fire, received[0].Type);
            Assert.AreEqual(2, received[0].Level);
            Assert.AreEqual(2, received[1].Count);
        }

        [UnityTest]
        public IEnumerator リセットをサーバーに送る()
        {
            yield return Wait.Until(() => server.Received.Count > 0, 5, "hello が届かない");

            Assert.IsTrue(connection.SendReset());

            yield return Wait.Until(() => server.Received.Contains(Reset), 5, "reset が届かない");
        }

        [UnityTest]
        public IEnumerator 切れたらつなぎ直し_seqを1から受け付ける()
        {
            yield return Wait.Until(() => server.HasClient && connection.IsConnected, 5, "つながらない");
            server.SendAsync(ServerMessage(5, "\"event\":\"cut\",\"item\":\"carrot\",\"count\":2"));
            yield return Wait.Until(() => received.Count == 1, 5, "1 件目が届かない");

            // サーバーが切る（サーバーを起動し直したことにする）
            server.DisconnectClient();
            yield return Wait.Until(() => connection.State == ConnectionState.Reconnecting, 5, "「再接続中」にならない");

            // 自動でつなぎ直して、もう一度 hello を送ってくる
            yield return Wait.Until(() => server.ConnectionCount == 2 && server.HasClient && connection.IsConnected, 10, "つなぎ直さない");
            yield return Wait.Until(() => server.Received.FindAll(m => m == Hello).Count == 2, 5, "2 回目の hello が届かない");

            // 起動し直したサーバーは seq を 1 から数える。前の 5 より小さくても受け付ける
            server.SendAsync(ServerMessage(1, "\"event\":\"cut\",\"item\":\"carrot\",\"count\":3"));
            yield return Wait.Until(() => received.Count == 2, 5, "つなぎ直したあとの seq 1 が届かない");
            Assert.AreEqual(3, received[1].Count);
        }

        [UnityTest]
        public IEnumerator サーバーがないときは_待っている状態で_リセットは送れない()
        {
            server.Dispose();
            var nobody = new FakeScreenServer(); // 空いているポートを 1 つ見つけるためだけに使う
            string urlWithNobody = nobody.Url;
            nobody.Dispose();

            CreateConnection(urlWithNobody);
            yield return Wait.RealSeconds(1.5f);

            Assert.AreEqual(ConnectionState.Connecting, connection.State);
            Assert.IsFalse(connection.SendReset());
        }
    }
}
