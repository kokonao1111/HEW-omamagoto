using System.Collections;
using NUnit.Framework;
using Omamagoto.Events;
using Omamagoto.Kitchen;
using Omamagoto.Testing;
using UnityEngine;
using UnityEngine.TestTools;

namespace Omamagoto.Tests
{
    /// <summary>カメラで見た物（items）をコンロの上に出す</summary>
    public class StoveItemsTests
    {
        GameObject routerObject;
        StoveItemsView view;
        LocalEventSender sender;

        [SetUp]
        public void SetUp()
        {
            routerObject = new GameObject("Router");
            var router = routerObject.AddComponent<EventRouter>();
            sender = new LocalEventSender("test", router);

            var viewObject = new GameObject("StoveItems");
            viewObject.SetActive(false); // 欄に値を入れてから動かす
            view = viewObject.AddComponent<StoveItemsView>();
            TestFields.Set(view, "router", router);
            viewObject.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(view.gameObject);
            Object.Destroy(routerObject);
        }

        [UnityTest]
        public IEnumerator itemsで_ピーマンとブロッコリーがコンロの上の位置に出る()
        {
            sender.Items("stove",
                new SeenItem { Item = "pepper", X = 0.25f, Y = 0.5f },
                new SeenItem { Item = "broccoli", X = 0.75f, Y = 0.5f },
                new SeenItem { Item = "broccoli", X = 0.6f, Y = 0.2f });
            yield return null;

            Assert.AreEqual(3, view.VisibleCount);
            Vector3 expected = KitchenLayout.StoveAreaToWorld(0.25f, 0.5f);
            Assert.Less(Vector3.Distance(view.Visible("pepper")[0].position, expected), 0.002f, "ピーマンがコンロの上の位置にない");
            Assert.AreEqual(2, view.Visible("broccoli").Count);
        }

        [UnityTest]
        public IEnumerator コンロより手前なら_台の上に置かれる()
        {
            sender.Items("stove", new SeenItem { Item = "pepper", X = 0.5f, Y = 0f });
            yield return null;

            Vector3 position = view.Visible("pepper")[0].position;
            Assert.AreEqual(KitchenLayout.ToWorld(0, 0, 0).y, position.y, 0.001f, "台の上（低い所）にない");
            Assert.Less(position.z, KitchenLayout.ToWorld(0, KitchenLayout.StoveY, 0).z, "コンロより手前にない");
        }

        [UnityTest]
        public IEnumerator フライパンの中に入ると_底まで落ちる()
        {
            // コンロの天板のはし → フライパンの真ん中（バーナーの上）へ。フライパンのふちを越えてから、底まで落ちる
            float panU = KitchenLayout.BurnerX / KitchenLayout.StoveWidth;
            float centerV = (KitchenLayout.StoveY + KitchenLayout.StoveDepth / 2) / (KitchenLayout.StoveY + KitchenLayout.StoveDepth);
            float edgeV = (KitchenLayout.StoveY + 10) / (KitchenLayout.StoveY + KitchenLayout.StoveDepth);
            sender.Items("stove", new SeenItem { Item = "pepper", X = panU, Y = edgeV });
            yield return null;
            Transform pepper = view.Visible("pepper")[0];
            Assert.AreEqual(KitchenLayout.ToWorld(0, 0, KitchenLayout.StoveTopZ).y, pepper.position.y, 0.001f, "コンロの天板の上にない");

            sender.Items("stove", new SeenItem { Item = "pepper", X = panU, Y = centerV });
            bool wentOverRim = false;
            float rim = KitchenLayout.ToWorld(0, 0, KitchenLayout.PanRimZ).y;
            for (int i = 0; i < 30 && !wentOverRim; i++)
            {
                wentOverRim = pepper.position.y > rim - 0.003f;
                yield return null;
            }
            Assert.IsTrue(wentOverRim, "フライパンのふちを越えていない（ふちをすりぬけている）");
            float panFloor = KitchenLayout.ToWorld(0, 0, KitchenLayout.PanFloorZ).y;
            yield return Wait.Until(() => Mathf.Abs(pepper.position.y - panFloor) < 0.001f
                && Vector3.Distance(pepper.position, KitchenLayout.PanBottomCenter) < 0.01f, 2, "フライパンの底まで落ちない");
        }

        [UnityTest]
        public IEnumerator 動いた位置についていき_一覧から消えたらかくれる()
        {
            sender.Items("stove", new SeenItem { Item = "pepper", X = 0.2f, Y = 0.5f });
            yield return null;
            sender.Items("stove", new SeenItem { Item = "pepper", X = 0.8f, Y = 0.5f });
            Vector3 target = KitchenLayout.StoveAreaToWorld(0.8f, 0.5f);
            Transform pepper = view.Visible("pepper")[0];
            yield return Wait.Until(() => Vector3.Distance(pepper.position, target) < 0.002f, 2, "ピーマンが新しい位置へ動かない");

            sender.Items("stove");
            yield return null;
            Assert.AreEqual(0, view.VisibleCount, "一覧にないのに出ている");
        }

        [UnityTest]
        public IEnumerator itemsが届かなくなったら_しばらくしてかくれる()
        {
            sender.Items("stove", new SeenItem { Item = "pepper", X = 0.5f, Y = 0.5f });
            yield return null;
            Assert.AreEqual(1, view.VisibleCount);
            yield return Wait.Until(() => view.VisibleCount == 0, 3, "カメラが止まっても消えない");
        }

        [UnityTest]
        public IEnumerator ほかの場所のitemsとresetでは_出ない_かくれる()
        {
            sender.Items("board", new SeenItem { Item = "pepper", X = 0.5f, Y = 0.5f });
            yield return null;
            Assert.AreEqual(0, view.VisibleCount, "コンロ以外の一覧で出てしまう");

            sender.Items("stove", new SeenItem { Item = "pepper", X = 0.5f, Y = 0.5f });
            yield return null;
            sender.Reset();
            yield return null;
            Assert.AreEqual(0, view.VisibleCount, "リセットで消えない");
        }
    }
}
