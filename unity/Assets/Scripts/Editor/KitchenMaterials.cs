using System.IO;
using UnityEditor;
using UnityEngine;

namespace Omamagoto.Editor
{
    /// <summary>
    /// キッチンのマテリアル（色・つや・テクスチャ）を作る。Assets/Art/Materials/ に保存する。
    /// もうあれば、中身を上書きする（手で直したい色は、ここの数字を直す）。
    /// </summary>
    public static class KitchenMaterials
    {
        const string MaterialFolder = "Assets/Art/Materials";
        const string TextureFolder = "Assets/Art/Textures";

        public static Material CounterWood => Textured("CounterWood", "kitchen_wood", smoothness: 0.35f, tiling: new Vector2(1, 1));
        public static Material BoardWood => Textured("BoardWood", "oak_veneer_01", smoothness: 0.25f, tiling: new Vector2(0.6f, 0.6f));
        public static Material WallTiles => Textured("WallTiles", "interior_tiles", smoothness: 0.6f, tiling: new Vector2(2, 1.2f));

        public static Material StoveTop => Plain("StoveTop", new Color(0.05f, 0.05f, 0.055f), smoothness: 0.85f);
        public static Material BurnerMetal => Plain("BurnerMetal", new Color(0.18f, 0.18f, 0.19f), smoothness: 0.45f, metallic: 0.8f);
        public static Material PanCoating => Plain("PanCoating", new Color(0.08f, 0.08f, 0.085f), smoothness: 0.55f, metallic: 0.3f);
        public static Material PanHandle => Plain("PanHandle", new Color(0.04f, 0.035f, 0.03f), smoothness: 0.4f);
        public static Material PlateCeramic => Plain("PlateCeramic", new Color(0.93f, 0.92f, 0.89f), smoothness: 0.88f);
        public static Material MicrowaveBody => Plain("MicrowaveBody", new Color(0.82f, 0.82f, 0.8f), smoothness: 0.5f);
        public static Material MicrowaveGlass => Plain("MicrowaveGlass", new Color(0.03f, 0.035f, 0.04f), smoothness: 0.95f);
        public static Material SaltShaker => Plain("SaltShaker", new Color(0.95f, 0.95f, 0.93f), smoothness: 0.7f);
        public static Material PepperShaker => Plain("PepperShaker", new Color(0.12f, 0.1f, 0.09f), smoothness: 0.6f);
        public static Material ShakerCap => Plain("ShakerCap", new Color(0.75f, 0.76f, 0.78f), smoothness: 0.75f, metallic: 1f);

        public static Material KnifeBlade => Plain("KnifeBlade", new Color(0.87f, 0.89f, 0.9f), smoothness: 0.85f, metallic: 1f);
        public static Material KnifeHandle => Plain("KnifeHandle", new Color(0.23f, 0.14f, 0.09f), smoothness: 0.55f);

        public static Material CarrotSkin => Plain("CarrotSkin", new Color(0.93f, 0.43f, 0.08f), smoothness: 0.45f);
        public static Material CarrotCut => Plain("CarrotCut", new Color(1f, 0.55f, 0.15f), smoothness: 0.7f);
        public static Material CarrotLeaf => Plain("CarrotLeaf", new Color(0.3f, 0.54f, 0.2f), smoothness: 0.4f);

        /// <summary>色だけのマテリアル（URP の Lit）</summary>
        static Material Plain(string name, Color color, float smoothness, float metallic = 0)
        {
            Material material = LoadOrCreate(name);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// テクスチャのマテリアル。Assets/Art/Textures/{textureName}/ の
        /// {textureName}_diff_1k.jpg（色）と {textureName}_nor_gl_1k.jpg（凹凸）を使う
        /// </summary>
        static Material Textured(string name, string textureName, float smoothness, Vector2 tiling)
        {
            string folder = $"{TextureFolder}/{textureName}";
            var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/{textureName}_diff_1k.jpg");
            var normalMap = LoadNormalMap($"{folder}/{textureName}_nor_gl_1k.jpg");

            Material material = LoadOrCreate(name);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", baseMap);
            material.SetTextureScale("_BaseMap", tiling);
            material.SetTexture("_BumpMap", normalMap);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>凹凸のテクスチャは「Normal map」として読み込む設定にする</summary>
        static Texture2D LoadNormalMap(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material LoadOrCreate(string name)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            Directory.CreateDirectory(MaterialFolder);
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
