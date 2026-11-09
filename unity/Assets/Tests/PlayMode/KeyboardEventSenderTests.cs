using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Omamagoto.Events;
using Omamagoto.Testing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;

namespace Omamagoto.Tests
{
    /// <summary>
    /// キーを押すと、正しいイベントが出るか。
    /// 本物のキーボードは使わず、Input System の「キーを押したことにする」機能（InputTestFixture）で押す。
    /// </summary>
    public class KeyboardEventSenderTests : InputTestFixture
    {
        Keyboard keyboard;
        GameObject gameObject;
        EventRouter router;
        List<ScreenEvent> received;

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
        }

        [TearDown]
        public override void TearDown()
        {
            Object.Destroy(gameObject);
            base.TearDown();
        }

        /// <summary>キーで操作する画面を作る。withDemo が true ならデモも付ける</summary>
        void CreateScreen(bool withDemo = false)
        {
            gameObject = new GameObject("KeyboardTest");
            router = gameObject.AddComponent<EventRouter>();
            if (withDemo) gameObject.AddComponent<DemoPlayer>();
            gameObject.AddComponent<KeyboardEventSender>();

            received = new List<ScreenEvent>();
            router.OnAnyEvent += received.Add;
        }

        /// <summary>キーを 1 回押してはなす</summary>
        IEnumerator Tap(KeyControl key)
        {
            Press(key, queueEventOnly: true);
            yield return null;
            Release(key, queueEventOnly: true);
            yield return null;
        }

        List<ScreenEvent> ReceivedOf(string type) => received.FindAll(e => e.Type == type);

        [UnityTest]
        public IEnumerator Kで振り下ろす()
        {
            CreateScreen();
            yield return Tap(keyboard.kKey);

            Assert.AreEqual(1, ReceivedOf(EventNames.Swing).Count);
            Assert.AreEqual("knife", received[0].Item);
            Assert.AreEqual("keyboard", received[0].Device);
        }

        [UnityTest]
        public IEnumerator Cを押すたびにピースが1個ずつ増える()
        {
            CreateScreen();
            yield return Tap(keyboard.cKey);
            yield return Tap(keyboard.cKey);

            var cuts = ReceivedOf(EventNames.Cut);
            Assert.AreEqual(new int?[] { 2, 3 }, cuts.ConvertAll(e => e.Count).ToArray());
        }

        [UnityTest]
        public IEnumerator Aでまな板のピースを全部フライパンに入れる()
        {
            CreateScreen();
            yield return Tap(keyboard.cKey); // 2 個
            yield return Tap(keyboard.cKey); // 3 個
            yield return Tap(keyboard.aKey); // 3 個とも入れる
            yield return Tap(keyboard.aKey); // まな板はもう空。テスト用に 1 個として出す

            var adds = ReceivedOf(EventNames.Add);
            Assert.AreEqual(new int?[] { 3, 1 }, adds.ConvertAll(e => e.Count).ToArray());
            Assert.AreEqual("pan", adds[0].Zone);
        }

        [UnityTest]
        public IEnumerator SとPで塩こしょう_合計が増えていく()
        {
            CreateScreen();
            yield return Tap(keyboard.sKey);
            yield return Tap(keyboard.sKey);
            yield return Tap(keyboard.pKey);

            var seasons = ReceivedOf(EventNames.Season);
            Assert.AreEqual(3, seasons.Count);
            Assert.AreEqual(("salt", 0.5f), (seasons[0].Item, seasons[0].TotalGrams.Value));
            Assert.AreEqual(("salt", 1.0f), (seasons[1].Item, seasons[1].TotalGrams.Value));
            Assert.AreEqual(("pepper", 0.5f), (seasons[2].Item, seasons[2].TotalGrams.Value));
            Assert.AreEqual(0.5f, seasons[2].AmountGrams);
        }

        [UnityTest]
        public IEnumerator 数字キーで火の強さ()
        {
            CreateScreen();
            yield return Tap(keyboard.digit3Key);
            yield return Tap(keyboard.digit1Key);
            yield return Tap(keyboard.digit2Key);
            yield return Tap(keyboard.digit0Key);

            var fires = ReceivedOf(EventNames.Fire);
            Assert.AreEqual(new int?[] { 3, 1, 2, 0 }, fires.ConvertAll(e => e.Level).ToArray());
        }

        [UnityTest]
        public IEnumerator Enterで提供()
        {
            CreateScreen();
            yield return Tap(keyboard.enterKey);

            var serves = ReceivedOf(EventNames.Serve);
            Assert.AreEqual(1, serves.Count);
            Assert.AreEqual(80, serves[0].Score);
        }

        [UnityTest]
        public IEnumerator サーバーがないときRでリセットし_数え直す()
        {
            CreateScreen();
            yield return Tap(keyboard.cKey);
            yield return Tap(keyboard.sKey);
            yield return Tap(keyboard.rKey);
            yield return Tap(keyboard.cKey);
            yield return Tap(keyboard.sKey);

            Assert.AreEqual(1, ReceivedOf(EventNames.Reset).Count);
            Assert.AreEqual(new int?[] { 2, 2 }, ReceivedOf(EventNames.Cut).ConvertAll(e => e.Count).ToArray());
            Assert.AreEqual(0.5f, ReceivedOf(EventNames.Season)[1].TotalGrams);
        }

        [UnityTest]
        public IEnumerator サーバーから届いた数に続けて数える()
        {
            CreateScreen();
            router.Receive(new ScreenEvent { Device = "server", Seq = 1, Type = EventNames.Cut, Count = 5 });
            router.Receive(new ScreenEvent { Device = "server", Seq = 2, Type = EventNames.Season, Item = "salt", TotalGrams = 2f });

            yield return Tap(keyboard.cKey);
            yield return Tap(keyboard.sKey);

            Assert.AreEqual(6, ReceivedOf(EventNames.Cut)[1].Count);
            Assert.AreEqual(2.5f, ReceivedOf(EventNames.Season)[1].TotalGrams);
        }

        [UnityTest]
        public IEnumerator Dでデモを始めて止め_ほかのキーでも止まる()
        {
            CreateScreen(withDemo: true);
            var demo = gameObject.GetComponent<DemoPlayer>();
            demo.StopByUser(); // 自動で始まっていたら止める
            yield return null;
            Assert.IsFalse(demo.IsPlaying);

            yield return Tap(keyboard.dKey);
            Assert.IsTrue(demo.IsPlaying, "D でデモが始まらない");

            yield return Tap(keyboard.cKey);
            Assert.IsFalse(demo.IsPlaying, "キーを押してもデモが止まらない");

            yield return Tap(keyboard.dKey);
            Assert.IsTrue(demo.IsPlaying, "もう一度 D で始まらない");

            yield return Tap(keyboard.dKey);
            Assert.IsFalse(demo.IsPlaying, "D でデモが止まらない");
        }
    }
}
