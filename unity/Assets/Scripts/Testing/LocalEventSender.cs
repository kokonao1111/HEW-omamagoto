using System;
using Omamagoto.Events;

namespace Omamagoto.Testing
{
    /// <summary>
    /// サーバーなしで試すために、サーバーと同じ形のイベントを作って EventRouter に渡す。
    /// キー（KeyboardEventSender）とデモ（DemoPlayer）で使う。
    ///
    /// 例：sender.Cut(3);  // 「ピースが 3 個になった」を出す
    /// </summary>
    public class LocalEventSender
    {
        readonly string deviceName;
        readonly EventRouter router;
        long seq;

        /// <param name="deviceName">送ったものの名前。"keyboard" や "demo"</param>
        public LocalEventSender(string deviceName, EventRouter router)
        {
            this.deviceName = deviceName;
            this.router = router;
        }

        public void Swing()
        {
            Send(new ScreenEvent { Type = EventNames.Swing, Item = "knife" });
        }

        /// <param name="pieceCount">切ったあとのピースの数</param>
        public void Cut(int pieceCount)
        {
            Send(new ScreenEvent { Type = EventNames.Cut, Item = "carrot", Count = pieceCount });
        }

        /// <param name="pieceCount">フライパンに入ったピースの数</param>
        public void AddToPan(int pieceCount)
        {
            Send(new ScreenEvent { Type = EventNames.Add, Item = "carrot", Zone = "pan", Count = pieceCount });
        }

        /// <param name="item">"salt" か "pepper"</param>
        public void Season(string item, float amountGrams, float totalGrams)
        {
            Send(new ScreenEvent { Type = EventNames.Season, Item = item, AmountGrams = amountGrams, TotalGrams = totalGrams });
        }

        /// <param name="level">0=切、1=弱、2=中、3=強</param>
        public void Fire(int level)
        {
            Send(new ScreenEvent { Type = EventNames.Fire, Level = level });
        }

        public void Serve(int score, string comment)
        {
            Send(new ScreenEvent { Type = EventNames.Serve, Score = score, Comment = comment });
        }

        public void Reset()
        {
            Send(new ScreenEvent { Type = EventNames.Reset });
        }

        /// <summary>にんじんの位置（まな板の範囲で 0〜1）と向き（度）</summary>
        public void CarrotPosition(float x, float y, float rotation)
        {
            Send(new ScreenEvent { Type = EventNames.Position, Item = "carrot", X = x, Y = y, Rotation = rotation, Visible = true });
        }

        /// <summary>包丁の刃先と柄のはしの位置（まな板の範囲で 0〜1）</summary>
        public void KnifePosition(float tipX, float tipY, float gripX, float gripY)
        {
            Send(new ScreenEvent
            {
                Type = EventNames.Position,
                Item = "knife",
                Tip = new[] { tipX, tipY },
                Grip = new[] { gripX, gripY },
                Visible = true,
            });
        }

        public void KnifeHidden()
        {
            Send(new ScreenEvent { Type = EventNames.Position, Item = "knife", Visible = false });
        }

        void Send(ScreenEvent e)
        {
            // サーバーと同じく、共通ヘッダ（device・ts・seq）を付ける
            e.Device = deviceName;
            e.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            e.Seq = ++seq;
            router.Receive(e);
        }
    }
}
