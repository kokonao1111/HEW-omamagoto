using System;
using UnityEngine;

namespace Omamagoto.Kitchen
{
    /// <summary>
    /// 物を、山なりにふわっと飛ばして、目的の位置と向きにおろす。
    /// ピースをフライパンや皿へ入れる、塩・こしょうの入れ物をフライパンの上へ運ぶ、などに使う。
    ///
    /// 例：Flight.Begin(piece, spot, rotation, seconds: 0.7f, arcHeight: 0.1f, delay: 0, onLanded: () => Debug.Log("着いた"));
    ///
    /// 飛んでいるあいだは、重さ（Rigidbody）を止める。着いたら元に戻すので、そこから先は転がって落ち着く。
    /// </summary>
    public class Flight : MonoBehaviour
    {
        Vector3 startPosition;
        Quaternion startRotation;
        Vector3 endPosition;
        Quaternion endRotation;
        float duration;
        float arcHeight;
        float waitLeft;
        float progress;   // 0〜1
        bool started;
        Action onLanded;
        Rigidbody body;

        /// <summary>飛ばす。同じ物がもう飛んでいたら、そちらは止めて、今の位置から飛び直す</summary>
        /// <param name="delay">飛び始めるまでの秒数（いくつかを順に飛ばすとき）</param>
        public static Flight Begin(GameObject target, Vector3 position, Quaternion rotation, float seconds, float arcHeight,
            float delay = 0, Action onLanded = null)
        {
            Stop(target);
            var flight = target.AddComponent<Flight>();
            flight.endPosition = position;
            flight.endRotation = rotation;
            flight.duration = Mathf.Max(0.01f, seconds);
            flight.arcHeight = arcHeight;
            flight.waitLeft = delay;
            flight.onLanded = onLanded;
            flight.body = target.GetComponent<Rigidbody>();
            if (flight.body != null) flight.body.isKinematic = true; // 待っているあいだも、転がらないようにする
            return flight;
        }

        /// <summary>飛んでいる途中なら止める（その場に残る。重さは元に戻す）</summary>
        public static void Stop(GameObject target)
        {
            foreach (var old in target.GetComponents<Flight>())
            {
                old.enabled = false;
                if (old.body != null) old.body.isKinematic = false;
                Destroy(old);
            }
        }

        /// <summary>飛んでいる途中か（待っている時間も含む）</summary>
        public static bool IsFlying(GameObject target)
        {
            foreach (var flight in target.GetComponents<Flight>())
            {
                if (flight.enabled) return true;
            }
            return false;
        }

        void Update()
        {
            if (waitLeft > 0)
            {
                waitLeft -= Time.deltaTime;
                return;
            }
            if (!started)
            {
                // 飛び始める瞬間の位置から飛ぶ（待っているあいだに動いていてもよいように）
                startPosition = transform.position;
                startRotation = transform.rotation;
                started = true;
            }

            progress = Mathf.Min(1, progress + Time.deltaTime / duration);
            float eased = progress * progress * (3 - 2 * progress); // ゆっくり出て、ゆっくり着く
            Vector3 position = Vector3.Lerp(startPosition, endPosition, eased);
            position += Vector3.up * Mathf.Sin(progress * Mathf.PI) * arcHeight;
            transform.SetPositionAndRotation(position, Quaternion.Slerp(startRotation, endRotation, eased));

            if (progress >= 1) Land();
        }

        void Land()
        {
            enabled = false;
            if (body != null)
            {
                body.isKinematic = false;
                body.linearVelocity = Vector3.down * 0.1f; // ぽとっと落とす
                body.angularVelocity = Vector3.zero;
            }
            onLanded?.Invoke();
            Destroy(this);
        }
    }
}
