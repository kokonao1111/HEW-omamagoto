namespace Omamagoto.Kitchen
{
    /// <summary>
    /// ガスコンロのバーナーの寸法（mm。ガラスの天板の上面から測る）。
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
    }
}
