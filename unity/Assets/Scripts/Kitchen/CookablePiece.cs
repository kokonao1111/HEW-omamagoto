using UnityEngine;

namespace Omamagoto.Kitchen
{
    /// <summary>
    /// 焼ける食材のピース。焼け具合（Doneness）が増えると、色とつやが変わる。
    ///
    /// 焼け具合：0 = 生、1 = ちょうど焼けた、1.4 までは焼けたまま、2.2 = まっ黒にこげた
    /// （three.js 版の採点と同じ目安：0.5 より下は生焼け、1.6 より上は焼きすぎ）
    ///
    /// 色は「生の色 × 焼けた色合い」なので、どの野菜でもそのまま使える（にんじん以外のモデルにも付けられる）。
    /// MeshRenderer のマテリアルは変えずに、ピースごとの色（MaterialPropertyBlock）で変える。
    /// </summary>
    public class CookablePiece : MonoBehaviour
    {
        public const float Cooked = 1f;
        public const float BurningStarts = 1.4f;
        public const float Burnt = 2.2f;

        [Tooltip("焼けたとき、生の色にかける色（茶色っぽくする）")]
        [SerializeField] Color cookedTint = new Color(0.9f, 0.7f, 0.45f);
        [Tooltip("こげたときの色")]
        [SerializeField] Color burntColor = new Color(0.1f, 0.06f, 0.035f);

        MeshRenderer pieceRenderer;
        Color[] rawColors;
        float[] rawSmoothness;
        MaterialPropertyBlock block;

        /// <summary>焼け具合（0 = 生、1 = 焼けた、2.2 = こげた）</summary>
        public float Doneness { get; private set; }

        void Awake()
        {
            pieceRenderer = GetComponent<MeshRenderer>();
            block = new MaterialPropertyBlock();
            Material[] materials = pieceRenderer.sharedMaterials;
            rawColors = new Color[materials.Length];
            rawSmoothness = new float[materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                bool hasMaterial = materials[i] != null;
                rawColors[i] = hasMaterial && materials[i].HasProperty("_BaseColor") ? materials[i].GetColor("_BaseColor") : Color.white;
                rawSmoothness[i] = hasMaterial && materials[i].HasProperty("_Smoothness") ? materials[i].GetFloat("_Smoothness") : 0.5f;
            }
        }

        /// <summary>焼く（焼け具合を amount 増やす）</summary>
        public void Cook(float amount)
        {
            if (amount <= 0) return;
            Doneness = Mathf.Min(Burnt, Doneness + amount);
            UpdateLook();
        }

        void UpdateLook()
        {
            for (int i = 0; i < rawColors.Length; i++)
            {
                pieceRenderer.GetPropertyBlock(block, i);
                block.SetColor("_BaseColor", ColorAt(rawColors[i], cookedTint, burntColor, Doneness));
                block.SetFloat("_Smoothness", SmoothnessAt(rawSmoothness[i], Doneness));
                pieceRenderer.SetPropertyBlock(block, i);
            }
        }

        /// <summary>焼け具合 doneness のときの色</summary>
        public static Color ColorAt(Color raw, Color cookedTint, Color burnt, float doneness)
        {
            Color cooked = raw * cookedTint;
            cooked.a = raw.a;
            if (doneness <= Cooked) return Color.Lerp(raw, cooked, doneness / Cooked);
            if (doneness <= BurningStarts) return cooked;
            return Color.Lerp(cooked, burnt, (doneness - BurningStarts) / (Burnt - BurningStarts));
        }

        /// <summary>焼けると油でつやが出る。こげるとつやがなくなる</summary>
        public static float SmoothnessAt(float raw, float doneness)
        {
            float glossy = Mathf.Min(0.85f, raw + 0.2f);
            if (doneness <= Cooked) return Mathf.Lerp(raw, glossy, doneness / Cooked);
            if (doneness <= BurningStarts) return glossy;
            return Mathf.Lerp(glossy, 0.2f, (doneness - BurningStarts) / (Burnt - BurningStarts));
        }
    }
}
