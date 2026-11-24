using System.Collections;
using NUnit.Framework;
using Omamagoto.Events;
using Omamagoto.Kitchen;
using Omamagoto.Testing;
using UnityEngine;
using UnityEngine.TestTools;

namespace Omamagoto.Tests
{
    /// <summary>ガスコンロの炎（fire）と、塩・こしょうの入れ物（season）</summary>
    public class BurnerAndShakerTests
    {
        GameObject routerObject;
        GasBurnerView burner;
        Transform knob;
        ShakerView salt;
        ShakerView pepper;
        GameObject panFloor;
        LocalEventSender sender;

        [SetUp]
        public void SetUp()
        {
            routerObject = new GameObject("Router");
            var router = routerObject.AddComponent<EventRouter>();
            sender = new LocalEventSender("test", router);

            var burnerObject = new GameObject("Burner");
            burnerObject.SetActive(false); // 欄に値を入れてから動かす
            burnerObject.transform.position = KitchenLayout.BurnerCenter;
            burner = burnerObject.AddComponent<GasBurnerView>();
            knob = new GameObject("Knob").transform;
            TestFields.Set(burner, "router", router);
            TestFields.Set(burner, "knob", knob);
            burnerObject.SetActive(true);

            // 調味料の置き場（まな板の奥）の上
            Vector3 spice = KitchenLayout.ToWorld(KitchenLayout.SpiceX + KitchenLayout.SpiceWidth / 2, KitchenLayout.SpiceY + KitchenLayout.SpiceDepth / 2, 12);
            salt = CreateShaker(router, "salt", spice + new Vector3(-0.045f, 0, 0));
            pepper = CreateShaker(router, "pepper", spice + new Vector3(0.045f, 0, 0));

            // 粒が落ちて止まる所（フライパンの底の代わり）
            panFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panFloor.transform.localScale = new Vector3(0.2f, 0.02f, 0.2f);
            panFloor.transform.position = KitchenLayout.PanBottomCenter - new Vector3(0, 0.01f, 0);
        }

        static ShakerView CreateShaker(EventRouter router, string item, Vector3 home)
        {
            var shakerObject = new GameObject(item);
            shakerObject.SetActive(false);
            shakerObject.transform.position = home;
            var shaker = shakerObject.AddComponent<ShakerView>();
            TestFields.Set(shaker, "router", router);
            TestFields.Set(shaker, "item", item);
            shakerObject.SetActive(true);
            return shaker;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1;
            Object.Destroy(burner.gameObject);
            Object.Destroy(knob.gameObject);
            Object.Destroy(salt.gameObject);
            Object.Destroy(pepper.gameObject);
            Object.Destroy(panFloor);
            Object.Destroy(routerObject);
        }

        [UnityTest]
        public IEnumerator fireで炎がつき_強さが変わり_0で消える()
        {
            yield return null;
            Assert.IsFalse(burner.IsBurning, "はじめから火がついている");

            sender.Fire(2);
            yield return Wait.Until(() => Mathf.Abs(burner.Flame - 2) < 0.05f, 2, "中火にならない");
            Assert.AreEqual(2, burner.Level);
            Assert.IsTrue(burner.transform.Find("Flame 1").gameObject.activeSelf, "炎が見えていない");

            sender.Fire(3);
            yield return Wait.Until(() => burner.Flame > 2.9f, 2, "強火にならない");

            sender.Fire(0);
            yield return Wait.Until(() => !burner.IsBurning, 2, "火が消えない");
            yield return null;
            Assert.IsFalse(burner.transform.Find("Flame 1").gameObject.activeSelf, "火を消したのに炎が見えている");
        }

        [UnityTest]
        public IEnumerator つまみが火の強さの所まで回り_消すと戻る()
        {
            yield return null;
            Assert.AreEqual(0, knob.localEulerAngles.z, 0.5f, "はじめからつまみが回っている");

            sender.Fire(3); // 強：左へ 90 度
            yield return Wait.Until(() => Mathf.Abs(Mathf.DeltaAngle(knob.localEulerAngles.z, 90)) < 1, 2, "強の所まで回らない");

            sender.Fire(1); // 弱：さらに左へ（150 度）
            yield return Wait.Until(() => Mathf.Abs(Mathf.DeltaAngle(knob.localEulerAngles.z, 150)) < 1, 2, "弱の所まで回らない");

            sender.Fire(0);
            yield return Wait.Until(() => Mathf.Abs(Mathf.DeltaAngle(knob.localEulerAngles.z, 0)) < 1, 2, "消すと戻らない");
        }

        [UnityTest]
        public IEnumerator resetで火が消える()
        {
            sender.Fire(3);
            yield return Wait.Until(() => burner.IsBurning, 2, "火がつかない");
            sender.Reset();
            yield return Wait.Until(() => !burner.IsBurning, 2, "リセットしても火が消えない");
        }

        [UnityTest]
        public IEnumerator seasonで入れ物がフライパンの上へ行って振り_粒が出て_元に戻る()
        {
            Vector3 home = salt.transform.position;
            sender.Season("salt", 0.5f, 0.5f);

            yield return Wait.Until(() => salt.IsShaking, 2, "振り始めない");
            Assert.Less(Vector2.Distance(new Vector2(salt.transform.position.x, salt.transform.position.z),
                new Vector2(KitchenLayout.PanBottomCenter.x, KitchenLayout.PanBottomCenter.z)), 0.1f, "フライパンの上にいない");
            Assert.Less(Vector3.Dot(salt.transform.up, Vector3.up), -0.5f, "さかさまになっていない（口が下を向いていない）");
            yield return Wait.Until(() => salt.GrainCount > 0, 1, "粒が出ない");

            Assert.IsTrue(pepper.IsHome, "こしょうまで動いた");

            yield return Wait.Until(() => salt.IsHome, 3, "元の場所に戻らない");
            Assert.Less(Vector3.Distance(home, salt.transform.position), 0.001f);
            Assert.Greater(Vector3.Dot(salt.transform.up, Vector3.up), 0.999f, "まっすぐに戻っていない");
        }

        [UnityTest]
        public IEnumerator 続けて振ると長く振る()
        {
            sender.Season("pepper", 0.5f, 0.5f);
            yield return Wait.Until(() => pepper.IsShaking, 2, "振り始めない");
            float started = Time.time;
            for (int i = 0; i < 3; i++) sender.Season("pepper", 0.5f, 1f + i * 0.5f);

            yield return Wait.Until(() => !pepper.IsShaking, 4, "振り終わらない");
            Assert.Greater(Time.time - started, 1.5f, "続けて届いたのに、振る時間がのびていない");
        }

        [UnityTest]
        public IEnumerator resetで振っている途中でも元に戻る()
        {
            Vector3 home = salt.transform.position;
            sender.Season("salt", 0.5f, 0.5f);
            yield return Wait.Until(() => salt.IsShaking, 2, "振り始めない");

            sender.Reset();
            yield return null;
            Assert.IsTrue(salt.IsHome);
            Assert.Less(Vector3.Distance(home, salt.transform.position), 0.0001f);
            Assert.AreEqual(0, salt.GrainCount);
        }
    }
}
