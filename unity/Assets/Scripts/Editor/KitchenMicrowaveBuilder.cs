using System.Collections.Generic;
using Omamagoto.Kitchen;
using Omamagoto.Kitchen.Shapes;
using UnityEngine;
using static Omamagoto.Editor.KitchenBuildTools;

namespace Omamagoto.Editor
{
    /// <summary>
    /// 電子レンジ（白い本体）。
    /// - 扉：黒いスモークガラス。裏に黒い印刷の枠、窓の所は穴のあいた金属の板（中が少し見える）。曲がった棒の取っ手
    /// - 下の操作パネル：時計の表示（光る）、丸い 3 つのボタン、ギザギザのダイヤル
    /// - 横と上の通気の穴、ゴムの足、本体の継ぎ目
    /// 温めているときに中が光るのは、MicrowaveCavity のマテリアル（いまは光らせていない。電子レンジの演出は目標）
    ///
    /// 寸法は電子レンジの左手前の下（現実の座標 mm）から測る。前の面は y = 0、上は z
    /// </summary>
    public static class KitchenMicrowaveBuilder
    {
        const float FootHeight = 8;
        const float DoorThickness = 7;
        static readonly Vector3 Up = new Vector3(0, 0, 1); // 現実の座標の「上」（z）

        // 扉と窓（電子レンジの左手前の下から、mm）
        const float DoorLeft = 6, DoorRight = 274, DoorBottom = 52, DoorTop = 224;
        const float WindowLeft = 22, WindowRight = 214, WindowBottom = 70, WindowTop = 206;

        static Vector3 origin; // 電子レンジの左手前の下（現実の座標 mm）

        public static void Build(Transform parent)
        {
            origin = new Vector3(KitchenLayout.MicrowaveX, KitchenLayout.MicrowaveY, 0);
            var microwave = Group(parent, "Microwave");

            BuildBody(microwave);
            BuildDoor(microwave);
            BuildHandle(microwave);
            BuildControlPanel(microwave);
            BuildDial(microwave);
            BuildVents(microwave);
        }

        /// <summary>電子レンジの中の位置（mm）→ 現実の座標（mm）</summary>
        static Vector3 P(float x, float y, float z) => origin + new Vector3(x, y, z);

        /// <summary>本体・足・継ぎ目</summary>
        static void BuildBody(Transform microwave)
        {
            float w = KitchenLayout.MicrowaveWidth, d = KitchenLayout.MicrowaveDepth, h = KitchenLayout.MicrowaveHeight;

            foreach (float fx in new[] { 24f, w - 24 })
            {
                foreach (float fy in new[] { 24f, d - 24 })
                {
                    Lathe(microwave, "Foot", new[] { Part(Mm(9, 0), Mm(9.5f, 5), Mm(8, FootHeight + 1)), Part(Mm(8, FootHeight + 1), Mm(0, FootHeight + 1)) },
                        P(fx, fy, 0), KitchenMaterials.Rubber, meshName: "MicrowaveFoot");
                }
            }

            float bodyHeight = h - FootHeight;
            RoundedBox(microwave, "MicrowaveBody", P(w / 2, d / 2, FootHeight + bodyHeight / 2), new Vector3(w, d, bodyHeight), 9,
                KitchenMaterials.ApplianceWhite);

            // 前の枠と、上と横をおおうカバーの継ぎ目（細い溝）。前から 14 mm の所を 1 周
            Vector2[] outline = SweepMesh.RoundedRect(w + 0.6f, bodyHeight + 0.6f, 9.2f, 6);
            var seam = new Vector3[outline.Length];
            for (int i = 0; i < outline.Length; i++) seam[i] = P(w / 2 + outline[i].x, 14, FootHeight + bodyHeight / 2 + outline[i].y);
            Sweep(microwave, "MicrowaveSeam", SweepMesh.Circle(0.45f, 6), seam, new Vector3(0, 1, 0), KitchenMaterials.VentHole,
                closedPath: true);
        }

        /// <summary>扉：前のスモークガラス、裏の黒い枠、窓の穴あき板、その奥の庫内</summary>
        static void BuildDoor(Transform microwave)
        {
            float doorWidth = DoorRight - DoorLeft, doorHeight = DoorTop - DoorBottom;
            float doorCenterX = (DoorLeft + DoorRight) / 2, doorCenterZ = (DoorBottom + DoorTop) / 2;
            float windowWidth = WindowRight - WindowLeft, windowHeight = WindowTop - WindowBottom;
            float windowCenterX = (WindowLeft + WindowRight) / 2, windowCenterZ = (WindowBottom + WindowTop) / 2;

            // 前のガラス（厚さ 3 mm）
            RoundedBox(microwave, "DoorGlass", P(doorCenterX, -DoorThickness + 1.5f, doorCenterZ), new Vector3(doorWidth, 3, doorHeight), 1.5f,
                KitchenMaterials.SmokedGlass);

            // ガラスの裏の黒い枠（窓のまわり）
            const float frameDepth = 4;
            float frameY = -frameDepth / 2;
            void Frame(string name, float left, float right, float bottom, float top)
            {
                RoundedBox(microwave, name, P((left + right) / 2, frameY, (bottom + top) / 2), new Vector3(right - left, frameDepth, top - bottom), 0.5f,
                    KitchenMaterials.DoorPrint);
            }
            Frame("DoorFrameTop", DoorLeft, DoorRight, WindowTop, DoorTop);
            Frame("DoorFrameBottom", DoorLeft, DoorRight, DoorBottom, WindowBottom);
            Frame("DoorFrameLeft", DoorLeft, WindowLeft, WindowBottom, WindowTop);
            Frame("DoorFrameRight", WindowRight, DoorRight, WindowBottom, WindowTop);

            // 窓：穴のあいた金属の板と、その奥の庫内（温めるときに光る）
            RoundedBox(microwave, "DoorMesh", P(windowCenterX, -2.2f, windowCenterZ), new Vector3(windowWidth, 0.3f, windowHeight), 0,
                KitchenMaterials.MicrowaveMesh);
            RoundedBox(microwave, "Cavity", P(windowCenterX, -0.7f, windowCenterZ), new Vector3(windowWidth, 1.2f, windowHeight), 0,
                KitchenMaterials.MicrowaveCavity);
        }

        /// <summary>
        /// 取っ手：扉の右に、縦の棒。上と下の 2 か所で扉から出て、なめらかに曲がって縦に走る。
        /// 付け根には、丸い座金
        /// </summary>
        static void BuildHandle(Transform microwave)
        {
            const float x = 250, bottom = 92, top = 186, reach = 19;
            float front = -DoorThickness;
            Vector3[] path = SweepMesh.Polyline(new[]
            {
                P(x, front, bottom),
                P(x, front - reach, bottom + 6),
                P(x, front - reach, top - 6),
                P(x, front, top),
            }, 10f, 10);
            Sweep(microwave, "MicrowaveHandle", SweepMesh.RoundedRect(13, 9, 4, 5), path, Up, KitchenMaterials.SilverPlastic);

            foreach (float z in new[] { bottom, top })
            {
                var boss = Lathe(microwave, "HandleBoss", new[] { Part(Mm(9, 0), Mm(9, 1.6f), Mm(7.5f, 3f)), Part(Mm(7.5f, 3f), Mm(0, 3f)) },
                    P(x, front, z), KitchenMaterials.SilverPlastic, meshName: "MicrowaveHandleBoss");
                boss.rotation = Quaternion.Euler(-90, 0, 0); // ろくろの軸（上）を、手前に向ける
            }
        }

        /// <summary>扉の下の操作パネル：時計の表示と、丸い 3 つのボタン</summary>
        static void BuildControlPanel(Transform microwave)
        {
            const float rowZ = 30;

            // 表示：黒いガラスの窓に、光る数字
            RoundedBox(microwave, "DisplayWindow", P(56, -0.8f, rowZ), new Vector3(80, 1.6f, 18), 2, KitchenMaterials.GlassTop);
            Vector2 size = KitchenMaterials.MicrowaveDisplaySize * 1000f;
            RoundedBox(microwave, "Display", P(56, -1.7f, rowZ), new Vector3(size.x, 0.3f, size.y), 0, KitchenMaterials.MicrowaveDisplay);

            // ボタン：はしが丸い横長のボタンを 3 つ
            for (int i = 0; i < 3; i++)
            {
                RoundedBox(microwave, $"Button{i + 1}", P(116 + i * 34, -1.5f, rowZ), new Vector3(26, 5, 11), 5.4f, KitchenMaterials.ButtonLight);
            }
        }

        /// <summary>ダイヤル：銀色の台座と、まわりにギザギザのついたつまみ。上に、向きの印の溝</summary>
        static void BuildDial(Transform microwave)
        {
            // ダイヤルは手前を向く。ろくろの形は上（+Y）に向けて作るので、入れ物ごと -90 度回す
            var dial = new GameObject("Dial").transform;
            dial.SetParent(microwave, false);
            Vector3 center = P(238, 0, 30);
            dial.position = KitchenLayout.ToWorld(center.x, center.y, center.z);
            dial.rotation = Quaternion.Euler(-90, 0, 0);

            AddLocal(dial, "DialBezel", LatheMesh.Build(new[]
            {
                new LathePart(0, Mm(17, 0) / 1000, Mm(17, 1) / 1000, Mm(16, 2) / 1000, Mm(14.5f, 2.1f) / 1000),
            }, 64), KitchenMaterials.SilverPlastic);

            AddLocal(dial, "DialKnob", LatheMesh.Build(new[]
            {
                new LathePart(0, Mm(13.4f, 2) / 1000, Mm(13.4f, 12) / 1000),
                new LathePart(0, Mm(13.4f, 12) / 1000, Mm(12.6f, 13.6f) / 1000, Mm(10.5f, 14.4f) / 1000, Mm(0, 14.7f) / 1000),
            }, 64), KitchenMaterials.SilverPlastic);

            // ギザギザ：細い山を 36 本、側面に並べる
            Mesh rib = RoundedBoxMesh.Build(new Vector3(0.0012f, 0.0094f, 0.0017f), 0.0004f, 2);
            var ribs = new List<CombineInstance>();
            for (int i = 0; i < 36; i++)
            {
                float angle = i * 10f;
                Vector3 position = Quaternion.Euler(0, -angle, 0) * new Vector3(0.0134f, 0.0074f, 0);
                ribs.Add(new CombineInstance { mesh = rib, transform = Matrix4x4.TRS(position, Quaternion.Euler(0, -angle, 0), Vector3.one) });
            }
            var ribMesh = new Mesh();
            ribMesh.CombineMeshes(ribs.ToArray(), true, true);
            AddLocal(dial, "DialRibs", ribMesh, KitchenMaterials.SilverPlastic);

            // 向きの印：上の面に、黒い細い溝
            AddLocal(dial, "DialMark", RoundedBoxMesh.Build(new Vector3(0.0018f, 0.0006f, 0.008f), 0.0003f, 2), KitchenMaterials.VentHole,
                new Vector3(0, 0.0146f, 0.0055f));
        }

        /// <summary>横と上の、細長い通気の穴（黒い細長い形で表す）</summary>
        static void BuildVents(Transform microwave)
        {
            float w = KitchenLayout.MicrowaveWidth, d = KitchenLayout.MicrowaveDepth, h = KitchenLayout.MicrowaveHeight;

            // 右の横の面：奥の上のほうに、4 列 × 11 段
            Mesh sideSlot = RoundedBoxMesh.Build(ToWorldSize(new Vector3(0.6f, 16, 3.2f)), 0.0003f, 2);
            var side = new List<(Mesh, Matrix4x4)>();
            for (int column = 0; column < 4; column++)
            {
                for (int row = 0; row < 11; row++)
                {
                    side.Add((sideSlot, At(new Vector3(0.1f, 100 + column * 22, 120 + row * 8), Quaternion.identity)));
                }
            }
            Combined(microwave, "SideVents", P(w, 0, 0), KitchenMaterials.VentHole, side);

            // 上の面：奥に 1 列
            Mesh topSlot = RoundedBoxMesh.Build(ToWorldSize(new Vector3(3.2f, 30, 0.6f)), 0.0003f, 2);
            var top = new List<(Mesh, Matrix4x4)>();
            for (int i = 0; i < 21; i++) top.Add((topSlot, At(new Vector3(40 + i * 10, d - 40, 0.1f), Quaternion.identity)));
            Combined(microwave, "TopVents", P(0, 0, h), KitchenMaterials.VentHole, top);
        }

        /// <summary>入れ物の中の位置（m）に、メッシュを置く（ダイヤルのように、入れ物ごと回すとき）</summary>
        static void AddLocal(Transform parent, string objectName, Mesh mesh, Material material, Vector3 localPosition = default)
        {
            var gameObject = new GameObject(objectName);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            gameObject.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh, "Microwave" + objectName);
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            gameObject.isStatic = true;
        }
    }
}
