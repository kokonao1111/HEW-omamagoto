using UnityEngine;

namespace Omamagoto.Kitchen
{
    /// <summary>
    /// 現実のキッチン台の寸法（mm）。画面のキッチンを、現実と同じ配置にするために使う。
    ///
    /// 2026-11-24：作っている実物の台に合わせた（ユーザーが測った）。横幅 900 mm・奥行き 340 mm。
    /// 左はしから 240 mm が電子レンジ、右はしから 260 mm がコンロ（1 口）、残りの真ん中がまな板。
    /// 皿は電子レンジの手前、塩・こしょうはまな板の奥（空いている所。ユーザーと決めた）。
    /// tools/kitchen_plan.py の P（設計図。横 1050・奥行き 550）とは、ちがう数字になっている。
    ///
    /// 現実の座標（mm）：台の左手前が原点。x は右へ、y は奥へ、z は天板の上面から上へ。
    /// Unity の座標（m） ：x は右へ、y は上へ、z は奥へ。1 = 1 m。
    /// </summary>
    public static class KitchenLayout
    {
        // ---- 本体 ----
        public const float Width = 900;       // 横幅（実物）
        public const float Depth = 340;       // 奥行き（実物。背板まで）
        public const float BaseHeight = 150;  // base_h：土台の高さ（机の上面 → 天板の上面）
        public const float BackHeight = 620;  // back_h：背板の高さ（天板の上面 → 背板の上端）
        public const float WingDepth = 150;   // wing_d：袖板の奥行き
        public const float PlyThickness = 15; // T：合板の厚さ

        // ---- まな板（おもちゃ。真ん中の手前。電子レンジとコンロの間 240〜640 mm の真ん中に置く） ----
        public const float BoardX = 290, BoardY = 10, BoardWidth = 300, BoardDepth = 220, BoardThickness = 20;

        // ---- 皿を置く所（電子レンジの手前） ----
        public const float PlateX = 40, PlateY = 5, PlateWidth = 160, PlateDepth = 160;

        // ---- コンロ（右はしの 260 mm に置く、1 口。横 230・奥行き 300） ----
        public const float StoveX = 655, StoveY = 20, StoveWidth = 230, StoveDepth = 300;
        public const float BurnerX = StoveWidth / 2; // コンロの左端 → バーナーの中心（1 口なので真ん中）
        public const float BurnerRadius = 60;
        public const float PanRadius = 90;   // フライパン（おもちゃ）の半径
        // 画面では、台に置く箱型のガステーブル（つまみ式）にしたので、フライパンは五徳の上（StoveTopZ・TrivetTopZ）。
        // 左右・奥行きの位置は、現実と同じ

        // ---- 電子レンジ（おもちゃを置く。左はしの奥。横 240・奥行き 170） ----
        public const float MicrowaveX = 0, MicrowaveY = 170, MicrowaveWidth = 240, MicrowaveDepth = 170, MicrowaveHeight = 230;

        // ---- 調味料の置き場（まな板の奥） ----
        public const float SpiceX = 350, SpiceY = 230, SpiceWidth = 180, SpiceDepth = 110, SpiceHeight = 130;

        /// <summary>
        /// まな板の座標の向き。true なら、カメラの y = 0 がまな板の手前の縁。
        /// （カメラのキャリブレーションで決まる向き。three.js 版の V_FROM_FRONT と同じ）
        /// </summary>
        public const bool BoardYFromFront = true;

        /// <summary>現実の座標（mm）→ Unity の位置（m）</summary>
        public static Vector3 ToWorld(float xMm, float yMm, float zMm)
        {
            return new Vector3(xMm, zMm, yMm) / 1000f;
        }

        /// <summary>
        /// まな板の座標（カメラが送ってくる 0〜1）→ まな板の上面の Unity の位置（m）。
        /// 1 より大きい・0 より小さい値は、まな板の外（そのまま外に置く）。
        /// </summary>
        public static Vector3 BoardToWorld(float u, float v)
        {
            float depthRatio = BoardYFromFront ? v : 1 - v;
            return ToWorld(BoardX + u * BoardWidth, BoardY + depthRatio * BoardDepth, BoardThickness);
        }

        /// <summary>まな板の真ん中（上面）</summary>
        public static Vector3 BoardCenter => BoardToWorld(0.5f, 0.5f);

        /// <summary>皿の真ん中（天板の上）</summary>
        public static Vector3 PlateCenter => ToWorld(PlateX + PlateWidth / 2, PlateY + PlateDepth / 2, 0);

        /// <summary>
        /// 画面のガステーブルの天板の上面の高さ（mm）。
        /// 本物（幅 56 cm・天板まで約 13 cm）を小さくした高さ（2026-11-10 に決めた。1 口・横 230 mm にした 11-24 も、高さはそのまま）
        /// </summary>
        public const float StoveTopZ = 90;

        /// <summary>五徳の上面＝フライパンの底の高さ（mm）。バーナーと炎の上に、すき間をあける</summary>
        public const float TrivetTopZ = StoveTopZ + 34;

        /// <summary>バーナーの真ん中（天板の上面）</summary>
        public static Vector3 BurnerCenter => ToWorld(StoveX + BurnerX, StoveY + StoveDepth / 2, StoveTopZ);

        /// <summary>フライパンの底の真ん中（バーナーの五徳の上）</summary>
        public static Vector3 PanBottomCenter => BurnerCenter + Vector3.up * (TrivetTopZ - StoveTopZ) / 1000f;

        /// <summary>
        /// コンロのあたりの位置 → Unity の位置。カメラで見た物（items の zone: "stove"）を置くのに使う。
        /// u：コンロの左 0 → 右 1。v：台のいちばん手前 0 → コンロの奥 1（コンロの手前の台の上も入る）。
        /// 高さは、そこに物を置いたときにのる面（StoveSurfaceZ）
        /// </summary>
        public static Vector3 StoveAreaToWorld(float u, float v)
        {
            float x = StoveX + u * StoveWidth, y = v * (StoveY + StoveDepth);
            return ToWorld(x, y, StoveSurfaceZ(x, y));
        }

        /// <summary>フライパンの中の底の高さ（mm）。五徳の上にのったフライパンの、底の板の厚さの分だけ上（KitchenToolBuilder の形と同じ）</summary>
        public const float PanFloorZ = TrivetTopZ + 3;

        /// <summary>フライパンのふちの上の高さ（mm）</summary>
        public const float PanRimZ = TrivetTopZ + 42;

        /// <summary>フライパンの中（ふちの内側）とみなす半径（mm）</summary>
        public const float PanInnerRadius = PanRadius - 10;

        /// <summary>フライパンの真ん中（現実の位置 mm。バーナーの上）</summary>
        public static Vector2 PanCenterMm => new Vector2(StoveX + BurnerX, StoveY + StoveDepth / 2);

        /// <summary>
        /// コンロのあたりで、物を置いたときにのる面の高さ（mm）。x・y は現実の位置（mm）。
        /// フライパンの中 → フライパンの底、ガステーブルのほかの所 → 天板、その外 → 台
        /// </summary>
        public static float StoveSurfaceZ(float x, float y)
        {
            if (Distance(x, y, PanCenterMm.x, PanCenterMm.y) < PanInnerRadius) return PanFloorZ;  // ふちの内側
            bool onStove = x >= StoveX && x <= StoveX + StoveWidth && y >= StoveY && y <= StoveY + StoveDepth;
            return onStove ? StoveTopZ : 0;
        }

        static float Distance(float x1, float y1, float x2, float y2)
        {
            return Mathf.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
        }
    }
}
