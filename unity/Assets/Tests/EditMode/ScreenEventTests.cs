using Newtonsoft.Json;
using NUnit.Framework;
using Omamagoto.Events;

namespace Omamagoto.Tests
{
    /// <summary>サーバーの JSON（要件定義書 6.4）を、ScreenEvent に正しく読めるか</summary>
    public class ScreenEventTests
    {
        static ScreenEvent Parse(string json) => JsonConvert.DeserializeObject<ScreenEvent>(json);

        [Test]
        public void 包丁の位置を読める()
        {
            var e = Parse("{\"device\":\"server\",\"ts\":1760000000130,\"seq\":900,\"event\":\"position\",\"item\":\"knife\",\"tip\":[0.31,0.12],\"grip\":[0.55,0.05],\"visible\":true}");

            Assert.AreEqual("server", e.Device);
            Assert.AreEqual(1760000000130L, e.Timestamp);
            Assert.AreEqual(900L, e.Seq);
            Assert.AreEqual(EventNames.Position, e.Type);
            Assert.AreEqual("knife", e.Item);
            Assert.AreEqual(new[] { 0.31f, 0.12f }, e.Tip);
            Assert.AreEqual(new[] { 0.55f, 0.05f }, e.Grip);
            Assert.AreEqual(true, e.Visible);
        }

        [Test]
        public void 野菜の位置と向きを読める()
        {
            var e = Parse("{\"event\":\"position\",\"item\":\"carrot\",\"x\":0.42,\"y\":0.5,\"rot\":15,\"visible\":true}");

            Assert.AreEqual(0.42f, e.X);
            Assert.AreEqual(0.5f, e.Y);
            Assert.AreEqual(15f, e.Rotation);
        }

        [Test]
        public void 電子レンジの扉の開け閉めを読める()
        {
            var opened = Parse("{\"device\":\"camera-door\",\"seq\":1,\"event\":\"door\",\"open\":true}");
            var closed = Parse("{\"event\":\"door\",\"open\":false}");

            Assert.AreEqual(EventNames.Door, opened.Type);
            Assert.AreEqual(true, opened.Open);
            Assert.AreEqual(false, closed.Open);
            Assert.IsNull(closed.Angle, "angle がないときは null");

            var half = Parse("{\"event\":\"door\",\"open\":true,\"angle\":45.5}");
            Assert.AreEqual(45.5f, half.Angle);
        }

        [Test]
        public void 届かなかった項目はnullになり_0とは区別できる()
        {
            var e = Parse("{\"event\":\"position\",\"item\":\"knife\",\"visible\":false}");

            Assert.AreEqual(false, e.Visible);
            Assert.IsNull(e.X);
            Assert.IsNull(e.Tip);
            Assert.IsNull(e.Count);

            var fireOff = Parse("{\"event\":\"fire\",\"level\":0}");
            Assert.AreEqual(0, fireOff.Level); // 0（火を消した）は null ではない
        }

        [Test]
        public void 塩_火_提供_ステータスを読める()
        {
            var season = Parse("{\"event\":\"season\",\"item\":\"salt\",\"amount_g\":0.5,\"total_g\":3.2}");
            Assert.AreEqual(0.5f, season.AmountGrams);
            Assert.AreEqual(3.2f, season.TotalGrams);

            var serve = Parse("{\"event\":\"serve\",\"score\":72,\"comment\":\"too_salty\"}");
            Assert.AreEqual(72, serve.Score);
            Assert.AreEqual("too_salty", serve.Comment);

            var status = Parse("{\"event\":\"status\",\"devices\":[{\"role\":\"salt\",\"connected\":true,\"battery\":63}]}");
            Assert.AreEqual(1, status.Devices.Length);
            Assert.AreEqual("salt", status.Devices[0].Role);
            Assert.IsTrue(status.Devices[0].Connected);
            Assert.AreEqual(63, status.Devices[0].Battery);
        }

        [Test]
        public void 知らない項目があっても読める()
        {
            // サーバーが新しい項目を足しても、画面が止まらないように
            var e = Parse("{\"event\":\"serve\",\"score\":80,\"photo\":\"data:image/jpeg;base64,xxxx\"}");
            Assert.AreEqual(80, e.Score);
        }
    }
}
