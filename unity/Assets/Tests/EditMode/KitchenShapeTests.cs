using NUnit.Framework;
using Omamagoto.Kitchen;
using Omamagoto.Kitchen.Shapes;
using UnityEngine;

namespace Omamagoto.Tests
{
    /// <summary>形を作る道具（LatheMesh・ExtrudedMesh・RoundedBoxMesh・SweepMesh・CarrotShape）と、キッチンの寸法の計算</summary>
    public class KitchenShapeTests
    {
        /// <summary>
        /// どの三角も、表（Unity は時計回り）が外を向いているか。
        /// 三角の向き（外積）と、頂点の法線が同じ向きなら OK。逆なら裏返って見えなくなる
        /// </summary>
        static void AssertFacesOutward(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                int[] triangles = mesh.GetTriangles(sub);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    Vector3 faceNormal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                    if (faceNormal.sqrMagnitude < 1e-16f) continue; // 真ん中の点などの、つぶれた三角は見ない

                    Vector3 vertexNormal = normals[a] + normals[b] + normals[c];
                    Assert.Greater(Vector3.Dot(faceNormal, vertexNormal), 0, $"{mesh.name} の三角 {i / 3} が裏返っている");
                }
            }
        }

        [Test]
        public void ろくろの形の面が外を向いている()
        {
            var plate = LatheMesh.Build(new[]
            {
                new LathePart(0, new Vector2(0, 0), new Vector2(0.05f, 0)),
                new LathePart(0, new Vector2(0.05f, 0), new Vector2(0.075f, 0.015f)),
                new LathePart(0, new Vector2(0.075f, 0.015f), new Vector2(0.048f, 0.005f)),
                new LathePart(0, new Vector2(0.048f, 0.005f), new Vector2(0, 0.005f)),
            });
            AssertFacesOutward(plate);
        }

        [Test]
        public void 包丁の刃の面が外を向いている_線が右回りでも左回りでも()
        {
            Vector2[] clockwise = { new Vector2(0, 0), new Vector2(0.1f, 0), new Vector2(0.12f, -0.02f), new Vector2(0, -0.04f) };
            Vector2[] counterClockwise = (Vector2[])clockwise.Clone();
            System.Array.Reverse(counterClockwise);

            AssertFacesOutward(ExtrudedMesh.Build(clockwise, 0.002f));
            AssertFacesOutward(ExtrudedMesh.Build(counterClockwise, 0.002f));
            Assert.AreEqual(new Vector2(0.1f, 0), clockwise[1], "渡した配列を変えてしまっている");
        }

        [Test]
        public void 角の丸い箱の面が外を向き_大きさが合っている()
        {
            Mesh box = RoundedBoxMesh.Build(new Vector3(0.28f, 0.23f, 0.2f), 0.01f);

            AssertFacesOutward(box);
            Assert.Less(Vector3.Distance(new Vector3(0.28f, 0.23f, 0.2f), box.bounds.size), 0.0001f);
            Assert.Less(box.bounds.center.magnitude, 0.0001f, "真ん中が原点にない");

            // 角は丸くなっている（いちばん外の角の点より内側にある）
            foreach (Vector3 p in box.vertices)
            {
                Assert.Less(p.magnitude, new Vector3(0.14f, 0.115f, 0.1f).magnitude - 0.002f);
            }
        }

        [Test]
        public void 角の丸い箱_丸みがなくても作れる()
        {
            Mesh box = RoundedBoxMesh.Build(new Vector3(1, 0.5f, 0.2f), 0);
            AssertFacesOutward(box);
            Assert.Less(Vector3.Distance(new Vector3(1, 0.5f, 0.2f), box.bounds.size), 0.0001f);
        }

        [Test]
        public void にんじんのメッシュ_1本まるごと()
        {
            Mesh whole = CarrotShape.BuildPiece(0, 1);

            AssertFacesOutward(whole);
            Assert.AreEqual(2, whole.subMeshCount); // 皮と切り口
            Assert.AreEqual(CarrotShape.Length, whole.bounds.size.x, 0.001f, "長さ（X）が 150 mm でない");
            // いちばん太い所（葉の付け根の少し先）で直径 39 mm くらい（MaxRadius は形の計算の元の数）
            Assert.AreEqual(0.039f, whole.bounds.size.y, 0.002f, "太さが 39 mm くらいでない");
            Assert.AreEqual(0, whole.GetTriangles(CarrotShape.CutMaterial).Length, "切っていないのに切り口がある");
        }

        [Test]
        public void にんじんのメッシュ_輪切りの1枚()
        {
            Mesh slice = CarrotShape.BuildPiece(0.4f, 0.47f);

            AssertFacesOutward(slice);
            Assert.AreEqual(0.07f * CarrotShape.Length, slice.bounds.size.x, 0.0005f);
            Assert.AreEqual(0, slice.bounds.center.x, 0.0005f, "ピースの真ん中が原点にない");
            Assert.Greater(slice.GetTriangles(CarrotShape.CutMaterial).Length, 0, "切り口がない");
        }

        [Test]
        public void にんじんは先に向かって細くなる()
        {
            Assert.Greater(CarrotShape.RadiusAt(0.2f), CarrotShape.RadiusAt(0.5f));
            Assert.Greater(CarrotShape.RadiusAt(0.5f), CarrotShape.RadiusAt(0.9f));
            Assert.Greater(CarrotShape.RadiusAt(1f), 0);
        }

        [Test]
        public void まな板の座標を_現実と同じ位置に直す()
        {
            // まな板は左手前の角が (290, 10) mm、幅 300 mm・奥行き 220 mm・厚さ 20 mm（2026-11-24 の実物の台）
            AssertNear(new Vector3(0.290f, 0.020f, 0.010f), KitchenLayout.BoardToWorld(0, 0));
            AssertNear(new Vector3(0.590f, 0.020f, 0.230f), KitchenLayout.BoardToWorld(1, 1));
            AssertNear(new Vector3(0.440f, 0.020f, 0.120f), KitchenLayout.BoardCenter);
        }

        [Test]
        public void コンロとフライパンの位置()
        {
            // バーナー（1 口）はコンロ（655 mm から横 230 mm）の真ん中、奥行きの真ん中（20 + 150 mm）。ガステーブルの天板の上面は 90 mm
            AssertNear(new Vector3(0.770f, 0.090f, 0.170f), KitchenLayout.BurnerCenter);
            // フライパンは五徳の上（天板 ＋ 34 mm）。左右・奥行きは現実と同じ
            AssertNear(new Vector3(0.770f, 0.124f, 0.170f), KitchenLayout.PanBottomCenter);
        }

        [Test]
        public void 台の上の物が_台からはみ出さず_重ならない()
        {
            // 実物の台（横 900・奥行き 340）の中に、電子レンジ・皿・まな板・調味料・コンロが重ならずに入っているか
            var areas = new[]
            {
                new Rect(KitchenLayout.MicrowaveX, KitchenLayout.MicrowaveY, KitchenLayout.MicrowaveWidth, KitchenLayout.MicrowaveDepth),
                new Rect(KitchenLayout.PlateX, KitchenLayout.PlateY, KitchenLayout.PlateWidth, KitchenLayout.PlateDepth),
                new Rect(KitchenLayout.BoardX, KitchenLayout.BoardY, KitchenLayout.BoardWidth, KitchenLayout.BoardDepth),
                new Rect(KitchenLayout.SpiceX, KitchenLayout.SpiceY, KitchenLayout.SpiceWidth, KitchenLayout.SpiceDepth),
                new Rect(KitchenLayout.StoveX, KitchenLayout.StoveY, KitchenLayout.StoveWidth, KitchenLayout.StoveDepth),
            };
            for (int i = 0; i < areas.Length; i++)
            {
                // 台のふちにぴったり付いているのはよい（電子レンジは左はしの奥の角にぴったり）
                bool inside = areas[i].xMin >= 0 && areas[i].yMin >= 0 && areas[i].xMax <= KitchenLayout.Width && areas[i].yMax <= KitchenLayout.Depth;
                Assert.IsTrue(inside, $"{i} 番目が台からはみ出している");
                for (int j = i + 1; j < areas.Length; j++) Assert.IsFalse(areas[i].Overlaps(areas[j]), $"{i} 番目と {j} 番目が重なっている");
            }
        }

        [Test]
        public void 曲がった棒の形の面が外を向いている()
        {
            Vector2[] profile = SweepMesh.RoundedRect(0.012f, 0.008f, 0.003f);
            Vector3[] path = SweepMesh.Polyline(new[]
            {
                new Vector3(0, 0, 0), new Vector3(0, 0, -0.02f), new Vector3(0, 0.12f, -0.02f), new Vector3(0, 0.12f, 0),
            }, 0.01f);
            AssertFacesOutward(SweepMesh.Build(profile, path, Vector3.right));

            // 輪になった道すじ（ふたなし）
            AssertFacesOutward(SweepMesh.Build(SweepMesh.Circle(0.003f), SweepMesh.Arc(Vector3.zero, 0.09f, 0, 360, 48), Vector3.up, closedPath: true));
        }

        [Test]
        public void 曲がった棒の形_法線は道すじから外へ向く()
        {
            // まっすぐ上へ伸びる、半径 5 mm の丸い棒
            var mesh = SweepMesh.Build(SweepMesh.Circle(0.005f), new[] { Vector3.zero, new Vector3(0, 0.05f, 0), new Vector3(0, 0.1f, 0) }, Vector3.forward);
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            int sideVertexCount = 3 * 17; // 断面 16 点 ＋ 継ぎ目 1 点 × 3 か所（そのあとは、ふた）
            for (int i = 0; i < sideVertexCount; i++)
            {
                Vector3 outward = new Vector3(vertices[i].x, 0, vertices[i].z);
                Assert.AreEqual(0.005f, outward.magnitude, 0.0001f, "棒の太さがちがう");
                Assert.Greater(Vector3.Dot(outward, normals[i]), 0, $"頂点 {i} の法線が内を向いている");
            }
        }

        static void AssertNear(Vector3 expected, Vector3 actual)
        {
            Assert.Less(Vector3.Distance(expected, actual), 0.0001f, $"{expected:F3} のはずが {actual:F3}");
        }
    }
}
