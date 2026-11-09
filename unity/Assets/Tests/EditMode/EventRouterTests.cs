using System.Collections.Generic;
using NUnit.Framework;
using Omamagoto.Events;
using UnityEngine;

namespace Omamagoto.Tests
{
    /// <summary>EventRouter が、種類ごとに正しく振り分け、重複を捨てるか</summary>
    public class EventRouterTests
    {
        GameObject gameObject;
        EventRouter router;

        [SetUp]
        public void SetUp()
        {
            gameObject = new GameObject("EventRouterTest");
            router = gameObject.AddComponent<EventRouter>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(gameObject);
        }

        static ScreenEvent Event(string type, long seq, string device = "server")
        {
            return new ScreenEvent { Device = device, Seq = seq, Type = type };
        }

        [Test]
        public void 種類ごとに合うところだけに届く()
        {
            var cuts = new List<ScreenEvent>();
            var fires = new List<ScreenEvent>();
            router.OnCut += cuts.Add;
            router.OnFire += fires.Add;

            router.Receive(Event(EventNames.Cut, 1));

            Assert.AreEqual(1, cuts.Count);
            Assert.AreEqual(0, fires.Count);
        }

        [Test]
        public void 全部の種類が届く()
        {
            var received = new List<string>();
            router.OnPosition += e => received.Add(e.Type);
            router.OnSwing += e => received.Add(e.Type);
            router.OnCut += e => received.Add(e.Type);
            router.OnAdd += e => received.Add(e.Type);
            router.OnSeason += e => received.Add(e.Type);
            router.OnFire += e => received.Add(e.Type);
            router.OnServe += e => received.Add(e.Type);
            router.OnReset += e => received.Add(e.Type);
            router.OnStatus += e => received.Add(e.Type);

            string[] all =
            {
                EventNames.Position, EventNames.Swing, EventNames.Cut, EventNames.Add, EventNames.Season,
                EventNames.Fire, EventNames.Serve, EventNames.Reset, EventNames.Status,
            };
            for (int i = 0; i < all.Length; i++) router.Receive(Event(all[i], i + 1));

            Assert.AreEqual(all, received.ToArray());
        }

        [Test]
        public void 同じseq以下は捨てる()
        {
            var cuts = new List<ScreenEvent>();
            router.OnCut += cuts.Add;

            router.Receive(Event(EventNames.Cut, 5));
            router.Receive(Event(EventNames.Cut, 5)); // 同じ番号
            router.Receive(Event(EventNames.Cut, 3)); // 古い番号
            router.Receive(Event(EventNames.Cut, 6));

            Assert.AreEqual(new long?[] { 5, 6 }, cuts.ConvertAll(e => e.Seq).ToArray());
        }

        [Test]
        public void seqは送ったものごとに別に数える()
        {
            var cuts = new List<ScreenEvent>();
            router.OnCut += cuts.Add;

            router.Receive(Event(EventNames.Cut, 10, "server"));
            router.Receive(Event(EventNames.Cut, 1, "keyboard"));

            Assert.AreEqual(2, cuts.Count);
        }

        [Test]
        public void ForgetSeqのあとはseq1から受け付ける()
        {
            var cuts = new List<ScreenEvent>();
            router.OnCut += cuts.Add;

            router.Receive(Event(EventNames.Cut, 50));
            router.ForgetSeq(); // サーバーにつなぎ直した
            router.Receive(Event(EventNames.Cut, 1));

            Assert.AreEqual(2, cuts.Count);
        }

        [Test]
        public void seqがないイベントは捨てない()
        {
            var resets = new List<ScreenEvent>();
            router.OnReset += resets.Add;

            router.Receive(new ScreenEvent { Type = EventNames.Reset });
            router.Receive(new ScreenEvent { Type = EventNames.Reset });

            Assert.AreEqual(2, resets.Count);
        }

        [Test]
        public void 知らない種類や空のイベントでも止まらない()
        {
            var any = new List<ScreenEvent>();
            router.OnAnyEvent += any.Add;

            Assert.DoesNotThrow(() =>
            {
                router.Receive(null);
                router.Receive(new ScreenEvent());
                router.Receive(Event("dance", 1));
            });
            Assert.AreEqual(1, any.Count); // 知らない種類も OnAnyEvent には届く（表示はできる）
        }
    }
}
