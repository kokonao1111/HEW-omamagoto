using Omamagoto.Events;
using Omamagoto.Network;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Omamagoto.Testing
{
    /// <summary>
    /// キーでイベントを出す（サーバーやカメラがなくても試せるように）。
    /// キーは three.js 版（screen/index.html）と同じ。
    ///
    ///   K       包丁を振り下ろす（swing）
    ///   C       切る（cut。ピースが 1 個増える）
    ///   A       まな板のピースをフライパンに入れる（add）
    ///   S / P   塩 / こしょうを振る（season。0.5 g ずつ）
    ///   0〜3    火の強さ（fire。0=切、1=弱、2=中、3=強）
    ///   Enter   提供（serve）
    ///   M       電子レンジの扉を開ける / 閉める（door）
    ///   R       リセット（サーバーにつながっていれば、サーバーに頼む）
    ///   D       デモを始める / 止める
    /// </summary>
    [RequireComponent(typeof(EventRouter))]
    public class KeyboardEventSender : MonoBehaviour
    {
        const float GramsPerShake = 0.5f;

        EventRouter router;
        ServerConnection connection;
        DemoPlayer demo;
        LocalEventSender sender;

        // 次のイベントの数を決めるために、今の様子を覚えておく（サーバーやデモのイベントでも更新する）
        int pieceCount = 1;       // にんじんのピースの数（切る前は 1 本）
        int piecesOnBoard = 1;    // まな板の上に残っているピースの数
        float saltTotalGrams;
        float pepperTotalGrams;
        bool doorOpen;

        void Awake()
        {
            router = GetComponent<EventRouter>();
            connection = GetComponent<ServerConnection>(); // なくてもよい
            demo = GetComponent<DemoPlayer>();             // なくてもよい
            sender = new LocalEventSender("keyboard", router);
        }

        void OnEnable()
        {
            router.OnCut += RememberCut;
            router.OnAdd += RememberAdd;
            router.OnSeason += RememberSeason;
            router.OnReset += RememberReset;
            router.OnDoor += RememberDoor;
        }

        void OnDisable()
        {
            router.OnCut -= RememberCut;
            router.OnAdd -= RememberAdd;
            router.OnSeason -= RememberSeason;
            router.OnReset -= RememberReset;
            router.OnDoor -= RememberDoor;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.dKey.wasPressedThisFrame)
            {
                if (demo != null) demo.Toggle();
                return;
            }

            if (keyboard.kKey.wasPressedThisFrame) { StopDemo(); sender.Swing(); }
            if (keyboard.cKey.wasPressedThisFrame) { StopDemo(); sender.Cut(pieceCount + 1); }
            if (keyboard.aKey.wasPressedThisFrame) { StopDemo(); sender.AddToPan(Mathf.Max(1, piecesOnBoard)); }
            if (keyboard.sKey.wasPressedThisFrame) { StopDemo(); sender.Season("salt", GramsPerShake, saltTotalGrams + GramsPerShake); }
            if (keyboard.pKey.wasPressedThisFrame) { StopDemo(); sender.Season("pepper", GramsPerShake, pepperTotalGrams + GramsPerShake); }

            if (keyboard.digit0Key.wasPressedThisFrame) { StopDemo(); sender.Fire(0); }
            if (keyboard.digit1Key.wasPressedThisFrame) { StopDemo(); sender.Fire(1); }
            if (keyboard.digit2Key.wasPressedThisFrame) { StopDemo(); sender.Fire(2); }
            if (keyboard.digit3Key.wasPressedThisFrame) { StopDemo(); sender.Fire(3); }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                // 採点はサーバーがする。ここではテスト用に決まった点数を出す
                StopDemo();
                sender.Serve(80, "good");
            }

            if (keyboard.mKey.wasPressedThisFrame) { StopDemo(); sender.Door(!doorOpen); }

            if (keyboard.rKey.wasPressedThisFrame)
            {
                StopDemo();
                bool askedServer = connection != null && connection.SendReset();
                if (!askedServer) sender.Reset(); // サーバーがないときは、自分でリセットを出す
            }
        }

        /// <summary>キーで操作したら、デモは止める（デモとキーがまざらないように）</summary>
        void StopDemo()
        {
            if (demo != null) demo.StopByUser();
        }

        void RememberCut(ScreenEvent e)
        {
            if (e.Count == null) return;
            piecesOnBoard += e.Count.Value - pieceCount;
            pieceCount = e.Count.Value;
        }

        void RememberAdd(ScreenEvent e)
        {
            if (e.Zone == "pan") piecesOnBoard = Mathf.Max(0, piecesOnBoard - (e.Count ?? 0));
        }

        void RememberSeason(ScreenEvent e)
        {
            if (e.TotalGrams == null) return;
            if (e.Item == "salt") saltTotalGrams = e.TotalGrams.Value;
            if (e.Item == "pepper") pepperTotalGrams = e.TotalGrams.Value;
        }

        void RememberDoor(ScreenEvent e)
        {
            if (e.Open != null) doorOpen = e.Open.Value;
        }

        void RememberReset(ScreenEvent e)
        {
            pieceCount = 1;
            piecesOnBoard = 1;
            saltTotalGrams = 0;
            pepperTotalGrams = 0;
            doorOpen = false;
        }
    }
}
