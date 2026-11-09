using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Omamagoto.Editor
{
    /// <summary>
    /// キッチンのマテリアル（色・つや・テクスチャ）を作る。Assets/Art/Materials/ に保存する。
    /// もうあれば中身を上書きするので、色やつやを変えたいときは、ここの数字を直して「キッチンを作り直す」。
    ///
    /// 目指す雰囲気は「明るい家のキッチン」（2026-11-09 に決めた）：白い人工大理石の天板、白い角タイル（昔ながらの 10 cm 角）、
    /// ステンレス、昔ながらのつまみ式のガステーブル（2026-11-10 に決めた）。
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
        // 壁：昔ながらの台所の、10 cm 角の白いタイル（少し使い込んだ感じ。ambientCG の Tiles133B。画像 1 枚に 12 × 12 枚）
        public static Material WallTiles => Textured("WallTiles", "tiles133b/tiles133b_color_2k.jpg", "tiles133b/tiles133b_nor_gl_2k.jpg", "tiles133b/tiles133b_mask_2k.png", sizeMeters: 1.2f, smoothness: 1f, color: new Color(1f, 0.96f, 0.92f));
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

        // ---- ガスコンロ（台に置く、昔ながらのつまみ式のガステーブル） ----
        public static Material StoveBody => Plain("StoveBody", new Color(0.84f, 0.81f, 0.74f), smoothness: 0.55f);           // アイボリーの塗装
        public static Material StoveTopEnamel => Plain("StoveTopEnamel", new Color(0.6f, 0.6f, 0.6f), smoothness: 0.72f, metallic: 0.45f); // シルバーのホーロー
        public static Material StovePrint => Plain("StovePrint", new Color(0.03f, 0.03f, 0.03f), smoothness: 0.4f);
        public static Material GrillDoor => ClearCoat("GrillDoor", new Color(0.015f, 0.015f, 0.017f), smoothness: 0.85f);
        public static Material KnobBlack => Plain("KnobBlack", new Color(0.022f, 0.022f, 0.022f), smoothness: 0.62f);   // 黒い樹脂
        public static Material KnobMark => Plain("KnobMark", new Color(0.9f, 0.9f, 0.88f), smoothness: 0.5f);
        public static Material Chrome => Plain("Chrome", new Color(0.95f, 0.95f, 0.95f), smoothness: 0.93f, metallic: 1f);
        public static Material GlassTop => ClearCoat("GasGlassTop", new Color(0.02f, 0.02f, 0.024f), smoothness: 0.9f);  // グリルののぞき窓・電子レンジの表示の窓
        public static Material CastIron => Textured("CastIron", null, "generated/cast_iron_nor_gl.png", "generated/cast_iron_mask.png", sizeMeters: 0.1f, smoothness: 1f, color: new Color(0.06f, 0.06f, 0.06f), normalStrength: 0.7f);
        public static Material BurnerEnamel => ClearCoat("BurnerEnamel", new Color(0.018f, 0.018f, 0.02f), smoothness: 0.75f);
        public static Material BurnerAluminum => Plain("BurnerAluminum", new Color(0.5f, 0.5f, 0.5f), smoothness: 0.45f, metallic: 0.9f);
        public static Material BurnerPort => Plain("BurnerPort", new Color(0.01f, 0.01f, 0.01f), smoothness: 0.2f);
        public static Material IgniterCeramic => Plain("IgniterCeramic", new Color(0.93f, 0.92f, 0.88f), smoothness: 0.75f);
        public static Material GasFlame => Particle("GasFlame", "generated/flame_gas.png", ParticleBlend.Add, new Color(1.6f, 1.6f, 1.8f));

        // ---- 電子レンジ ----
        public static Material ApplianceWhite => Plain("ApplianceWhite", new Color(0.92f, 0.92f, 0.91f), smoothness: 0.62f);
        public static Material SmokedGlass => Transparent("SmokedGlass", new Color(0.02f, 0.02f, 0.025f, 0.6f), smoothness: 0.97f);
        public static Material DoorPrint => Plain("DoorPrint", new Color(0.012f, 0.012f, 0.014f), smoothness: 0.5f);
        public static Material MicrowaveMesh => PerforatedMetal("MicrowaveMesh", "generated/microwave_mesh.png", sizeMeters: 0.03f, smoothness: 0.4f, metallic: 0.6f);
        public static Material MicrowaveCavity => Glowing("MicrowaveCavity", new Color(1f, 0.82f, 0.55f), intensity: 0f, baseColor: new Color(0.3f, 0.3f, 0.29f));
        public static Material MicrowaveDisplay => Glowing("MicrowaveDisplay", Color.white, intensity: 1.6f, emissionMap: "generated/microwave_display.png", mapSizeMeters: MicrowaveDisplaySize);

        /// <summary>電子レンジの時計の表示の大きさ（m）。表示の画像 1 枚を、ちょうどこの大きさに貼る</summary>
        public static readonly Vector2 MicrowaveDisplaySize = new Vector2(0.068f, 0.013f);
        public static Material SilverPlastic => Plain("SilverPlastic", new Color(0.72f, 0.72f, 0.74f), smoothness: 0.6f, metallic: 0.85f);
        public static Material ButtonLight => Plain("ButtonLight", new Color(0.82f, 0.82f, 0.81f), smoothness: 0.55f);
        public static Material VentHole => Plain("VentHole", new Color(0.012f, 0.012f, 0.012f), smoothness: 0.1f);
        public static Material Rubber => Plain("Rubber", new Color(0.04f, 0.04f, 0.04f), smoothness: 0.25f);

        // ---- 湯気・塩・こしょう（粒やもや） ----
        public static Material Steam => Particle("Steam", "generated/steam_puff.png", ParticleBlend.Alpha, new Color(1f, 1f, 1f, 1f));
        public static Material SaltGrain => Particle("SaltGrain", "generated/grain.png", ParticleBlend.Cutout, new Color(0.95f, 0.95f, 0.93f));
        public static Material PepperGrain => Particle("PepperGrain", "generated/grain.png", ParticleBlend.Cutout, new Color(0.13f, 0.11f, 0.1f));

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

        /// <summary>
        /// 光る素材（電子レンジの時計の表示・コンロのランプなど）。
        /// emissionMap を渡すと、その画像の明るい所だけ光る。
        /// intensity が 0 なら、消えているときの色（baseColor）にしておき、再生中にスクリプトが _BaseColor を変えて光らせる
        /// </summary>
        static Material Glowing(string name, Color color, float intensity, Color? baseColor = null, string emissionMap = null,
            Vector2? mapSizeMeters = null)
        {
            // 光の計算をしない Unlit にして、色を 1 より明るくする（光って見え、Bloom でにじむ）。
            // Lit の「Emission」は、読み込み直すと _EMISSION のスイッチが切れてしまうので使わない（2026-11-09 に確かめた）
            Material material = LoadOrCreate(name, "Universal Render Pipeline/Unlit");
            Color off = baseColor ?? Color.black;
            material.SetColor("_BaseColor", intensity > 0 ? color * intensity : off);
            material.SetTexture("_BaseMap", emissionMap != null ? LoadTexture(emissionMap) : null);
            material.SetTextureScale("_BaseMap", mapSizeMeters.HasValue ? new Vector2(1 / mapSizeMeters.Value.x, 1 / mapSizeMeters.Value.y) : Vector2.one);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 穴のあいた金属の板（電子レンジの窓）。画像の A = 0 の所が穴で、奥が透けて見える。
        ///
        /// 穴は画面の 1〜2 画素くらいしかないので、切り抜き（Cutout）にすると横じま（モアレ）が出る。
        /// 半透明にして小さい画像（ミップマップ）を使うと、遠くでは穴がならされて、なめらかに暗く透ける
        /// </summary>
        static Material PerforatedMetal(string name, string baseMap, float sizeMeters, float smoothness, float metallic)
        {
            Material material = LoadOrCreate(name, "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", LoadTexture(baseMap, noStripes: true));
            material.SetTextureScale("_BaseMap", Vector2.one / sizeMeters);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);

            // 半透明（A で混ぜる）。URP が保存のときに自動で整える値と同じにしておく
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_AlphaClip", 0);
            material.SetFloat("_QueueOffset", -1); // 手前の黒いガラスより先に描く
            material.SetOverrideTag("RenderType", "Transparent");
            SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", true);
            SetKeyword(material, "_ALPHAPREMULTIPLY_ON", true);
            SetKeyword(material, "_ALPHATEST_ON", false);
            material.renderQueue = (int)RenderQueue.Transparent - 1;
            EditorUtility.SetDirty(material);
            return material;
        }

        enum ParticleBlend { Alpha, Add, Cutout }

        /// <summary>
        /// 粒やもや・炎の素材。光の計算をしない（URP の Particles/Unlit）。
        /// Add は足し算（炎）、Alpha は半透明（湯気）、Cutout は画像の A で切り抜く（塩・こしょうの粒）
        /// </summary>
        static Material Particle(string name, string baseMap, ParticleBlend blend, Color color)
        {
            Material material = LoadOrCreate(name, "Universal Render Pipeline/Particles/Unlit");
            material.SetTexture("_BaseMap", LoadTexture(baseMap));
            material.SetColor("_BaseColor", color);

            bool transparent = blend != ParticleBlend.Cutout;
            bool add = blend == ParticleBlend.Add;
            material.SetFloat("_Surface", transparent ? 1 : 0);
            material.SetFloat("_Blend", add ? 2 : 0);
            material.SetFloat("_SrcBlend", (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
            material.SetFloat("_DstBlend", (float)(add ? BlendMode.One : transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetFloat("_ZWrite", transparent ? 0 : 1);
            material.SetFloat("_AlphaClip", transparent ? 0 : 1);
            material.SetFloat("_Cutoff", 0.5f);
            material.SetOverrideTag("RenderType", transparent ? "Transparent" : "TransparentCutout");
            material.renderQueue = (int)(transparent ? RenderQueue.Transparent : RenderQueue.AlphaTest);
            SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", transparent);
            SetKeyword(material, "_ALPHATEST_ON", !transparent);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void SetKeyword(Material material, string keyword, bool on)
        {
            if (on) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }

        /// <summary>テクスチャを読む。凹凸（法線）や mask は、色の画像とちがう読み込み方の設定にする</summary>
        static Texture2D LoadTexture(string relativePath, bool isNormalMap = false, bool isLinear = false, bool noStripes = false)
        {
            string path = $"{TextureFolder}/{relativePath}";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null)
            {
                bool changed = false;
                if (noStripes && (importer.anisoLevel < 8 || importer.filterMode != FilterMode.Trilinear || importer.mipMapBias < 1))
                {
                    importer.anisoLevel = 8;                    // ななめから見ても、ぼやけすぎない
                    importer.filterMode = FilterMode.Trilinear; // 小さい画像（ミップマップ）のあいだを、なめらかにつなぐ
                    importer.mipMapBias = 1;                    // 1 段小さい画像を使う。画素より細かい模様が、しまに見えないように
                    changed = true;
                }
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
