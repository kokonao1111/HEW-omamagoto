using System.Collections.Generic;
using UnityEngine;

namespace Omamagoto.Kitchen.Shapes
{
    /// <summary>
    /// にんじんの形。three.js 版（screen/index.html の CAR と carR）と同じ形。
    ///
    /// にんじんの中の位置は t で表す：t = 0 が葉の側、t = 1 が先っぽ。
    /// メッシュは X 軸に沿って寝かせる（葉の側が -X、先っぽが +X）。
    /// </summary>
    public static class CarrotShape
    {
        public const float Length = 0.150f;    // 長さ 150 mm
        public const float MaxRadius = 0.021f; // いちばん太い所の半径 21 mm

        public const int SkinMaterial = 0;     // メッシュのマテリアルの番号：皮
        public const int CutMaterial = 1;      //                          切り口

        /// <summary>t の所の半径（m）。先に向かって細くなり、葉の付け根も少し細い。ところどころに浅い筋がある</summary>
        public static float RadiusAt(float t)
        {
            float body = MaxRadius * Mathf.Pow(Mathf.Max(0, 1 - t), 0.78f);
            float shoulder = Mathf.Sin(Mathf.Min(1, t / 0.09f) * Mathf.PI / 2);   // 葉の付け根の丸み
            float ring = 1 - 0.03f * Mathf.Pow(Mathf.Abs(Mathf.Sin(t * Mathf.PI * 15)), 10); // 浅い筋
            return Mathf.Max(0.0007f, body * (0.25f + 0.75f * shoulder) * ring);
        }

        /// <summary>t0〜t1 の部分の真ん中が、にんじん全体の真ん中からどれだけ X にずれているか（m）</summary>
        public static float PieceCenterX(float t0, float t1)
        {
            return ((t0 + t1) / 2 - 0.5f) * Length;
        }

        /// <summary>
        /// t0〜t1 の部分（輪切りの 1 枚、または 1 本まるごと）のメッシュ。部分の真ん中が原点。
        /// 切った面（t0 > 0 や t1 < 1 の端）は、切り口のマテリアルになる。
        /// </summary>
        public static Mesh BuildPiece(float t0, float t1)
        {
            float middle = (t0 + t1) / 2;
            float HeightOf(float t) => (middle - t) * Length; // ろくろの向き：t0 が上、t1 が下

            int steps = Mathf.Max(4, Mathf.RoundToInt((t1 - t0) * 70));
            var body = new List<Vector2>();
            for (int i = 0; i <= steps; i++)
            {
                float t = Mathf.Lerp(t1, t0, (float)i / steps); // 下（t1）から上（t0）へ
                body.Add(new Vector2(RadiusAt(t), HeightOf(t)));
            }

            bool cutAtStart = t0 > 0.001f; // 葉の側を切ってある
            bool cutAtEnd = t1 < 0.999f;   // 先の側を切ってある
            var parts = new[]
            {
                new LathePart(cutAtEnd ? CutMaterial : SkinMaterial, new Vector2(0, HeightOf(t1)), new Vector2(RadiusAt(t1), HeightOf(t1))),
                new LathePart(SkinMaterial, body.ToArray()),
                new LathePart(cutAtStart ? CutMaterial : SkinMaterial, new Vector2(RadiusAt(t0), HeightOf(t0)), new Vector2(0, HeightOf(t0))),
            };

            Mesh mesh = LatheMesh.Build(parts, 40, materialCount: 2); // 皮と切り口（切っていなくても 2 つ用意する）
            LayAlongX(mesh);
            mesh.name = $"Carrot {t0:F2}-{t1:F2}";
            return mesh;
        }

        /// <summary>ろくろで作った縦向きのメッシュを、寝かせる（上 → -X）</summary>
        static void LayAlongX(Mesh mesh)
        {
            Quaternion turn = Quaternion.Euler(0, 0, 90);
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = turn * vertices[i];
                normals[i] = turn * normals[i];
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
        }
    }
}
