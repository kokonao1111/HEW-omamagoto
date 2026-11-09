using System.Collections.Generic;
using NUnit.Framework;
using Omamagoto.Events;
using Omamagoto.Testing;
using UnityEngine;

namespace Omamagoto.Tests
{
    /// <summary>LocalEventSender が、サーバーと同じ形のイベントを作るか</summary>
    public class LocalEventSenderTests
    {
        GameObject gameObject;
        EventRouter router;
        LocalEventSender sender;
        List<ScreenEvent> received;

        [SetUp]
        public void SetUp()
        {
            gameObject = new GameObject("LocalEventSenderTest");
            router = gameObject.AddComponent<EventRouter>();
            sender = new LocalEventSender("keyboard", router);
            received = new List<ScreenEvent>();
            router.OnAnyEvent += received.Add;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void 共通ヘッダが付き_seqは1から増える()
        {
            sender.Swing();
            sender.Swing();

            Assert.AreEqual("keyboard", received[0].Device);
            Assert.AreEqual(1L, received[0].Seq);
            Assert.AreEqual(2L, received[1].Seq);
            Assert.IsNotNull(received[0].Timestamp);
        }

        [Test]
        public void それぞれのイベントの中身が6_4の形になる()
        {
            sender.Cut(3);
            sender.AddToPan(2);
            sender.Season("salt", 0.5f, 1.5f);
            sender.Fire(2);
            sender.Serve(80, "good");
            sender.Reset();
            sender.CarrotPosition(0.4f, 0.5f, 10f);
            sender.KnifePosition(0.1f, 0.2f, 0.3f, 0.4f);
            sender.KnifeHidden();

            AssertEvent(received[0], "{\"device\":\"keyboard\",\"seq\":1,\"event\":\"cut\",\"item\":\"carrot\",\"count\":3}");
            AssertEvent(received[1], "{\"device\":\"keyboard\",\"seq\":2,\"event\":\"add\",\"item\":\"carrot\",\"count\":2,\"zone\":\"pan\"}");
            AssertEvent(received[2], "{\"device\":\"keyboard\",\"seq\":3,\"event\":\"season\",\"item\":\"salt\",\"amount_g\":0.5,\"total_g\":1.5}");
            AssertEvent(received[3], "{\"device\":\"keyboard\",\"seq\":4,\"event\":\"fire\",\"level\":2}");
            AssertEvent(received[4], "{\"device\":\"keyboard\",\"seq\":5,\"event\":\"serve\",\"score\":80,\"comment\":\"good\"}");
            AssertEvent(received[5], "{\"device\":\"keyboard\",\"seq\":6,\"event\":\"reset\"}");
            AssertEvent(received[6], "{\"device\":\"keyboard\",\"seq\":7,\"event\":\"position\",\"item\":\"carrot\",\"visible\":true,\"x\":0.4,\"y\":0.5,\"rot\":10.0}");
            AssertEvent(received[7], "{\"device\":\"keyboard\",\"seq\":8,\"event\":\"position\",\"item\":\"knife\",\"visible\":true,\"tip\":[0.1,0.2],\"grip\":[0.3,0.4]}");
            AssertEvent(received[8], "{\"device\":\"keyboard\",\"seq\":9,\"event\":\"position\",\"item\":\"knife\",\"visible\":false}");
        }

        /// <summary>時刻（ts）は毎回変わるので外して、JSON の文字で比べる</summary>
        static void AssertEvent(ScreenEvent actual, string expectedJson)
        {
            actual.Timestamp = null;
            Assert.AreEqual(expectedJson, actual.ToString());
        }
    }
}
