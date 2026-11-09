using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Omamagoto.Events;
using Omamagoto.Testing;
using UnityEngine;
using UnityEngine.TestTools;

namespace Omamagoto.Tests
{
    /// <summary>デモが台本（three.js 版と同じ）どおりに流れるか。時間を 20 倍速にして確かめる</summary>
    public class DemoPlayerTests
    {
        GameObject gameObject;
        DemoPlayer demo;
        List<ScreenEvent> received;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 20f;

            gameObject = new GameObject("DemoTest");
            var router = gameObject.AddComponent<EventRouter>();
            demo = gameObject.AddComponent<DemoPlayer>(); // ServerConnection がない＝サーバーなし

            received = new List<ScreenEvent>();
            router.OnAnyEvent += received.Add;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            Object.Destroy(gameObject);
        }

        /// <summary>イベントを「種類」や「種類:中身」の短い文字にする（包丁の位置は数が多いので除く）</summary>
        static string Short(ScreenEvent e)
        {
            switch (e.Type)
            {
                case EventNames.Cut: return $"cut:{e.Count}";
                case EventNames.Add: return $"add:{e.Count}";
                case EventNames.Fire: return $"fire:{e.Level}";
                case EventNames.Season: return $"{e.Item}:{e.TotalGrams}";
                case EventNames.Serve: return $"serve:{e.Score}";
                case EventNames.Position: return e.Visible == false ? $"{e.Item}:hidden" : e.Item;
                default: return e.Type;
            }
        }

        static bool IsMovingKnife(ScreenEvent e) => e.Type == EventNames.Position && e.Item == "knife" && e.Visible == true;

        [UnityTest]
        public IEnumerator サーバーがないと自動で始まり_台本どおりに流れる()
        {
            yield return Wait.Until(() => received.Exists(e => e.Type == EventNames.Serve), 15, "提供（serve）まで進まない");

            Assert.IsTrue(demo.IsPlaying);

            string[] expected =
            {
                "reset", "carrot",
                "swing", "cut:2",
                "knife:hidden", "add:2", "fire:2",
                "salt:0.5", "salt:1", "salt:1.5", "salt:2", "salt:2.5", "salt:3",
                "pepper:0.5", "pepper:1",
                "fire:0", "serve:86",
            };
            string[] actual = received.Where(e => !IsMovingKnife(e)).Select(Short).ToArray();
            Assert.AreEqual(expected, actual);

            int knifeMoves = received.Count(IsMovingKnife);
            Assert.Greater(knifeMoves, 5, "包丁の位置がほとんど送られていない");
        }

        [UnityTest]
        public IEnumerator 止めたら自動では始まらない()
        {
            demo.StopByUser();
            int countWhenStopped = received.Count;

            yield return Wait.RealSeconds(0.5f); // 20 倍速なので、デモの中では 10 秒

            Assert.IsFalse(demo.IsPlaying);
            Assert.AreEqual(countWhenStopped, received.Count, "止めたのにイベントが出ている");
        }

        [UnityTest]
        public IEnumerator 最後まで行くとはじめからくり返す()
        {
            yield return Wait.Until(() => received.Count(e => e.Type == EventNames.Reset) >= 3, 15, "くり返さない");
            // reset は台本の最初と最後（40.5 秒）にあるので、2 周目の最初で 3 回目になる
            Assert.IsTrue(demo.IsPlaying);
        }
    }
}
