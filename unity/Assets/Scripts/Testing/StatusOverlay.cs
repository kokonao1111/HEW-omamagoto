using System.Collections.Generic;
using Omamagoto.Events;
using Omamagoto.Network;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Omamagoto.Testing
{
    /// <summary>
    /// 画面の左上に、サーバーとのつながりを小さく出す。
    /// 「届いたイベント」を出すと、演出がまだなくても動いているか確かめられる（F1 キーで出す / 消す）。
    /// </summary>
    [RequireComponent(typeof(EventRouter))]
    public class StatusOverlay : MonoBehaviour
    {
        [Tooltip("届いたイベントを、新しいものから何件か出す（F1 キーで切りかえ）")]
        [SerializeField] bool showRecentEvents = true;

        [SerializeField] int recentEventCount = 8;

        EventRouter router;
        ServerConnection connection;
        DemoPlayer demo;
        readonly Queue<string> recentEvents = new Queue<string>();
        GUIStyle textStyle;

        void Awake()
        {
            router = GetComponent<EventRouter>();
            connection = GetComponent<ServerConnection>(); // なくてもよい
            demo = GetComponent<DemoPlayer>();             // なくてもよい
        }

        void OnEnable() { router.OnAnyEvent += Remember; }
        void OnDisable() { router.OnAnyEvent -= Remember; }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            {
                showRecentEvents = !showRecentEvents;
            }
        }

        void Remember(ScreenEvent e)
        {
            // 包丁の位置は 1 秒に 20 回も来るので、出さない（ほかのイベントが流れて見えなくなるため）
            if (e.Type == EventNames.Position && e.Item == "knife" && e.Visible == true) return;

            recentEvents.Enqueue($"{e.Device} {e.Type} {Describe(e)}");
            while (recentEvents.Count > recentEventCount) recentEvents.Dequeue();
        }

        /// <summary>イベントの中身を短く文にする</summary>
        static string Describe(ScreenEvent e)
        {
            switch (e.Type)
            {
                case EventNames.Cut: return $"ピース {e.Count} 個";
                case EventNames.Add: return $"{e.Zone} に {e.Count} 個";
                case EventNames.Season: return $"{e.Item} {e.AmountGrams} g（合計 {e.TotalGrams} g）";
                case EventNames.Fire: return $"火 {e.Level}";
                case EventNames.Serve: return $"{e.Score} 点 {e.Comment}";
                case EventNames.Position: return e.Visible == false ? $"{e.Item} 見えなくなった" : $"{e.Item} ({e.X}, {e.Y})";
                default: return "";
            }
        }

        string StatusText()
        {
            if (demo != null && demo.IsPlaying) return "デモ再生中（キーを押すと止まる）";
            if (connection == null) return "サーバーなし";

            switch (connection.State)
            {
                case ConnectionState.Connected: return $"サーバー接続中　{connection.ServerUrl}";
                case ConnectionState.Reconnecting: return "再接続中…";
                default: return $"サーバーを待っています　{connection.ServerUrl}";
            }
        }

        void OnGUI()
        {
            if (textStyle == null)
            {
                textStyle = new GUIStyle(GUI.skin.label) { fontSize = 14 };
                textStyle.normal.textColor = Color.white;
            }

            var lines = new List<string> { StatusText() };
            if (showRecentEvents) lines.AddRange(recentEvents);

            float lineHeight = 20f;
            var area = new Rect(8, 8, 560, lines.Count * lineHeight + 8);
            GUI.Box(area, GUIContent.none);
            for (int i = 0; i < lines.Count; i++)
            {
                GUI.Label(new Rect(area.x + 6, area.y + 4 + i * lineHeight, area.width - 12, lineHeight), lines[i], textStyle);
            }
        }
    }
}
