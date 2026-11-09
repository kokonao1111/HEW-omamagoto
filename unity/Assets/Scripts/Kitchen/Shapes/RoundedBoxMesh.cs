using System.Collections.Generic;
using UnityEngine;

namespace Omamagoto.Kitchen.Shapes
{
    /// <summary>
    /// 角と辺を丸めた箱（面取りした箱）のメッシュを作る。
    /// 本物の物は角が少し丸いので、光がふちに当たって、ぐっと本物らしく見える。
    ///
    /// テクスチャの位置（UV）はメートルで付ける。マテリアルの Tiling を「1 ÷ テクスチャの実際の大きさ（m）」にすると、
    /// 現実と同じ大きさで貼れる（例：1.27 m 角のタイルの画像なら Tiling = 0.787）。
    /// </summary>
    public static class RoundedBoxMesh
    {
        /// <param name="size">箱の大きさ（m）。真ん中が原点</param>
        /// <param name="radius">角の丸みの半径（m）。いちばん短い辺の半分より大きくはできない</param>
        /// <param name="roundSteps">丸みを何段で作るか（多いほどなめらか）</param>
        public static Mesh Build(Vector3 size, float radius, int roundSteps = 4)
        {
            Vector3 half = size / 2;
            radius = Mathf.Clamp(radius, 0, Mathf.Min(half.x, half.y, half.z));
            Vector3 inner = half - Vector3.one * radius; // 丸みの中心が動ける範囲

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            // 6 つの面。面の向き（normal）と、面の上の 2 つの向き（u・v）
            AddFace(Vector3.right, Vector3.forward, Vector3.up);
            AddFace(Vector3.left, Vector3.back, Vector3.up);
            AddFace(Vector3.up, Vector3.right, Vector3.forward);
            AddFace(Vector3.down, Vector3.right, Vector3.back);
            AddFace(Vector3.forward, Vector3.left, Vector3.up);
            AddFace(Vector3.back, Vector3.right, Vector3.up);

            var mesh = new Mesh { name = "RoundedBox" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;

            void AddFace(Vector3 normal, Vector3 u, Vector3 v)
            {
                float halfU = Vector3.Scale(half, Abs(u)).magnitude;
                float halfV = Vector3.Scale(half, Abs(v)).magnitude;
                float halfN = Vector3.Scale(half, Abs(normal)).magnitude;
                float[] us = GridCoordinates(halfU, radius, roundSteps);
                float[] vs = GridCoordinates(halfV, radius, roundSteps);
                int first = vertices.Count;

                foreach (float b in vs)
                {
                    foreach (float a in us)
                    {
                        // 平らな箱の上の点を、丸みの中心から半径の所へ押し出す
                        Vector3 flat = normal * halfN + u * a + v * b;
                        Vector3 center = Vector3.Max(-inner, Vector3.Min(inner, flat));
                        Vector3 offset = flat - center;
                        Vector3 outward = offset.sqrMagnitude > 1e-12f ? offset.normalized : normal; // 丸みが 0 のときは面の向き
                        vertices.Add(center + outward * radius);
                        normals.Add(outward);
                        uvs.Add(new Vector2(a + halfU, b + halfV)); // メートル
                    }
                }

                // 格子の四角を三角 2 つでうめる。表が外を向く順番にする
                // （Unity は外から見て時計回りが表。u × v が内向きなら、u・v の順だと左回りになるので入れかえる）
                bool flip = Vector3.Dot(Vector3.Cross(u, v), normal) < 0;
                int columns = us.Length;
                for (int j = 0; j < vs.Length - 1; j++)
                {
                    for (int i = 0; i < columns - 1; i++)
                    {
                        int a = first + j * columns + i;
                        int b = a + 1;
                        int c = a + columns + 1;
                        int d = a + columns;
                        if (flip) { triangles.AddRange(new[] { a, d, c, a, c, b }); }
                        else { triangles.AddRange(new[] { a, b, c, a, c, d }); }
                    }
                }
            }
        }

        /// <summary>面の上の格子の位置。両はしの丸みの所だけ細かく分ける</summary>
        static float[] GridCoordinates(float half, float radius, int roundSteps)
        {
            var values = new List<float>();
            for (int i = 0; i <= roundSteps; i++) values.Add(-half + radius * i / roundSteps);
            for (int i = roundSteps; i >= 0; i--) values.Add(half - radius * i / roundSteps);

            // 丸みがない・面が細いときに、同じ位置が 2 回並ばないようにする
            var unique = new List<float>();
            foreach (float value in values)
            {
                if (unique.Count == 0 || value - unique[unique.Count - 1] > 1e-6f) unique.Add(value);
            }
            return unique.ToArray();
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}
