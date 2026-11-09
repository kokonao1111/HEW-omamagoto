using System;
using System.Collections.Generic;
using Omamagoto.Events;
using Omamagoto.Network;
using UnityEngine;

namespace Omamagoto.Testing
{
    /// <summary>
    /// サーバーがないときに流すデモ。台本は three.js 版（screen/index.html の SCRIPT）と同じ：
    /// 切る → フライパンに入れる → 火をつける → 塩・こしょう → 火を消す → 提供 → リセット（約 40 秒でくり返す）
    ///
    /// - サーバーにつながっていないと、少し待ってから自動で始まる
    /// - サーバーにつながったら止まる
    /// - キーで操作したら止まる（D キーでまた始められる）
    /// </summary>
    [RequireComponent(typeof(EventRouter))]
    public class DemoPlayer : MonoBehaviour
    {
        [Tooltip("サーバーにつながっていないとき、自動でデモを始める")]
        [SerializeField] bool playWhenNoServer = true;

        [Tooltip("自動で始めるまでの待ち時間（秒）。この間にサーバーにつながれば始めない")]
        [SerializeField] float autoPlayDelaySeconds = 3f;

        public bool IsPlaying { get; private set; }

        const float DemoLengthSeconds = 41.5f;
        const float KnifeSendInterval = 0.05f; // 包丁の位置は 1 秒に 20 回送る

        // 包丁の動き。{ 時刻, 刃先 x, 刃先 y, 柄 x, 柄 y }（まな板の範囲で 0〜1。1 より大きいのはまな板の外）
        static readonly float[][] KnifePath =
        {
            new[] { 0.6f, 1.75f, 0.78f, 2.25f, 0.50f },
            new[] { 2.2f, 0.66f, 0.66f, 1.12f, 0.36f },
            new[] { 3.8f, 0.66f, 0.66f, 1.12f, 0.36f },
            new[] { 5.1f, 1.70f, 0.85f, 2.20f, 0.60f },
        };

        /// <summary>台本の 1 行：「この時刻に、これをする」</summary>
        struct Step
        {
            public float Time;
            public Action Action;
        }

        EventRouter router;
        ServerConnection connection;
        LocalEventSender sender;
        readonly List<Step> script = new List<Step>();

        float playTime;          // デモの中の時刻（秒）
        int nextStepIndex;       // 次にする台本の行
        float knifeSendTimer;
        bool stoppedByUser;

        void Awake()
        {
            router = GetComponent<EventRouter>();
            connection = GetComponent<ServerConnection>(); // なくてもよい
            sender = new LocalEventSender("demo", router);
            WriteScript();
        }

        void OnEnable()
        {
            if (connection != null) connection.OnConnected += StopBecauseServerConnected;
        }

        void OnDisable()
        {
            if (connection != null) connection.OnConnected -= StopBecauseServerConnected;
        }

        void Update()
        {
            if (!IsPlaying && ShouldAutoPlay()) Play();
            if (!IsPlaying) return;

            playTime += Time.deltaTime;

            // 時刻が来た台本の行を、順にする
            while (nextStepIndex < script.Count && script[nextStepIndex].Time <= playTime)
            {
                script[nextStepIndex].Action();
                nextStepIndex++;
            }

            SendKnifePositionIfMoving();

            // 最後まで来たら、はじめからくり返す
            if (playTime > DemoLengthSeconds)
            {
                playTime = 0;
                nextStepIndex = 0;
            }
        }

        /// <summary>D キー：デモを始める / 止める</summary>
        public void Toggle()
        {
            if (IsPlaying)
            {
                StopByUser();
            }
            else
            {
                stoppedByUser = false;
                Play();
            }
        }

        /// <summary>人が操作したので止める（自動ではもう始めない）</summary>
        public void StopByUser()
        {
            IsPlaying = false;
            stoppedByUser = true;
        }

        void Play()
        {
            IsPlaying = true;
            playTime = 0;
            nextStepIndex = 0;
        }

        void StopBecauseServerConnected()
        {
            IsPlaying = false;
        }

        bool ShouldAutoPlay()
        {
            bool serverMissing = connection == null || !connection.IsConnected;
            return playWhenNoServer && serverMissing && !stoppedByUser
                && Time.timeSinceLevelLoad > autoPlayDelaySeconds;
        }

        /// <summary>デモの台本（three.js 版と同じ時刻）</summary>
        void WriteScript()
        {
            At(0.0f, () => sender.Reset());
            At(0.2f, () => sender.CarrotPosition(0.48f, 0.5f, 12f));

            // 1 回切る（おもちゃは 2 つに分かれる。画面では一気に輪切りになる）
            At(2.9f, () => sender.Swing());
            At(3.3f, () => sender.Cut(2));
            At(5.3f, () => sender.KnifeHidden());

            // フライパンに入れて、火をつける
            At(9.0f, () => sender.AddToPan(2));
            At(10.4f, () => sender.Fire(2));

            // 塩を 6 回、こしょうを 2 回振る（0.5 g ずつ）
            for (int i = 0; i < 6; i++)
            {
                float total = 0.5f * (i + 1);
                At(11.6f + i * 0.45f, () => sender.Season("salt", 0.5f, total));
            }
            for (int i = 0; i < 2; i++)
            {
                float total = 0.5f * (i + 1);
                At(15.2f + i * 0.45f, () => sender.Season("pepper", 0.5f, total));
            }

            // 火を消して、提供。しばらく見せてからリセット
            At(25.0f, () => sender.Fire(0));
            At(25.6f, () => sender.Serve(86, "good"));
            At(40.5f, () => sender.Reset());

            script.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        void At(float time, Action action)
        {
            script.Add(new Step { Time = time, Action = action });
        }

        /// <summary>包丁が動いている間だけ、KnifePath をなめらかにつないだ位置を送る</summary>
        void SendKnifePositionIfMoving()
        {
            float startTime = KnifePath[0][0];
            float endTime = KnifePath[KnifePath.Length - 1][0];
            if (playTime < startTime || playTime > endTime) return;

            knifeSendTimer += Time.deltaTime;
            if (knifeSendTimer < KnifeSendInterval) return;
            knifeSendTimer = 0;

            // 今の時刻をはさむ 2 つの点を探す
            int i = 0;
            while (i < KnifePath.Length - 2 && KnifePath[i + 1][0] < playTime) i++;
            float[] from = KnifePath[i];
            float[] to = KnifePath[i + 1];

            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from[0], to[0], playTime));
            sender.KnifePosition(
                Mathf.Lerp(from[1], to[1], t), Mathf.Lerp(from[2], to[2], t),
                Mathf.Lerp(from[3], to[3], t), Mathf.Lerp(from[4], to[4], t));
        }
    }
}
