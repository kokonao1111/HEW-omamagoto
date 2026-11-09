using System.Collections.Generic;
using UnityEngine;

namespace Omamagoto.Kitchen.Shapes
{
    /// <summary>
    /// 断面の形を、線（道すじ）に沿って動かして、メッシュを作る。
    /// 取っ手・五徳の脚・縁の飾りなど、「曲がった棒」の形に使う。
    ///
    /// 断面は (横, 上) の平面で、左回りに並べた閉じた線（m）。RoundedRect・Circle で作れる。
    /// 道すじの点は細かく並べる（Arc・Polyline を使うと、角を丸めた道すじが作れる）。
    /// </summary>
    public static class SweepMesh
    {
        /// <param name="profile">断面（左回りの閉じた線。最後の点と最初の点は同じにしない）</param>
        /// <param name="path">道すじの点（m）</param>
        /// <param name="up">はじめの点での、断面の「上」の向き。道すじの向きと平行でなければよい</param>
        /// <param name="closedPath">道すじが輪になっているか（輪なら、ふたは付けない）</param>
        public static Mesh Build(Vector2[] profile, Vector3[] path, Vector3 up, bool closedPath = false)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            path = WithoutRepeats(path, closedPath);
            Vector3[] tangents = Tangents(path, closedPath);
            Vector3[] ups = UpVectors(tangents, up);
            Vector2[] profileNormals = ProfileNormals(profile);
            float[] profileDistance = DistancesAlong(profile, closed: true);
            float[] pathDistance = DistancesAlong(path, closedPath);

            // 断面を、道すじの点ごとに並べる。断面の継ぎ目でテクスチャが切れるように、最初の点をもう 1 回足す
            int ringSize = profile.Length + 1;
            int ringCount = closedPath ? path.Length + 1 : path.Length;
            for (int i = 0; i < ringCount; i++)
            {
                int p = i % path.Length;
                Vector3 forward = tangents[p];
                Vector3 upward = ups[p];
                Vector3 right = Vector3.Cross(upward, forward);
                float along = i < path.Length ? pathDistance[i] : pathDistance[path.Length];

                for (int j = 0; j < ringSize; j++)
                {
                    int k = j % profile.Length;
                    vertices.Add(path[p] + right * profile[k].x + upward * profile[k].y);
                    normals.Add((right * profileNormals[k].x + upward * profileNormals[k].y).normalized);
                    uvs.Add(new Vector2(profileDistance[j], along)); // メートル
                }
            }

            // となりの断面のあいだを、四角（三角 2 つ）でうめる。外から見て時計回りにする
            for (int i = 0; i < ringCount - 1; i++)
            {
                for (int j = 0; j < profile.Length; j++)
                {
                    int a = i * ringSize + j;   // 今の断面
                    int b = a + 1;
                    int d = a + ringSize;       // 次の断面
                    int c = d + 1;
                    triangles.AddRange(new[] { a, b, c, a, c, d });
                }
            }

            if (!closedPath)
            {
                AddCap(profile, path[0], tangents[0], ups[0], facingForward: false, vertices, normals, uvs, triangles);
                int last = path.Length - 1;
                AddCap(profile, path[last], tangents[last], ups[last], facingForward: true, vertices, normals, uvs, triangles);
            }

            var mesh = new Mesh { name = "Sweep" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        // ---------------- 断面を作る ----------------

        /// <summary>角の丸い長方形の断面（幅 width・高さ height・角の半径 radius。m）</summary>
        public static Vector2[] RoundedRect(float width, float height, float radius, int stepsPerCorner = 4)
        {
            radius = Mathf.Clamp(radius, 0.00001f, Mathf.Min(width, height) / 2 - 0.00001f);
            float x = width / 2 - radius, y = height / 2 - radius;
            Vector2[] corners = { new Vector2(x, -y), new Vector2(x, y), new Vector2(-x, y), new Vector2(-x, -y) };

            var points = new List<Vector2>();
            for (int c = 0; c < 4; c++)
            {
                float start = -90 + c * 90; // 右下の角から左回り
                for (int s = 0; s <= stepsPerCorner; s++)
                {
                    float angle = (start + 90f * s / stepsPerCorner) * Mathf.Deg2Rad;
                    points.Add(corners[c] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                }
            }
            return points.ToArray();
        }

        /// <summary>丸い断面（半径 radius。m）</summary>
        public static Vector2[] Circle(float radius, int segments = 16)
        {
            var points = new Vector2[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = 2 * Mathf.PI * i / segments;
                points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            return points;
        }

        // ---------------- 道すじを作る ----------------

        /// <summary>
        /// 折れ線の角を、半径 radius で丸めた道すじ。points は角の位置（m）。
        /// 取っ手の「出て・横に走って・戻る」形などに使う
        /// </summary>
        public static Vector3[] Polyline(Vector3[] points, float radius, int stepsPerCorner = 8)
        {
            var path = new List<Vector3> { points[0] };
            for (int i = 1; i < points.Length - 1; i++)
            {
                Vector3 toPrevious = (points[i - 1] - points[i]).normalized;
                Vector3 toNext = (points[i + 1] - points[i]).normalized;
                float halfAngle = Vector3.Angle(toPrevious, toNext) * Mathf.Deg2Rad / 2;
                float cut = Mathf.Min(radius / Mathf.Tan(halfAngle),
                    Vector3.Distance(points[i - 1], points[i]) / 2, Vector3.Distance(points[i + 1], points[i]) / 2);

                // 角の手前と先の点のあいだを、なめらかな曲線（2 次ベジェ）でつなぐ
                Vector3 start = points[i] + toPrevious * cut;
                Vector3 end = points[i] + toNext * cut;
                for (int s = 0; s <= stepsPerCorner; s++)
                {
                    float t = (float)s / stepsPerCorner;
                    path.Add(Vector3.Lerp(Vector3.Lerp(start, points[i], t), Vector3.Lerp(points[i], end, t), t));
                }
            }
            path.Add(points[points.Length - 1]);
            return path.ToArray();
        }

        /// <summary>
        /// 円の道すじ（XZ の平面。真ん中 center、半径 radius）。輪にするときは closedPath を true にする
        /// </summary>
        public static Vector3[] Arc(Vector3 center, float radius, float fromDegrees, float toDegrees, int segments)
        {
            var path = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(fromDegrees, toDegrees, (float)i / segments) * Mathf.Deg2Rad;
                path[i] = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
            }
            return path;
        }

        /// <summary>
        /// 角の丸い長方形の輪の道すじ（XZ の平面。真ん中 center）。縁の飾りなどに使う。closedPath を true にして使う
        /// </summary>
        public static Vector3[] RoundedRectLoop(Vector3 center, float width, float depth, float radius, int stepsPerCorner = 8)
        {
            Vector2[] outline = RoundedRect(width, depth, radius, stepsPerCorner);
            var path = new Vector3[outline.Length];
            for (int i = 0; i < outline.Length; i++) path[i] = center + new Vector3(outline[i].x, 0, outline[i].y);
            return path;
        }

        // ---------------- 中で使う計算 ----------------

        /// <summary>
        /// 同じ所に重なった点を 1 つにする（つぶれた三角ができないように）。
        /// 輪の道すじで、最後の点が最初の点と同じ（Arc の 0〜360 度など）ときも、最後を消す
        /// </summary>
        static Vector3[] WithoutRepeats(Vector3[] path, bool closed)
        {
            const float same = 1e-6f;
            var result = new List<Vector3>();
            foreach (var point in path)
            {
                if (result.Count == 0 || Vector3.Distance(result[result.Count - 1], point) > same) result.Add(point);
            }
            if (closed && result.Count > 1 && Vector3.Distance(result[0], result[result.Count - 1]) <= same) result.RemoveAt(result.Count - 1);
            return result.ToArray();
        }

        /// <summary>道すじの向き（前とうしろの点の平均）</summary>
        static Vector3[] Tangents(Vector3[] path, bool closed)
        {
            int n = path.Length;
            var tangents = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 previous = closed ? path[(i - 1 + n) % n] : path[Mathf.Max(i - 1, 0)];
                Vector3 next = closed ? path[(i + 1) % n] : path[Mathf.Min(i + 1, n - 1)];
                tangents[i] = (next - previous).normalized;
            }
            return tangents;
        }

        /// <summary>
        /// 断面の「上」の向きを、道すじに沿って少しずつ回して運ぶ（ねじれないように）。
        /// 道すじが曲がった分だけ、上の向きも同じだけ回す
        /// </summary>
        static Vector3[] UpVectors(Vector3[] tangents, Vector3 up)
        {
            var ups = new Vector3[tangents.Length];
            ups[0] = Vector3.ProjectOnPlane(up, tangents[0]).normalized;
            for (int i = 1; i < tangents.Length; i++)
            {
                Quaternion turn = Quaternion.FromToRotation(tangents[i - 1], tangents[i]);
                ups[i] = Vector3.ProjectOnPlane(turn * ups[i - 1], tangents[i]).normalized;
            }
            return ups;
        }

        /// <summary>断面の各点での外向きの向き（左回りの線では、歩く向きの右側が外）</summary>
        static Vector2[] ProfileNormals(Vector2[] profile)
        {
            int n = profile.Length;
            var result = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 walk = (profile[(i + 1) % n] - profile[(i - 1 + n) % n]).normalized;
                result[i] = new Vector2(walk.y, -walk.x);
            }
            return result;
        }

        static float[] DistancesAlong(IReadOnlyList<Vector2> points, bool closed)
        {
            int n = points.Count;
            var distances = new float[closed ? n + 1 : n];
            for (int i = 1; i < distances.Length; i++)
            {
                distances[i] = distances[i - 1] + Vector2.Distance(points[(i - 1) % n], points[i % n]);
            }
            return distances;
        }

        static float[] DistancesAlong(IReadOnlyList<Vector3> points, bool closed)
        {
            int n = points.Count;
            var distances = new float[n + 1];
            for (int i = 1; i <= n; i++)
            {
                if (i == n && !closed) { distances[i] = distances[i - 1]; break; }
                distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i % n]);
            }
            return distances;
        }

        /// <summary>道すじのはしの、ふた（断面の形の平らな面）</summary>
        static void AddCap(Vector2[] profile, Vector3 center, Vector3 forward, Vector3 upward, bool facingForward,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            Vector3 right = Vector3.Cross(upward, forward);
            Vector3 normal = facingForward ? forward : -forward;
            int middle = vertices.Count;
            vertices.Add(center);
            normals.Add(normal);
            uvs.Add(Vector2.zero);
            for (int k = 0; k < profile.Length; k++)
            {
                vertices.Add(center + right * profile[k].x + upward * profile[k].y);
                normals.Add(normal);
                uvs.Add(profile[k]);
            }

            // 左回りの断面は、前（forward）から見ると時計回り。うしろ向きのふたは逆にする
            for (int k = 0; k < profile.Length; k++)
            {
                int a = middle + 1 + k;
                int b = middle + 1 + (k + 1) % profile.Length;
                if (facingForward) triangles.AddRange(new[] { middle, a, b });
                else triangles.AddRange(new[] { middle, b, a });
            }
        }
    }
}
