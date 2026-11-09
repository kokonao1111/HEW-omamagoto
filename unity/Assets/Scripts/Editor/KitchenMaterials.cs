using System.IO;
using UnityEditor;
using UnityEngine;

namespace Omamagoto.Editor
{
    /// <summary>
    /// キッチンのマテリアル（色・つや・テクスチャ）を作る。Assets/Art/Materials/ に保存する。
    /// もうあれば中身を上書きするので、色やつやを変えたいときは、ここの数字を直して「キッチンを作り直す」。
    ///
    /// 目指す雰囲気は「明るい家のキッチン」（2026-11-09 に決めた）：白い人工大理石の天板、白いタイル、
    /// ステンレス、黒いガラスの IH コンロ。
    ///
    /// テクスチャの Tiling は「1 ÷ テクスチャの実際の大きさ（m）」。形の UV をメートルで作っているので、現実と同じ大きさになる。
    /// </summary>
    public static class KitchenMaterials
    {
        const string MaterialFolder = "Assets/Art/Materials";
        const string TextureFolder = "Assets/Art/Textures";

        // ---- 部屋・台 ----
        public static Material CounterQuartz => Textured("CounterQuartz", "generated/white_quartz_diff.png", null, "generated/white_quartz_mask.png", sizeMeters: 0.6f, smoothness: 1f);
        public static Material CabinetWhite => Plain("CabinetWhite", new Color(0.9f, 0.9f, 0.88f), smoothness: 0.5f);
        public static Material WallTiles => Textured("WallTiles", "generated/white_subway_tiles_diff.png", "generated/white_subway_tiles_nor_gl.png", "generated/white_subway_tiles_mask.png", sizeMeters: 0.6f, smoothness: 1f);
        public static Material WallPaint => Plain("WallPaint", new Color(0.9f, 0.89f, 0.86f), smoothness: 0.15f);

        // ---- 道具 ----
        public static Material BoardOak => Textured("BoardOak", "oak_veneer_01/oak_veneer_01_diff_1k.jpg", "oak_veneer_01/oak_veneer_01_nor_gl_1k.jpg", "oak_veneer_01/oak_veneer_01_mask_1k.png", sizeMeters: 1.83f, smoothness: 0.8f);
        public static Material Stainless => Textured("Stainless", null, "generated/brushed_steel_nor_gl.png", "generated/brushed_steel_mask.png", sizeMeters: 0.25f, smoothness: 1f, color: new Color(0.8f, 0.81f, 0.82f), normalStrength: 0.3f);
        public static Material PlateGlaze => ClearCoat("PlateGlaze", new Color(0.95f, 0.95f, 0.93f), smoothness: 0.75f);
        public static Material PanInside => Plain("PanInside", new Color(0.06f, 0.06f, 0.065f), smoothness: 0.55f, metallic: 0.2f);
        public static Material PanOutside => Plain("PanOutside", new Color(0.14f, 0.14f, 0.15f), smoothness: 0.65f, metallic: 0.6f);
        public static Material BlackResin => Plain("BlackResin", new Color(0.03f, 0.03f, 0.03f), smoothness: 0.55f);
        public static Material Glass => Transparent("Glass", new Color(0.95f, 0.97f, 0.97f, 0.12f), smoothness: 0.97f);
        public static Material Salt => Plain("Salt", new Color(0.96f, 0.96f, 0.94f), smoothness: 0.35f);
        public static Material Pepper => Plain("Pepper", new Color(0.2f, 0.17f, 0.15f), smoothness: 0.3f);

        // ---- 家電 ----
        public static Material BlackGlass => ClearCoat("BlackGlass", new Color(0.015f, 0.015f, 0.018f), smoothness: 0.9f);
        public static Material CooktopPrint => Plain("CooktopPrint", new Color(0.28f, 0.28f, 0.28f), smoothness: 0.9f);
        public static Material ApplianceWhite => Plain("ApplianceWhite", new Color(0.92f, 0.92f, 0.91f), smoothness: 0.6f);
        public static Material ButtonGrey => Plain("ButtonGrey", new Color(0.25f, 0.25f, 0.26f), smoothness: 0.6f);
        public static Material DisplayGlow => Glowing("DisplayGlow", new Color(0.35f, 0.95f, 0.85f), intensity: 1.5f);

        // ---- 包丁（再生したときにスクリプトで形を作る） ----
        public static Material KnifeBlade => Stainless;
        public static Material KnifeHandle => BlackResin;

        // ---- にんじん（仮。野菜はチームで作ったモデルに入れかえる予定） ----
        public static Material CarrotSkin => Plain("CarrotSkin", new Color(0.93f, 0.43f, 0.08f), smoothness: 0.45f);
        public static Material CarrotCut => Plain("CarrotCut", new Color(1f, 0.55f, 0.15f), smoothness: 0.7f);
        public static Material CarrotLeaf => Plain("CarrotLeaf", new Color(0.3f, 0.54f, 0.2f), smoothness: 0.4f);

        // ---------------- 作り方 ----------------

        /// <summary>色だけのマテリアル（URP の Lit）</summary>
        static Material Plain(string name, Color color, float smoothness, float metallic = 0)
        {
            Material material = LoadOrCreate(name, "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// テクスチャのマテリアル（URP の Lit）。パスは Assets/Art/Textures/ から。使わないものは null。
        /// mask は「金属（R）となめらかさ（A）」の画像。なめらかさは mask の A × smoothness になる
        /// </summary>
        static Material Textured(string name, string baseMap, string normalMap, string mask, float sizeMeters,
            float smoothness, Color? color = null, float normalStrength = 1f)
        {
            Material material = LoadOrCreate(name, "Universal Render Pipeline/Lit");
            var tiling = Vector2.one / sizeMeters;

            material.SetColor("_BaseColor", color ?? Color.white);
            material.SetTexture("_BaseMap", baseMap != null ? LoadTexture(baseMap) : null);
            material.SetTextureScale("_BaseMap", tiling);

            material.SetTexture("_BumpMap", normalMap != null ? LoadTexture(normalMap, isNormalMap: true) : null);
            material.SetFloat("_BumpScale", normalStrength);
            SetKeyword(material, "_NORMALMAP", normalMap != null);

            material.SetTexture("_MetallicGlossMap", mask != null ? LoadTexture(mask, isLinear: true) : null);
            material.SetFloat("_SmoothnessTextureChannel", 0); // なめらかさは mask の A から
            SetKeyword(material, "_METALLICSPECGLOSSMAP", mask != null);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0);

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>表面に透明な膜（クリアコート）がある素材。陶器のうわぐすり、ガラスの天板など（URP の Complex Lit）</summary>
        static Material ClearCoat(string name, Color color, float smoothness)
        {
            Material material = LoadOrCreate(name, "Universal Render Pipeline/Complex Lit");
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_ClearCoat", 1);
            material.SetFloat("_ClearCoatMask", 1);
            material.SetFloat("_ClearCoatSmoothness", 0.97f);
            SetKeyword(material, "_CLEARCOAT", true);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>すき通る素材（ガラス）。色の A が不透明さ</summary>
        static Material Transparent(string name, Color color, float smoothness)
        {
            Material material = LoadOrCreate(name, "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0);

            // 透明にする（映り込みは消さない「Preserve Specular」）
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_BlendModePreserveSpecular", 1);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", true);
            SetKeyword(material, "_ALPHAPREMULTIPLY_ON", true);
            material.SetShaderPassEnabled("ShadowCaster", false); // ガラスは濃い影を落とさない
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>光る素材（電子レンジの時計の表示など）</summary>
        static Material Glowing(string name, Color color, float intensity)
        {
            Material material = LoadOrCreate(name, "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", Color.black);
            material.SetColor("_EmissionColor", color * intensity);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            SetKeyword(material, "_EMISSION", true);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void SetKeyword(Material material, string keyword, bool on)
        {
            if (on) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }

        /// <summary>テクスチャを読む。凹凸（法線）や mask は、色の画像とちがう読み込み方の設定にする</summary>
        static Texture2D LoadTexture(string relativePath, bool isNormalMap = false, bool isLinear = false)
        {
            string path = $"{TextureFolder}/{relativePath}";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null)
            {
                bool changed = false;
                if (isNormalMap && importer.textureType != TextureImporterType.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    changed = true;
                }
                if (isLinear && importer.sRGBTexture)
                {
                    importer.sRGBTexture = false; // mask は色ではなく数字なので、色の補正をしない
                    changed = true;
                }
                if (changed) importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material LoadOrCreate(string name, string shaderName)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(shaderName);
            if (material != null)
            {
                if (material.shader != shader) material.shader = shader;
                return material;
            }

            Directory.CreateDirectory(MaterialFolder);
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
