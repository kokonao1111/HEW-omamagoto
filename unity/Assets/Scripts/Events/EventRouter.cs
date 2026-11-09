using System;
using System.Collections.Generic;
using UnityEngine;

namespace Omamagoto.Events
{
    /// <summary>
    /// 届いたイベントを、種類ごとに振り分ける。
    /// 演出を出すスクリプトは、ここの OnCut などに登録して待つ。
    ///
    /// 使い方の例：
    ///   router.OnCut += e => Debug.Log($"ピースが {e.Count} 個になった");
    ///
    /// サーバーからでも、キーやデモからでも、ここを通る。
    /// 画面は判断しない。届いたとおりに演出を出すだけ。
    /// </summary>
    public class EventRouter : MonoBehaviour
    {
        public event Action<ScreenEvent> OnPosition;
        public event Action<ScreenEvent> OnSwing;
        public event Action<ScreenEvent> OnCut;
        public event Action<ScreenEvent> OnAdd;
        public event Action<ScreenEvent> OnSeason;
        public event Action<ScreenEvent> OnFire;
        public event Action<ScreenEvent> OnServe;
        public event Action<ScreenEvent> OnReset;
        public event Action<ScreenEvent> OnStatus;

        /// <summary>どのイベントでも呼ばれる（デバッグの表示などに使う）</summary>
        public event Action<ScreenEvent> OnAnyEvent;

        [Tooltip("届いたイベントを Console に出す")]
        [SerializeField] bool logToConsole = false;

        // 送ったものごとの、最後に受け付けた seq。これ以下が来たら同じものなので捨てる
        readonly Dictionary<string, long> lastSeqByDevice = new Dictionary<string, long>();

        /// <summary>イベントを 1 件受け取って、種類に合う所へ渡す</summary>
        public void Receive(ScreenEvent e)
        {
            if (e == null || string.IsNullOrEmpty(e.Type)) return;
            if (IsDuplicate(e)) return;

            if (logToConsole) Debug.Log($"[イベント] {e}");
            OnAnyEvent?.Invoke(e);

            switch (e.Type)
            {
                case EventNames.Position: OnPosition?.Invoke(e); break;
                case EventNames.Swing: OnSwing?.Invoke(e); break;
                case EventNames.Cut: OnCut?.Invoke(e); break;
                case EventNames.Add: OnAdd?.Invoke(e); break;
                case EventNames.Season: OnSeason?.Invoke(e); break;
                case EventNames.Fire: OnFire?.Invoke(e); break;
                case EventNames.Serve: OnServe?.Invoke(e); break;
                case EventNames.Reset: OnReset?.Invoke(e); break;
                case EventNames.Status: OnStatus?.Invoke(e); break;
                default: break; // 知らない種類は無視する（サーバーが新しい種類を足しても止まらないように）
            }
        }

        /// <summary>
        /// 覚えている seq を全部忘れる。サーバーにつなぎ直したときに呼ぶ
        /// （サーバーは起動するたびに seq を 1 から数え直すため）
        /// </summary>
        public void ForgetSeq()
        {
            lastSeqByDevice.Clear();
        }

        bool IsDuplicate(ScreenEvent e)
        {
            if (e.Seq == null || string.IsNullOrEmpty(e.Device)) return false;

            long seq = e.Seq.Value;
            if (lastSeqByDevice.TryGetValue(e.Device, out long lastSeq) && seq <= lastSeq)
            {
                return true;
            }

            lastSeqByDevice[e.Device] = seq;
            return false;
        }
    }
}
