namespace Omamagoto.Kitchen
{
    /// <summary>
    /// ガスコンロのバーナーの寸法（mm。天板の上面から測る）と、つまみの角度。
    /// 形を作る KitchenStoveBuilder と、炎を出す GasBurnerView の両方で使うので、ここにまとめる
    /// </summary>
    public static class GasBurnerShape
    {
        /// <summary>炎が出る穴（炎口）が並ぶ輪の半径</summary>
        public const float PortRadius = 37.8f;

        /// <summary>炎口の高さ（真ん中）</summary>
        public const float PortHeight = 15.5f;

        /// <summary>炎口の数</summary>
        public const int PortCount = 32;

        /// <summary>
        /// 火の強さ 0〜3（切・弱・中・強）ごとの、つまみを回す角度（度。前から見て左回り）。
        /// 昔のガステーブルと同じく、左へ回すと「強」で火がつき、さらに回すと弱くなる
        /// </summary>
        public static readonly float[] KnobDegrees = { 0, 150, 120, 90 };
    }
}
