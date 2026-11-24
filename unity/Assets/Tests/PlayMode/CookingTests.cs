using System.Collections;
using NUnit.Framework;
using Omamagoto.Events;
using Omamagoto.Kitchen;
using Omamagoto.Testing;
using UnityEngine;
using UnityEngine.TestTools;

namespace Omamagoto.Tests
{
    /// <summary>
    /// 切ったにんじんを、フライパンに入れて（add）、火で焼いて（fire）、皿に盛る（serve）までの流れ。
    /// まな板・フライパン・皿は、当たり判定のある平らな板で代わりにする
    /// </summary>
    public class CookingTests
    {
        GameObject routerObject;
        GameObject[] floors;
        CarrotView carrot;
        PanView pan;
        GasBurnerView burner;
        LocalEventSender sender;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(1234);

            routerObject = new GameObject("Router");
            var router = routerObject.AddComponent<EventRouter>();
            sender = new LocalEventSender("test", router);

            floors = new[]
            {
                Floor(KitchenLayout.BoardCenter, 0.36f), // 皿の所にかからない大きさ
                Box(KitchenLayout.PanBottomCenter + Vector3.up * 0.003f, 0.13f), // フライパンの代わり（かべのある四角い箱）
                Floor(KitchenLayout.PlateCenter + Vector3.up * 0.0046f, 0.12f),
            };

            var burnerObject = new GameObject("Burner");
            burnerObject.SetActive(false); // 欄に値を入れてから動かす
            burnerObject.transform.position = KitchenLayout.BurnerCenter;
            burner = burnerObject.AddComponent<GasBurnerView>();
            TestFields.Set(burner, "router", router);
            burnerObject.SetActive(true);

            var panObject = new GameObject("Pan");
            panObject.SetActive(false);
            panObject.transform.position = KitchenLayout.PanBottomCenter;
            pan = panObject.AddComponent<PanView>();
            TestFields.Set(pan, "router", router);
            TestFields.Set(pan, "burner", burner);
            panObject.SetActive(true);

            var carrotObject = new GameObject("Carrot");
            carrotObject.SetActive(false);
            carrot = carrotObject.AddComponent<CarrotView>();
            TestFields.Set(carrot, "router", router);
            TestFields.Set(carrot, "pan", pan);
            TestFields.Set(carrot, "skinMaterial", Colored(new Color(0.93f, 0.43f, 0.08f)));
            TestFields.Set(carrot, "cutMaterial", Colored(new Color(1f, 0.55f, 0.15f)));
            carrotObject.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1;
            sender.Reset(); // フライパンや皿に行ったピースも消す
            Object.Destroy(carrot.gameObject);
            Object.Destroy(pan.gameObject);
            Object.Destroy(burner.gameObject);
            foreach (var floor in floors) Object.Destroy(floor);
            Object.Destroy(routerObject);
        }

        /// <summary>上面が topCenter の、平らな板（当たり判定あり）</summary>
        static GameObject Floor(Vector3 topCenter, float size)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(size, 0.02f, size);
            floor.transform.position = topCenter - new Vector3(0, 0.01f, 0);
            return floor;
        }

        static Material Colored(Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            return material;
        }

        /// <summary>上面が floorCenter で、まわりに高さ 4 cm のかべがある、四角い箱（内側の幅 inside）</summary>
        static GameObject Box(Vector3 floorCenter, float inside)
        {
            var box = Floor(floorCenter, inside + 0.02f);
            for (int i = 0; i < 4; i++)
            {
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bool alongX = i < 2;
                float sign = i % 2 == 0 ? 1 : -1;
                wall.transform.localScale = alongX ? new Vector3(inside + 0.02f, 0.04f, 0.01f) : new Vector3(0.01f, 0.04f, inside + 0.02f);
                wall.transform.position = floorCenter + new Vector3(alongX ? 0 : sign * (inside / 2 + 0.005f), 0.02f, alongX ? sign * (inside / 2 + 0.005f) : 0);
                wall.transform.SetParent(box.transform, true); // 大きさと位置を決めてから入れる（一緒に消えるように）
            }
            return box;
        }

        static float HorizontalDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        IEnumerator CutAndAddAll()
        {
            sender.Cut(4);
            yield return Wait.RealSeconds(0.5f);
            sender.AddToPan(4);
            yield return Wait.Until(() => pan.InPan.Count == 19, 6, "全部のピースがフライパンに入らない");
        }

        [UnityTest]
        public IEnumerator addでまな板のピースが全部フライパンに入る()
        {
            yield return CutAndAddAll();
            yield return Wait.RealSeconds(1f); // 落ち着くまで

            Assert.AreEqual(0, carrot.SlicesOnBoard.Count, "まな板にピースが残っている");
            foreach (var piece in pan.InPan)
            {
                Assert.Less(HorizontalDistance(piece.transform.position, pan.transform.position), 0.095f, $"{piece.name} がフライパンの外にある");
            }
        }

        [UnityTest]
        public IEnumerator 半分だけaddすると_画面のピースも半分くらい入る()
        {
            sender.Cut(4);
            yield return Wait.RealSeconds(0.5f);
            sender.AddToPan(2); // サーバーでは 4 個のうち 2 個

            yield return Wait.RealSeconds(2.5f);
            Assert.That(pan.InPan.Count, Is.InRange(8, 11), "19 個の半分くらい入るはず");
            Assert.AreEqual(19 - pan.InPan.Count, carrot.SlicesOnBoard.Count);

            sender.AddToPan(2); // 残りの 2 個 → 全部
            yield return Wait.Until(() => pan.InPan.Count == 19, 5, "残りが全部入らない");
        }

        [UnityTest]
        public IEnumerator zoneがplateなら皿にのる()
        {
            sender.Cut(4);
            yield return Wait.RealSeconds(0.5f);
            sender.AddToPlate(4);

            yield return Wait.Until(() => pan.OnPlate.Count == 19, 6, "皿にのらない");
            Assert.AreEqual(0, pan.InPan.Count);
        }

        [UnityTest]
        public IEnumerator 火をつけると焼けて色が変わる_消すと止まる()
        {
            yield return CutAndAddAll();
            var piece = pan.InPan[0];
            Color raw = ColorOf(piece);

            Time.timeScale = 10;
            sender.Fire(3);
            yield return Wait.Until(() => pan.AverageDoneness > 0.6f, 6, "焼けない");
            Assert.Less(ColorOf(piece).g, raw.g * 0.9f, "焼けても色が変わっていない（茶色くなっていない）");

            sender.Fire(0);
            yield return Wait.Until(() => pan.Heat == 0, 6, "火を消しても、フライパンが冷めない");
            float stopped = pan.AverageDoneness;
            yield return Wait.RealSeconds(0.3f);
            Assert.AreEqual(stopped, pan.AverageDoneness, 0.0001f, "火を消して冷めたのに、まだ焼けている");
        }

        [UnityTest]
        public IEnumerator 熱いフライパンにピースがあると湯気が上がる()
        {
            yield return CutAndAddAll();
            Time.timeScale = 5;
            sender.Fire(3);
            yield return Wait.Until(() => pan.SteamParticleCount > 5, 4, "湯気が上がらない");
        }

        [UnityTest]
        public IEnumerator serveでフライパンのピースが皿に移る()
        {
            yield return CutAndAddAll();
            sender.Serve(80, "good");

            yield return Wait.Until(() => pan.OnPlate.Count == 19, 6, "皿に移らない");
            Assert.AreEqual(0, pan.InPan.Count);
            yield return Wait.RealSeconds(1f);
            foreach (var piece in pan.OnPlate)
            {
                Assert.Less(HorizontalDistance(piece.transform.position, KitchenLayout.PlateCenter), 0.08f, $"{piece.name} が皿の外にある");
            }
        }

        [UnityTest]
        public IEnumerator resetでフライパンが空になり_にんじんは1本に戻る()
        {
            yield return CutAndAddAll();
            sender.Fire(2);
            yield return null;
            sender.Reset();
            yield return null;

            Assert.AreEqual(0, pan.InPan.Count);
            Assert.AreEqual(0, pan.Heat);
            Assert.AreEqual(0, burner.Level);
            Assert.IsFalse(carrot.IsCut);
            Assert.AreEqual(0, carrot.Slices.Count);
            Assert.IsTrue(carrot.transform.Find("Whole").gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator 切らずにaddすると1本まるごと入り_resetで戻る()
        {
            sender.AddToPan(1);
            yield return Wait.Until(() => pan.InPan.Count == 1, 4, "まるごとの 1 本が入らない");
            Assert.IsNull(carrot.transform.Find("Whole"), "まるごとの 1 本が、まな板に残っている");

            sender.Reset();
            yield return null;
            Assert.IsNotNull(carrot.transform.Find("Whole"), "まるごとの 1 本が戻らない");
            Assert.Less(HorizontalDistance(carrot.transform.position, KitchenLayout.BoardCenter), 0.01f);
        }

        static Color ColorOf(CookablePiece piece)
        {
            var block = new MaterialPropertyBlock();
            var renderer = piece.GetComponent<Renderer>();
            renderer.GetPropertyBlock(block, 0);
            Color color = block.GetColor("_BaseColor");
            return color == default ? renderer.sharedMaterials[0].GetColor("_BaseColor") : color;
        }
    }
}
