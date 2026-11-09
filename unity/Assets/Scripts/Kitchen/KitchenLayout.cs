using UnityEngine;

namespace Omamagoto.Kitchen
{
    /// <summary>
    /// 現実のキッチン台の寸法（mm）。画面のキッチンを、現実と同じ配置にするために使う。
    /// 数字は tools/kitchen_plan.py の P と同じ。P を変えたら、ここも同じに直す。
    ///
    /// 現実の座標（mm）：台の左手前が原点。x は右へ、y は奥へ、z は天板の上面から上へ。
    /// Unity の座標（m） ：x は右へ、y は上へ、z は奥へ。1 = 1 m。
    /// </summary>
    public static class KitchenLayout
    {
        // ---- 本体 ----
        public const float Width = 1050;      // W：幅
        public const float Depth = 550;       // D：奥行き（背板まで）
        public const float BaseHeight = 150;  // base_h：土台の高さ（机の上面 → 天板の上面）
        public const float BackHeight = 620;  // back_h：背板の高さ（天板の上面 → 背板の上端）
        public const float WingDepth = 150;   // wing_d：袖板の奥行き
        public const float PlyThickness = 15; // T：合板の厚さ

        // ---- まな板（おもちゃ。天板に置く） ----
        public const float BoardX = 90, BoardY = 90, BoardWidth = 300, BoardDepth = 220, BoardThickness = 20;

        // ---- 皿を置く所 ----
        public const float PlateX = 410, PlateY = 100, PlateWidth = 160, PlateDepth = 160;

        // ---- コンロ（天板の上に合板を 1 枚重ねる。2 口） ----
        public const float StoveX = 600, StoveY = 60, StoveWidth = 400, StoveDepth = 300;
        public const float LeftBurnerX = 100, RightBurnerX = 300; // コンロ板の左端 → バーナーの中心
        public const float BurnerRadius = 60;
        public const float PanRadius = 90;   // フライパン（おもちゃ）の半径
        public const float PanHeight = 25;   // pan_z：コンロ板の上面 → フライパンの底

        // ---- 電子レンジ（おもちゃを置く） ----
        public const float MicrowaveX = 30, MicrowaveY = 320, MicrowaveWidth = 280, MicrowaveDepth = 200, MicrowaveHeight = 230;

        // ---- 調味料の置き場 ----
        public const float SpiceX = 400, SpiceY = 330, SpiceWidth = 180, SpiceDepth = 110, SpiceHeight = 130;

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

        /// <summary>コンロ板の上面の高さ（mm）</summary>
        public const float StoveTopZ = PlyThickness;

        /// <summary>バーナーの真ん中（コンロ板の上面）。right が true なら右のバーナー</summary>
        public static Vector3 BurnerCenter(bool right)
        {
            float x = StoveX + (right ? RightBurnerX : LeftBurnerX);
            return ToWorld(x, StoveY + StoveDepth / 2, StoveTopZ);
        }

        /// <summary>フライパンの底の真ん中（右のバーナーの上）</summary>
        public static Vector3 PanBottomCenter => BurnerCenter(right: true) + ToWorld(0, 0, PanHeight);
    }
}
