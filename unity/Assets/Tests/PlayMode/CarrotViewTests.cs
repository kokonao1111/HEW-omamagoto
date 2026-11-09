using System.Collections;
using System.Linq;
using NUnit.Framework;
using Omamagoto.Events;
using Omamagoto.Kitchen;
using Omamagoto.Kitchen.Shapes;
using Omamagoto.Testing;
using UnityEngine;
using UnityEngine.TestTools;

namespace Omamagoto.Tests
{
    /// <summary>にんじんが、イベントのとおりに動き・切れ・戻るか</summary>
    public class CarrotViewTests
    {
        GameObject routerObject;
        GameObject board;
        CarrotView carrot;
        LocalEventSender sender;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(1234); // 散らばり方を毎回同じにする

            routerObject = new GameObject("Router");
            var router = routerObject.AddComponent<EventRouter>();
            sender = new LocalEventSender("test", router);

            // まな板（ピースが落ちないように、当たり判定のある板）
            board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.transform.position = KitchenLayout.BoardCenter - new Vector3(0, 0.01f, 0);
            board.transform.localScale = new Vector3(0.6f, 0.02f, 0.6f);

            var carrotObject = new GameObject("Carrot");
            carrotObject.SetActive(false); // 欄に値を入れてから動かす
            carrot = carrotObject.AddComponent<CarrotView>();
            TestFields.Set(carrot, "router", router);
            carrotObject.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1;
            Object.Destroy(carrot.gameObject);
            Object.Destroy(board);
            Object.Destroy(routerObject);
        }

        GameObject WholeCarrot => carrot.transform.Find("Whole").gameObject;

        [UnityTest]
        public IEnumerator はじめはまな板の真ん中に1本ある()
        {
            yield return null;

            Assert.IsFalse(carrot.IsCut);
            Assert.IsTrue(WholeCarrot.activeSelf);
            Assert.Less(Vector3.Distance(KitchenLayout.BoardCenter, carrot.transform.position), 0.03f);
        }

        [UnityTest]
        public IEnumerator 最初のcutで輪切りになる()
        {
            sender.Cut(2);
            yield return null;

            Assert.IsTrue(carrot.IsCut);
            Assert.IsFalse(WholeCarrot.activeSelf, "まるごとの 1 本が消えていない");
            Assert.AreEqual(19, carrot.Slices.Count, "輪切り 18 枚 ＋ 葉の付いた端 1 個 になっていない");
            Assert.IsTrue(carrot.Slices.All(s => s.GetComponent<Rigidbody>() != null), "ピースに重さが付いていない");
        }

        [UnityTest]
        public IEnumerator 二回目からのcutではピースは増えない()
        {
            sender.Cut(2);
            yield return null;
            sender.Cut(3);
            sender.Cut(4);
            yield return null;

            Assert.AreEqual(19, carrot.Slices.Count);
        }

        [UnityTest]
        public IEnumerator 切ったピースはバラッと倒れる()
        {
            sender.Cut(2);
            yield return Wait.RealSeconds(2f);

            // 輪切りが倒れて平らになると、ピースの X 軸（にんじんの軸）が上を向く
            var rings = carrot.Slices.Where(s => s.name.StartsWith("Slice")).ToList();
            int lyingFlat = rings.Count(s => Mathf.Abs(Vector3.Dot(s.transform.right, Vector3.up)) > 0.7f);
            string detail = string.Join(", ", rings.Select(s => $"y={s.transform.position.y:F3} 軸の上向き={s.transform.right.y:F2}"));
            Assert.GreaterOrEqual(lyingFlat, rings.Count / 2, $"倒れた輪切りが {lyingFlat} / {rings.Count} 枚しかない：{detail}");

            // まな板から落ちていない
            Assert.IsTrue(rings.All(s => s.transform.position.y > KitchenLayout.BoardCenter.y - 0.01f), "まな板から落ちたピースがある");
        }

        [UnityTest]
        public IEnumerator resetで1本に戻る()
        {
            sender.Cut(2);
            yield return null;
            sender.Reset();
            yield return null;

            Assert.IsFalse(carrot.IsCut);
            Assert.AreEqual(0, carrot.Slices.Count);
            Assert.IsTrue(WholeCarrot.activeSelf);
        }

        [UnityTest]
        public IEnumerator positionの場所へなめらかに動く()
        {
            sender.CarrotPosition(0.1f, 0.9f, 30f);
            yield return Wait.RealSeconds(1f);

            Vector3 expected = KitchenLayout.BoardToWorld(0.1f, 0.9f) + Vector3.up * CarrotShape.MaxRadius;
            Assert.Less(Vector3.Distance(expected, carrot.transform.position), 0.005f);
            Assert.AreEqual(-30f, Mathf.DeltaAngle(0, carrot.transform.eulerAngles.y), 1f);
        }

        [UnityTest]
        public IEnumerator 見えなくなったら3秒で消える()
        {
            Time.timeScale = 10; // 3 秒を 0.3 秒で
            sender.CarrotPosition(0.5f, 0.5f, 0);
            yield return null;

            sender.CarrotHidden();
            yield return Wait.RealSeconds(0.1f);
            Assert.IsTrue(WholeCarrot.activeSelf, "3 秒たつ前に消えた");

            yield return Wait.Until(() => !WholeCarrot.activeSelf, 2, "3 秒たっても消えない");

            sender.CarrotPosition(0.5f, 0.5f, 0); // また見えた
            yield return null;
            Assert.IsTrue(WholeCarrot.activeSelf);
        }
    }
}
