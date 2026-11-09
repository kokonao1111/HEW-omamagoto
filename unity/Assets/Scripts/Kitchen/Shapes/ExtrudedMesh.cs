using System.Collections.Generic;
using UnityEngine;

namespace Omamagoto.Kitchen.Shapes
{
    /// <summary>
    /// 平らな形（外側の線）に厚みを付けて、メッシュを作る。包丁の刃などに使う。
    /// 外側の線はふくらんだ形（へこみのない形）にする。
    /// </summary>
    public static class ExtrudedMesh
    {
        /// <param name="outline">外側の線（XY の平面。m）。左回りでも右回りでもよい</param>
        /// <param name="thickness">厚み（m）。Z の -thickness/2 〜 +thickness/2 になる</param>
        public static Mesh Build(Vector2[] outline, float thickness)
        {
            outline = (Vector2[])outline.Clone(); // 渡された配列は変えない
            if (SignedArea(outline) < 0) System.Array.Reverse(outline); // 左回りにそろえる

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            float half = thickness / 2;
            Vector2 center = Center(outline);

            // Unity は「見ている側から時計回り」の三角が表。左回りの線は、-Z 側から見ると左回りなので裏返す
            AddFace(outline, center, -half, Vector3.back, vertices, normals, triangles, flip: true);
            AddFace(outline, center, +half, Vector3.forward, vertices, normals, triangles, flip: false);
            AddSides(outline, half, vertices, normals, triangles);

            var mesh = new Mesh { name = "Extruded" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>表か裏の面。真ん中の点から扇のように三角を並べる</summary>
        static void AddFace(Vector2[] outline, Vector2 center, float z, Vector3 normal,
            List<Vector3> vertices, List<Vector3> normals, List<int> triangles, bool flip)
        {
            int centerIndex = vertices.Count;
            vertices.Add(new Vector3(center.x, center.y, z));
            normals.Add(normal);
            foreach (Vector2 p in outline)
            {
                vertices.Add(new Vector3(p.x, p.y, z));
                normals.Add(normal);
            }

            for (int i = 0; i < outline.Length; i++)
            {
                int a = centerIndex + 1 + i;
                int b = centerIndex + 1 + (i + 1) % outline.Length;
                if (flip) { triangles.Add(centerIndex); triangles.Add(b); triangles.Add(a); }
                else { triangles.Add(centerIndex); triangles.Add(a); triangles.Add(b); }
            }
        }

        /// <summary>まわりの側面。辺ごとに四角を作る（角はくっきり）</summary>
        static void AddSides(Vector2[] outline, float half, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 p = outline[i];
                Vector2 q = outline[(i + 1) % outline.Length];
                Vector2 edge = (q - p).normalized;
                var normal = new Vector3(edge.y, -edge.x, 0); // 左回りの線の外向き

                int start = vertices.Count;
                vertices.Add(new Vector3(p.x, p.y, -half));
                vertices.Add(new Vector3(q.x, q.y, -half));
                vertices.Add(new Vector3(q.x, q.y, +half));
                vertices.Add(new Vector3(p.x, p.y, +half));
                for (int k = 0; k < 4; k++) normals.Add(normal);

                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }
        }

        static float SignedArea(Vector2[] outline)
        {
            float area = 0;
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 p = outline[i];
                Vector2 q = outline[(i + 1) % outline.Length];
                area += p.x * q.y - q.x * p.y;
            }
            return area / 2;
        }

        static Vector2 Center(Vector2[] outline)
        {
            Vector2 sum = Vector2.zero;
            foreach (Vector2 p in outline) sum += p;
            return sum / outline.Length;
        }
    }
}
