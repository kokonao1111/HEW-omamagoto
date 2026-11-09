using System.Collections.Generic;
using Omamagoto.Kitchen;
using Omamagoto.Kitchen.Shapes;
using UnityEngine;
using static Omamagoto.Editor.KitchenBuildTools;

namespace Omamagoto.Editor
{
    /// <summary>
    /// ガスコンロ：台に置く、昔ながらの箱型のガステーブル（2 口・つまみ式。2026-11-10 に決めた）。
    ///
    /// - 箱：アイボリーの塗装の鉄板。下にゴムの足
    /// - 天板：シルバーのホーロー。ふちが少し盛り上がる
    /// - 前：ステンレスの板に、左右のつまみ（回すと火がつく）と、真ん中の魚焼きグリルの扉
    /// - バーナーごとに：受け皿・バーナー（炎口つき）・点火プラグ・鋳物の五徳
    ///
    /// フライパンは五徳の上にのる（底の高さは KitchenLayout.TrivetTopZ）
    /// </summary>
    public static class KitchenStoveBuilder
    {
        const float Top = KitchenLayout.StoveTopZ;         // 天板の上面（mm）
        const float FootHeight = 8;                        // 箱の下の、ゴムの足の高さ
        const float PanelFrontY = KitchenLayout.StoveY - 1; // 前の板の表面（現実の y。mm）
        const float KnobZ = 47;                            // つまみの真ん中の高さ
        const float KnobFromSide = 42;                     // 箱の横から、つまみの真ん中まで
        static readonly Vector3 Up = new Vector3(0, 0, 1); // 現実の座標の「上」（z）

        /// <summary>作ったもののうち、再生中に動かすもの（炎を出す GasBurnerView を付ける所と、回すつまみ）</summary>
        public struct Result
        {
            public Transform RightBurner;
            public Transform RightKnob;
        }

        public static Result Build(Transform parent)
        {
            var stove = Group(parent, "GasStove");
            BuildBody(stove);
            BuildGrill(stove);

            var result = new Result();
            foreach (bool right in new[] { false, true })
            {
                Transform burner = BuildBurner(stove, right);
                Transform knob = BuildKnob(stove, right);
                if (right)
                {
                    result.RightBurner = burner;
                    result.RightKnob = knob;
                }
            }
            return result;
        }

        // ---------------- 箱と天板 ----------------

        /// <summary>箱・ゴムの足・天板（ふちつき）・前の板</summary>
        static void BuildBody(Transform stove)
        {
            float x = KitchenLayout.StoveX, y = KitchenLayout.StoveY;
            float w = KitchenLayout.StoveWidth, d = KitchenLayout.StoveDepth;
            float cx = x + w / 2, cy = y + d / 2;

            foreach (var (footX, footY) in new[] { (x + 30, y + 30), (x + w - 30, y + 30), (x + 30, y + d - 30), (x + w - 30, y + d - 30) })
            {
                Lathe(stove, "Foot", new[] { Part(Mm(10, 0), Mm(10, 6), Mm(8.5f, FootHeight)) },
                    new Vector3(footX, footY, 0), KitchenMaterials.Rubber, meshName: "StoveFoot");
            }

            // 箱（前の板の厚さ 3 mm の分だけ、うしろから始める）
            float boxBottom = FootHeight, boxTop = Top - 6;
            RoundedBox(stove, "Body", new Vector3(cx, cy + 1, (boxBottom + boxTop) / 2), new Vector3(w - 4, d - 2, boxTop - boxBottom), 5,
                KitchenMaterials.StoveBody, collider: true);

            // 天板：箱より少し大きいホーローの板。ふちを 1 周、少し盛り上げる
            RoundedBox(stove, "TopPlate", new Vector3(cx, cy, Top - 3), new Vector3(w + 4, d + 4, 6), 3,
                KitchenMaterials.StoveTopEnamel, collider: true);
            Vector3[] rim = RoundedLoopMm(cx, cy, Top - 0.5f, w + 1, d + 1, 5);
            Sweep(stove, "TopPlateRim", SweepMesh.RoundedRect(5, 4, 1.5f), rim, Up, KitchenMaterials.StoveTopEnamel, closedPath: true);

            // 前の板：ステンレス。つまみとグリルの扉が付く
            RoundedBox(stove, "FrontPanel", new Vector3(cx, PanelFrontY + 1.5f, 47), new Vector3(w - 12, 3, 66), 1.5f,
                KitchenMaterials.Stainless);
        }

        /// <summary>真ん中の魚焼きグリル：黒い扉・のぞき窓・クロムの取っ手と、天板のうしろの排気口</summary>
        static void BuildGrill(Transform stove)
        {
            float cx = KitchenLayout.StoveX + KitchenLayout.StoveWidth / 2;

            RoundedBox(stove, "GrillDoor", new Vector3(cx, PanelFrontY - 1.5f, 45), new Vector3(220, 3, 54), 2,
                KitchenMaterials.GrillDoor);
            RoundedBox(stove, "GrillWindow", new Vector3(cx, PanelFrontY - 3.1f, 38), new Vector3(130, 0.4f, 20), 2,
                KitchenMaterials.GlassTop);

            // 取っ手：扉から出て、横に走って、扉へ戻る棒
            float front = PanelFrontY - 3, handleZ = 62;
            Vector3[] handle = SweepMesh.Polyline(new[]
            {
                new Vector3(cx - 80, front, handleZ),
                new Vector3(cx - 78, front - 12, handleZ),
                new Vector3(cx + 78, front - 12, handleZ),
                new Vector3(cx + 80, front, handleZ),
            }, 5f, 8);
            Sweep(stove, "GrillHandle", SweepMesh.RoundedRect(9, 6, 2.5f, 5), handle, Up, KitchenMaterials.Chrome);

            // 排気口：天板のうしろの真ん中。ステンレスのふたに、細長いすき間
            float ventY = KitchenLayout.StoveY + KitchenLayout.StoveDepth - 28;
            RoundedBox(stove, "GrillVent", new Vector3(cx, ventY, Top + 2), new Vector3(110, 24, 4), 2, KitchenMaterials.Stainless);
            Mesh slot = RoundedBoxMesh.Build(new Vector3(0.003f, 0.0006f, 0.016f), 0.0008f, 2);
            var slots = new List<(Mesh, Matrix4x4)>();
            for (int i = -4; i <= 4; i++) slots.Add((slot, At(new Vector3(i * 11.5f, 0, 0), Quaternion.identity)));
            Combined(stove, "GrillVentSlots", new Vector3(cx, ventY, Top + 4.05f), KitchenMaterials.VentHole, slots);
        }

        // ---------------- つまみ ----------------

        /// <summary>
        /// 前のつまみ（黒い樹脂・クロムの輪・縦の持ち手）と、まわりの印刷（消・強・中・弱の印）。
        /// 返すのは、つまみを回すための入れ物（前の板の表面・つまみの真ん中。Z のまわりに回すと、前から見て左回り）
        /// </summary>
        static Transform BuildKnob(Transform stove, bool right)
        {
            float x = right ? KitchenLayout.StoveX + KitchenLayout.StoveWidth - KnobFromSide : KitchenLayout.StoveX + KnobFromSide;
            string side = right ? "Right" : "Left";

            BuildKnobPrint(stove, side, new Vector3(x, PanelFrontY, KnobZ));

            var knob = Group(stove, "Knob" + side);
            knob.position = KitchenLayout.ToWorld(x, PanelFrontY, KnobZ);

            // ろくろの形は Y の向きに作るので、前（-Z）へ向ける
            Quaternion toFront = Quaternion.Euler(-90, 0, 0);
            Mesh bezel = LatheMesh.Build(new[]
            {
                Part(Mm(22, 0), Mm(22, 1.8f), Mm(21, 3)),
                Part(Mm(21, 3), Mm(19.2f, 3.2f)),
            }, 64);
            AddLocal(knob, "Bezel", SaveMesh(bezel, "StoveKnobBezel"), KitchenMaterials.Chrome, Vector3.zero, toFront);

            Mesh body = LatheMesh.Build(new[]
            {
                Part(Mm(19.2f, 2.5f), Mm(19.4f, 9), Mm(18.6f, 13.5f), Mm(16.5f, 15.5f), Mm(9, 16.4f), Mm(0, 16.6f)),
            }, 64);
            AddLocal(knob, "Body", SaveMesh(body, "StoveKnobBody"), KitchenMaterials.KnobBlack, Vector3.zero, toFront);

            // 横のみぞ（指がすべらないように、細い出っぱりを 24 本）
            Mesh rib = RoundedBoxMesh.Build(new Vector3(0.0016f, 0.009f, 0.0012f), 0.0005f, 2);
            var ribs = new List<CombineInstance>();
            for (int i = 0; i < 24; i++)
            {
                float angle = 360f * i / 24;
                Quaternion around = Quaternion.Euler(0, angle, 0);
                ribs.Add(new CombineInstance { mesh = rib, transform = Matrix4x4.TRS(around * new Vector3(0, 0.0085f, -0.0194f), around, Vector3.one) });
            }
            var ribMesh = new Mesh();
            ribMesh.CombineMeshes(ribs.ToArray(), mergeSubMeshes: true, useMatrices: true);
            AddLocal(knob, "Ribs", SaveMesh(ribMesh, "StoveKnobRibs"), KitchenMaterials.KnobBlack, Vector3.zero, toFront);

            // 持ち手：縦の棒。上のはしに白い印（火を消した所で真上を向く）
            Mesh grip = RoundedBoxMesh.Build(new Vector3(0.010f, 0.034f, 0.014f), 0.0035f, 4);
            AddLocal(knob, "Grip", SaveMesh(grip, "StoveKnobGrip"), KitchenMaterials.KnobBlack, new Vector3(0, 0, -0.022f), Quaternion.identity);
            Mesh mark = RoundedBoxMesh.Build(new Vector3(0.0022f, 0.009f, 0.0006f), 0.0003f, 2);
            AddLocal(knob, "GripMark", SaveMesh(mark, "StoveKnobMark"), KitchenMaterials.KnobMark, new Vector3(0, 0.0105f, -0.0291f), Quaternion.identity);
            return knob;
        }

        /// <summary>つまみのまわりの黒い印：真上が「消」（白抜きの輪）、左へ「強」「中」「弱」（だんだん小さい点）</summary>
        static void BuildKnobPrint(Transform stove, string side, Vector3 centerMm)
        {
            Mesh offRing = LatheMesh.Build(new[] { Part(Mm(2.6f, 0), Mm(2.6f, 0.3f), Mm(1.6f, 0.3f), Mm(1.6f, 0)) }, 24);
            float[] dotRadius = { 0, 1.6f, 2.3f, 3f }; // 弱・中・強
            var pieces = new List<(Mesh, Matrix4x4)>();
            for (int level = 0; level < GasBurnerShape.KnobDegrees.Length; level++)
            {
                float rad = GasBurnerShape.KnobDegrees[level] * Mathf.Deg2Rad;
                var offset = new Vector3(-Mathf.Sin(rad), 0, Mathf.Cos(rad)) * 30f; // 前から見て、真上から左回り
                Mesh mesh = level == 0
                    ? offRing
                    : LatheMesh.Build(new[] { Part(Mm(dotRadius[level], 0), Mm(dotRadius[level], 0.3f), Mm(0, 0.3f)) }, 24);
                pieces.Add((mesh, At(offset, Quaternion.Euler(-90, 0, 0))));
            }
            Combined(stove, "KnobPrint" + side, centerMm, KitchenMaterials.StovePrint, pieces);
        }

        /// <summary>入れ物の中に、メッシュを置く（位置と向きは入れ物から見た Unity の値）</summary>
        static void AddLocal(Transform parent, string objectName, Mesh mesh, Material material, Vector3 localPosition, Quaternion localRotation)
        {
            var gameObject = new GameObject(objectName);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            gameObject.transform.localRotation = localRotation;
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        // ---------------- バーナー ----------------

        /// <summary>バーナー 1 口ぶん。まとめた入れ物（バーナーの真ん中・天板の上面）を返す</summary>
        static Transform BuildBurner(Transform stove, bool right)
        {
            Vector3 c = BurnerMm(right);
            var burner = Group(stove, right ? "BurnerRight" : "BurnerLeft");
            burner.position = KitchenLayout.BurnerCenter(right);
            var onTop = new Vector3(c.x, c.y, Top);

            // 受け皿：ステンレス。外のふちを丸めて、真ん中へ向かって浅く下がる
            Lathe(burner, "SpillBowl", new[]
            {
                Part(Mm(62.6f, 0.2f), Mm(63.4f, 1.6f), Mm(63.2f, 3f), Mm(62f, 3.4f)),
                Part(Mm(62f, 3.4f), Mm(59f, 2.6f), Mm(54f, 1.2f), Mm(46f, 0.3f)),
                Part(Mm(46f, 0.3f), Mm(0, 0.3f)),
            }, onTop, KitchenMaterials.Stainless, meshName: "StoveSpillBowl");

            // バーナー：アルミの胴、炎口の並ぶ帯、黒いホーローのキャップ（昔のコンロなので、真ん中の温度センサーはない）
            const int metal = 0, enamel = 1;
            Lathe(burner, "Burner", new[]
            {
                Part(metal, Mm(31f, 0.3f), Mm(31.6f, 6f), Mm(31.4f, 11.5f)),
                Part(metal, Mm(31.4f, 11.5f), Mm(36.5f, 12.2f), Mm(37.8f, 13.5f)),
                Part(metal, Mm(37.8f, 13.5f), Mm(37.8f, 17.5f)),
                Part(enamel, Mm(37.8f, 17.5f), Mm(36.8f, 19f), Mm(30f, 20.6f), Mm(18f, 21.6f), Mm(0, 22f)),
            }, onTop, new[] { KitchenMaterials.BurnerAluminum, KitchenMaterials.BurnerEnamel }, meshName: "StoveBurner");

            BuildPorts(burner, onTop);
            BuildIgniter(burner, c);
            BuildTrivet(burner, c);
            return burner;
        }

        /// <summary>炎口：帯にあいた小さな穴を、黒い小さな四角で表す</summary>
        static void BuildPorts(Transform burner, Vector3 onTopMm)
        {
            Mesh port = RoundedBoxMesh.Build(new Vector3(0.0008f, 0.002f, 0.0026f), 0.0003f, 2);
            var pieces = new List<(Mesh, Matrix4x4)>();
            for (int i = 0; i < GasBurnerShape.PortCount; i++)
            {
                float angle = 360f * i / GasBurnerShape.PortCount;
                float rad = angle * Mathf.Deg2Rad;
                var offset = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0) * (GasBurnerShape.PortRadius + 0.1f)
                    + Up * GasBurnerShape.PortHeight;
                pieces.Add((port, At(offset, Quaternion.Euler(0, -angle, 0)))); // 箱の X を外へ向ける
            }
            Combined(burner, "BurnerPorts", onTopMm, KitchenMaterials.BurnerPort, pieces);
        }

        /// <summary>点火プラグ（白い焼き物に、バーナーへ曲がった電極）と、立ち消え安全装置の細い棒</summary>
        static void BuildIgniter(Transform burner, Vector3 c)
        {
            Vector3 plug = c + Polar(44f, 215f);
            Lathe(burner, "Igniter", new[]
            {
                Part(Mm(3f, 0), Mm(3f, 9f), Mm(2.6f, 10.6f), Mm(1.2f, 11.2f), Mm(0, 11.3f)),
            }, new Vector3(plug.x, plug.y, Top), KitchenMaterials.IgniterCeramic, meshName: "StoveIgniter");

            Vector3 inward = -Polar(1f, 215f);
            Vector3[] electrode = SweepMesh.Polyline(new[]
            {
                new Vector3(plug.x, plug.y, Top + 10.5f),
                new Vector3(plug.x, plug.y, Top + 15.5f),
                new Vector3(plug.x, plug.y, Top + 15.5f) + inward * 4.5f,
            }, 1.5f, 6);
            Sweep(burner, "IgniterElectrode", SweepMesh.Circle(0.6f, 8), electrode, inward, KitchenMaterials.Stainless,
                meshName: "StoveIgniterElectrode");

            Vector3 rod = c + Polar(44f, 240f);
            Lathe(burner, "FlameSafety", new[]
            {
                Part(Mm(1.8f, 0), Mm(1.8f, 15.5f), Mm(1.3f, 17f), Mm(0, 17.3f)),
            }, new Vector3(rod.x, rod.y, Top), KitchenMaterials.Stainless, meshName: "StoveFlameSafety");
        }

        /// <summary>
        /// 五徳（鋳物）：天板に置く輪と、そこから立ち上がって内へのびる 5 本の爪。
        /// 爪の上面が、フライパンの底の高さ（KitchenLayout.TrivetTopZ）
        /// </summary>
        static void BuildTrivet(Transform burner, Vector3 c)
        {
            const float ringRadius = 86f;
            Vector2[] ringProfile = SweepMesh.RoundedRect(7, 5, 1.8f);
            Vector3[] ring = CircleMm(c.x, c.y, Top + 2.5f, ringRadius, 64);
            Sweep(burner, "TrivetRing", ringProfile, ring, Up, KitchenMaterials.CastIron, closedPath: true,
                meshName: "StoveTrivetRing");

            const float armHeight = 8f;
            float armCenterZ = KitchenLayout.TrivetTopZ - armHeight / 2;
            Vector2[] armProfile = SweepMesh.RoundedRect(6, armHeight, 2f);
            for (int i = 0; i < 5; i++)
            {
                float angle = 90 + 72 * i; // 1 本は真うしろ
                Vector3 outward = Polar(1f, angle);
                Vector3 Point(float radius, float z) => new Vector3(c.x, c.y, 0) + outward * radius + Up * z;
                Vector3[] arm = SweepMesh.Polyline(new[]
                {
                    Point(ringRadius, Top + 2.5f),     // 輪から
                    Point(ringRadius - 2, armCenterZ), // 立ち上がって
                    Point(50, armCenterZ),             // 内へ（この上にフライパンがのる）
                    Point(47, armCenterZ - 3),         // 先は少し下げる
                }, 5f, 8);
                Sweep(burner, $"TrivetArm{i + 1}", armProfile, arm, outward, KitchenMaterials.CastIron);
            }
        }

        // ---------------- 小さな計算 ----------------

        /// <summary>バーナーの真ん中（現実の座標 mm。z は 0）</summary>
        static Vector3 BurnerMm(bool right)
        {
            float x = KitchenLayout.StoveX + (right ? KitchenLayout.RightBurnerX : KitchenLayout.LeftBurnerX);
            return new Vector3(x, KitchenLayout.StoveY + KitchenLayout.StoveDepth / 2, 0);
        }

        /// <summary>上から見て、角度 degrees（右が 0、奥が 90）の向きに radius 進んだ所（現実の座標 mm）</summary>
        static Vector3 Polar(float radius, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0) * radius;
        }

        /// <summary>水平な円の道すじ（現実の座標 mm）</summary>
        static Vector3[] CircleMm(float cx, float cy, float z, float radius, int segments)
        {
            var points = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float rad = 2 * Mathf.PI * i / segments;
                points[i] = new Vector3(cx + Mathf.Cos(rad) * radius, cy + Mathf.Sin(rad) * radius, z);
            }
            return points;
        }

        /// <summary>水平な、角の丸い長方形の道すじ（現実の座標 mm）</summary>
        static Vector3[] RoundedLoopMm(float cx, float cy, float z, float width, float depth, float radius)
        {
            Vector2[] outline = SweepMesh.RoundedRect(width, depth, radius, 6);
            var points = new Vector3[outline.Length];
            for (int i = 0; i < outline.Length; i++) points[i] = new Vector3(cx + outline[i].x, cy + outline[i].y, z);
            return points;
        }
    }
}
