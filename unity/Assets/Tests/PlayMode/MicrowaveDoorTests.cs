using System.Collections;
using NUnit.Framework;
using Omamagoto.Events;
using Omamagoto.Kitchen;
using Omamagoto.Testing;
using UnityEngine;
using UnityEngine.TestTools;

namespace Omamagoto.Tests
{
    /// <summary>電子レンジの扉（door で開く・閉まる、reset で閉まる）</summary>
    public class MicrowaveDoorTests
    {
        GameObject routerObject;
        MicrowaveDoorView door;
        Transform doorPanel;
        LocalEventSender sender;

        [SetUp]
        public void SetUp()
        {
            routerObject = new GameObject("Router");
            var router = routerObject.AddComponent<EventRouter>();
            sender = new LocalEventSender("test", router);

            // 蝶番の入れ物と、その右にある扉の板
            var hinge = new GameObject("Door");
            hinge.SetActive(false); // 欄に値を入れてから動かす
            door = hinge.AddComponent<MicrowaveDoorView>();
            TestFields.Set(door, "router", router);
            doorPanel = new GameObject("Panel").transform;
            doorPanel.SetParent(hinge.transform, false);
            doorPanel.localPosition = new Vector3(0.1f, 0, -0.003f);
            hinge.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(door.gameObject);
            Object.Destroy(routerObject);
        }

        [UnityTest]
        public IEnumerator doorで開いて_閉まる()
        {
            yield return null;
            Assert.AreEqual(0, door.Angle, 0.01f, "はじめから開いている");

            sender.Door(true);
            yield return Wait.Until(() => door.Angle > 95, 2, "扉が開かない");
            Assert.IsTrue(door.IsOpen);
            Assert.Less(doorPanel.position.z, -0.05f, "扉が手前に開いていない（奥へ回っている）");

            sender.Door(false);
            yield return Wait.Until(() => door.Angle < 0.1f, 2, "扉が閉まらない");
            Assert.IsFalse(door.IsOpen);
        }

        [UnityTest]
        public IEnumerator angleが届いたら_その角度まで開く()
        {
            sender.DoorAngle(45);
            yield return Wait.Until(() => Mathf.Abs(door.Angle - 45) < 0.5f, 2, "45 度まで開かない");
            Assert.IsTrue(door.IsOpen);

            sender.DoorAngle(20);
            yield return Wait.Until(() => Mathf.Abs(door.Angle - 20) < 0.5f, 2, "20 度まで戻らない");

            sender.DoorAngle(0);
            yield return Wait.Until(() => door.Angle < 0.1f, 2, "閉まらない");
            Assert.IsFalse(door.IsOpen);
        }

        [UnityTest]
        public IEnumerator resetで閉まる()
        {
            sender.Door(true);
            yield return Wait.Until(() => door.Angle > 95, 2, "扉が開かない");

            sender.Reset();
            yield return Wait.Until(() => door.Angle < 0.1f, 2, "リセットで扉が閉まらない");
        }
    }
}
