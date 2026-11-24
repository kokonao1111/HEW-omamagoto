using Omamagoto.Events;
using UnityEngine;

namespace Omamagoto.Kitchen
{
    /// <summary>
    /// 電子レンジの扉。
    ///
    /// - door：angle（度）が届いたら、その角度まで開く（半分だけ開けたら半分だけ開く）。
    ///         angle がなく open だけなら、true でいっぱいに開き、false で閉まる
    /// - reset：閉める
    ///
    /// このコンポーネントは、扉の蝶番（左のはし）に置いた入れ物に付ける。扉の部品はその子にする。
    /// 縦の軸（Y）のまわりに回すと、扉が手前に開く。
    /// </summary>
    public class MicrowaveDoorView : MonoBehaviour
    {
        [SerializeField] EventRouter router;

        [Tooltip("open だけが届いたときに開く角度（度）")]
        [SerializeField] float openDegrees = 100;
        [Tooltip("これより大きい角度は開かない（度）")]
        [SerializeField] float maxDegrees = 110;
        [Tooltip("目標の角度に追いつくまでの時間（秒）のめやす。カメラの角度についていくので短め")]
        [SerializeField] float followSeconds = 0.15f;

        float angle;

        /// <summary>目標の角度（度。イベントで届いた値）</summary>
        public float TargetAngle { get; private set; }

        /// <summary>扉が開いているか（目標の角度が 5 度より大きい）</summary>
        public bool IsOpen => TargetAngle > 5;

        /// <summary>扉の今の角度（度。0 = 閉まっている）</summary>
        public float Angle => angle;

        void OnEnable()
        {
            router.OnDoor += HandleDoor;
            router.OnReset += HandleReset;
        }

        void OnDisable()
        {
            router.OnDoor -= HandleDoor;
            router.OnReset -= HandleReset;
        }

        void HandleDoor(ScreenEvent e)
        {
            if (e.Angle != null)
            {
                TargetAngle = Mathf.Clamp(e.Angle.Value, 0, maxDegrees);
            }
            else if (e.Open != null)
            {
                TargetAngle = e.Open.Value ? openDegrees : 0;
            }
        }

        void HandleReset(ScreenEvent e)
        {
            TargetAngle = 0;
        }

        void Update()
        {
            // 目標の角度に向かって、はじめは速く、最後はゆっくり近づく
            float k = 1 - Mathf.Exp(-Time.deltaTime / (followSeconds / 3));
            angle = Mathf.Lerp(angle, TargetAngle, k);
            if (Mathf.Abs(angle - TargetAngle) < 0.05f) angle = TargetAngle;
            transform.localRotation = Quaternion.Euler(0, angle, 0);
        }
    }
}
