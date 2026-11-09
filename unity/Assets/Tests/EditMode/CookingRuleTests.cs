using System.Collections.Generic;
using NUnit.Framework;
using Omamagoto.Events;
using Omamagoto.Kitchen;
using Omamagoto.Testing;
using UnityEngine;

namespace Omamagoto.Tests
{
    /// <summary>焼けると色がどう変わるか（CookablePiece）と、皿へ入れるイベント</summary>
    public class CookingRuleTests
    {
        static readonly Color Raw = new Color(0.93f, 0.43f, 0.08f);
        static readonly Color Tint = new Color(0.85f, 0.66f, 0.5f);
        static readonly Color Burnt = new Color(0.1f, 0.06f, 0.035f);

        static Color At(float doneness) => CookablePiece.ColorAt(Raw, Tint, Burnt, doneness);

        static void AssertColor(Color expected, Color actual)
        {
            Assert.AreEqual(expected.r, actual.r, 0.001f, $"{expected} のはずが {actual}");
            Assert.AreEqual(expected.g, actual.g, 0.001f, $"{expected} のはずが {actual}");
            Assert.AreEqual(expected.b, actual.b, 0.001f, $"{expected} のはずが {actual}");
        }

        [Test]
        public void 生は元の色_焼けると茶色っぽく_こげると黒くなる()
        {
            AssertColor(Raw, At(0));
            AssertColor(Raw * Tint, At(CookablePiece.Cooked));
            AssertColor(Raw * Tint, At(1.2f));                     // 焼けた～こげ始めのあいだは、焼けた色のまま
            AssertColor(Burnt, At(CookablePiece.Burnt));
        }

        [Test]
        public void 焼けるほど暗くなっていく()
        {
            float previous = float.MaxValue;
            for (float doneness = 0; doneness <= CookablePiece.Burnt; doneness += 0.1f)
            {
                Color color = At(doneness);
                float brightness = color.r + color.g + color.b;
                Assert.LessOrEqual(brightness, previous + 0.0001f, $"焼け具合 {doneness:F1} で明るくなった");
                previous = brightness;
            }
        }

        [Test]
        public void 焼けるとつやが出て_こげるとつやがなくなる()
        {
            Assert.AreEqual(0.45f, CookablePiece.SmoothnessAt(0.45f, 0), 0.001f);
            Assert.Greater(CookablePiece.SmoothnessAt(0.45f, CookablePiece.Cooked), 0.45f);
            Assert.Less(CookablePiece.SmoothnessAt(0.45f, CookablePiece.Burnt), 0.45f);
        }

        [Test]
        public void 皿へ入れるイベントは_zoneがplate()
        {
            var gameObject = new GameObject("Router");
            var router = gameObject.AddComponent<EventRouter>();
            var received = new List<ScreenEvent>();
            router.OnAdd += received.Add;

            new LocalEventSender("keyboard", router).AddToPlate(3);

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual("plate", received[0].Zone);
            Assert.AreEqual(3, received[0].Count);
            Object.DestroyImmediate(gameObject);
        }
    }
}
