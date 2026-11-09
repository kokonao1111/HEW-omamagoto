using System.Collections;
using NUnit.Framework;
using Omamagoto.Events;
using Omamagoto.Kitchen;
using Omamagoto.Testing;
using UnityEngine;
using UnityEngine.TestTools;

namespace Omamagoto.Tests
{
    /// <summary>包丁が、イベントのとおりに出て・動き・振り下ろし・消えるか</summary>
    public class KnifeViewTests
    {
        GameObject routerObject;
        KnifeView knife;
        LocalEventSender sender;

        [SetUp]
        public void SetUp()
        {
            routerObject = new GameObject("Router");
            var router = routerObject.AddComponent<EventRouter>();
            sender = new LocalEventSender("test", router);

            var knifeObject = new GameObject("Knife");
            knifeObject.SetActive(false); // 欄に値を入れてから動かす
            knife = knifeObject.AddComponent<KnifeView>();
            TestFields.Set(knife, "router", router);
            knifeObject.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1;
            Object.Destroy(knife.gameObject);
            Object.Destroy(routerObject);
        }

        Transform Pivot => knife.transform.Find("Pivot (handle end)");

        [UnityTest]
        public IEnumerator はじめは見えず_位置が来たら出る()
        {
            yield return null;
            Assert.IsFalse(knife.IsVisible, "カメラが見つける前から出ている");

            sender.KnifePosition(0.3f, 0.5f, 0.7f, 0.5f);
            yield return null;
            Assert.IsTrue(knife.IsVisible);
        }

        [UnityTest]
        public IEnumerator 刃先と柄の真ん中に_柄から刃先の向きで置く()
        {
            // 刃先が奥（y = 0.8）、柄が手前（y = 0.2）。包丁は奥を向く
            sender.KnifePosition(0.5f, 0.8f, 0.5f, 0.2f);
            yield return Wait.RealSeconds(0.5f);

            Vector3 middle = KitchenLayout.BoardToWorld(0.5f, 0.5f);
            Vector3 position = knife.transform.position;
            Assert.Less(Vector2.Distance(new Vector2(middle.x, middle.z), new Vector2(position.x, position.z)), 0.003f, "真ん中にない");
            Assert.Greater(position.y, middle.y, "まな板より下にある");
            Assert.Greater(Vector3.Dot(knife.transform.right, Vector3.forward), 0.99f, "刃先が奥を向いていない");
        }

        [UnityTest]
        public IEnumerator swingで振り下ろして元に戻る()
        {
            sender.KnifePosition(0.3f, 0.5f, 0.7f, 0.5f);
            yield return Wait.RealSeconds(0.3f);
            float restHeight = knife.transform.position.y;

            sender.Swing();
            yield return Wait.RealSeconds(0.08f);
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(0, Pivot.localEulerAngles.z)), 3f, "刃先が上がっていない");

            yield return Wait.RealSeconds(0.6f);
            Assert.AreEqual(0, Mathf.DeltaAngle(0, Pivot.localEulerAngles.z), 0.5f, "元の角度に戻っていない");
            Assert.AreEqual(restHeight, knife.transform.position.y, 0.002f, "元の高さに戻っていない");
        }

        [UnityTest]
        public IEnumerator 見えなくなったら3秒で消える()
        {
            Time.timeScale = 10; // 3 秒を 0.3 秒で
            sender.KnifePosition(0.3f, 0.5f, 0.7f, 0.5f);
            yield return null;

            sender.KnifeHidden();
            yield return Wait.RealSeconds(0.1f);
            Assert.IsTrue(knife.IsVisible, "3 秒たつ前に消えた");

            yield return Wait.Until(() => !knife.IsVisible, 2, "3 秒たっても消えない");
        }

        [UnityTest]
        public IEnumerator resetで消える()
        {
            sender.KnifePosition(0.3f, 0.5f, 0.7f, 0.5f);
            yield return null;
            sender.Reset();
            yield return null;

            Assert.IsFalse(knife.IsVisible);
        }
    }
}
