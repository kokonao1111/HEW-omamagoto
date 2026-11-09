using System.Collections.Generic;
using UnityEngine;

namespace Omamagoto.Kitchen.Shapes
{
    /// <summary>
    /// 回転させた形（ろくろで作るような形）の 1 つの部分。
    /// Points は (半径, 高さ) の並び（m）。中は折れ目なしでなめらかにつなぐ。
    /// </summary>
    public struct LathePart
    {
        /// <summary>
        /// (半径, 高さ) の並び。並びに沿って歩いたとき、右側が外になるように並べる
        /// （例：底は真ん中 → 外へ、側面は下 → 上、上面は外 → 真ん中）
        /// </summary>
        public Vector2[] Points;

        /// <summary>どのマテリアルを使うか（0 から）。にんじんの皮と切り口を分けるときなどに使う</summary>
        public int MaterialIndex;

        public LathePart(int materialIndex, params Vector2[] points)
        {
            MaterialIndex = materialIndex;
            Points = points;
        }
    }

    /// <summary>
    /// 断面の線を Y 軸のまわりに 1 周回して、メッシュを作る。お皿・フライパン・にんじん などに使う。
    /// 部分（LathePart）と部分のあいだは、角（折れ目）になる。
    /// </summary>
    public static class LatheMesh
    {
        /// <param name="parts">断面の部分。つながった順に並べる</param>
        /// <param name="segments">1 周を何個に分けるか（多いほどなめらか）</param>
        /// <param name="materialCount">
        /// マテリアルの数。使わない番号があっても、この数だけ用意する
        /// （MeshRenderer のマテリアルの数と合わせないと、同じ面が 2 回描かれてしまう）
        /// </param>
        public static Mesh Build(LathePart[] parts, int segments = 48, int materialCount = 1)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();

            foreach (var part in parts) materialCount = Mathf.Max(materialCount, part.MaterialIndex + 1);
            var trianglesByMaterial = new List<int>[materialCount];
            for (int i = 0; i < materialCount; i++) trianglesByMaterial[i] = new List<int>();

            foreach (var part in parts)
            {
                AddPart(part, segments, vertices, normals, uvs, trianglesByMaterial[part.MaterialIndex]);
            }

            var mesh = new Mesh { name = "Lathe" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = materialCount;
            for (int i = 0; i < materialCount; i++) mesh.SetTriangles(trianglesByMaterial[i], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        static void AddPart(LathePart part, int segments, List<Vector3> vertices, List<Vector3> normals,
            List<Vector2> uvs, List<int> triangles)
        {
            Vector2[] points = part.Points;
            int firstIndex = vertices.Count;
            float[] distanceAlong = DistancesAlong(points);

            for (int i = 0; i < points.Length; i++)
            {
                Vector2 normal2D = ProfileNormal(points, i);
                for (int j = 0; j <= segments; j++)
                {
                    float angle = 2 * Mathf.PI * j / segments;
                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);

                    vertices.Add(new Vector3(points[i].x * cos, points[i].y, points[i].x * sin));
                    normals.Add(new Vector3(normal2D.x * cos, normal2D.y, normal2D.x * sin));
                    uvs.Add(new Vector2((float)j / segments, distanceAlong[i]));
                }
            }

            // となりの 2 つの輪のあいだを、四角（三角 2 つ）でうめる
            int ringSize = segments + 1;
            for (int i = 0; i < points.Length - 1; i++)
            {
                for (int j = 0; j < segments; j++)
                {
                    int a = firstIndex + i * ringSize + j;  // 今の輪
                    int b = a + 1;
                    int d = a + ringSize;                   // 次の輪
                    int c = d + 1;
                    triangles.Add(a); triangles.Add(d); triangles.Add(c);
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                }
            }
        }

        /// <summary>断面の線の、その点での外向きの向き（歩く向きの右側）</summary>
        static Vector2 ProfileNormal(Vector2[] points, int i)
        {
            Vector2 before = i > 0 ? points[i] - points[i - 1] : Vector2.zero;
            Vector2 after = i < points.Length - 1 ? points[i + 1] - points[i] : Vector2.zero;
            Vector2 walk = (before.normalized + after.normalized).normalized;
            return new Vector2(walk.y, -walk.x); // 右に 90 度回す
        }

        /// <summary>線に沿った長さ（テクスチャの縦の位置に使う）</summary>
        static float[] DistancesAlong(Vector2[] points)
        {
            var distances = new float[points.Length];
            for (int i = 1; i < points.Length; i++)
            {
                distances[i] = distances[i - 1] + Vector2.Distance(points[i - 1], points[i]);
            }
            return distances;
        }
    }
}
