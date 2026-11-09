using System.Collections.Generic;
using Omamagoto.Kitchen;
using Omamagoto.Kitchen.Shapes;
using UnityEngine;
using static Omamagoto.Editor.KitchenBuildTools;

namespace Omamagoto.Editor
{
    /// <summary>
    /// ガスコンロ（2 口。2026-11-09 に IH から変えた）。
    /// ガラスの天板にステンレスの縁。バーナーごとに、受け皿・バーナー（炎口つき）・温度センサー・点火プラグ・鋳物の五徳。
    /// フライパンは五徳の上にのる（底の高さは現実のおもちゃと同じ 40 mm。KitchenLayout.TrivetTopZ）
    /// </summary>
    public static class KitchenStoveBuilder
    {
        const float Top = KitchenLayout.StoveTopZ; // ガラスの天板の上面（mm）
        static readonly Vector3 Up = new Vector3(0, 0, 1); // 現実の座標の「上」（z）

        /// <summary>作ったもののうち、再生中に動かすもの（炎を出す GasBurnerView を付ける所）</summary>
        public struct Result
        {
            public Transform RightBurner;
            public Renderer RightLamp;
        }

        public static Result Build(Transform parent)
        {
            var stove = Group(parent, "GasStove");
            BuildTopPlate(stove);

            var result = new Result();
            foreach (bool right in new[] { false, true })
            {
                Transform burner = BuildBurner(stove, right);
                Renderer lamp = BuildLamp(stove, right);
                if (right)
                {
                    result.RightBurner = burner;
                    result.RightLamp = lamp;
                }
            }
            return result;
        }

        /// <summary>ガラスの天板と、まわりのステンレスの縁</summary>
        static void BuildTopPlate(Transform stove)
        {
            float x = KitchenLayout.StoveX, y = KitchenLayout.StoveY;
            float w = KitchenLayout.StoveWidth, d = KitchenLayout.StoveDepth;
            RoundedBox(stove, "GlassTop", new Vector3(x + w / 2, y + d / 2, Top / 2), new Vector3(w, d, Top), 3,
                KitchenMaterials.GlassTop, collider: true);

            // 縁：幅 5 mm・高さ 8 mm の角の丸い棒を、天板のふちに沿って 1 周させる（天板より 3 mm 高い）
            Vector3[] edge = RoundedLoopMm(x + w / 2, y + d / 2, Top - 1, w, d, 4);
            Sweep(stove, "GlassTopFrame", SweepMesh.RoundedRect(5, 8, 1.5f), edge, Up, KitchenMaterials.Stainless, closedPath: true);
        }

        /// <summary>手前の、火がついているかを知らせるランプ（オレンジに光る）と、まわりの印刷の輪</summary>
        static Renderer BuildLamp(Transform stove, bool right)
        {
            Vector3 burner = BurnerMm(right);
            var position = new Vector3(burner.x, KitchenLayout.StoveY + 26, Top);
            Lathe(stove, right ? "LampRingRight" : "LampRingLeft", new[] { Part(Mm(7.5f, 0.15f), Mm(6.3f, 0.15f)) }, position,
                KitchenMaterials.CooktopPrint, meshName: "StoveLampRing");
            var lamp = Lathe(stove, right ? "LampRight" : "LampLeft",
                new[] { Part(Mm(2.6f, 0), Mm(2.6f, 0.3f), Mm(2f, 0.5f), Mm(0, 0.55f)) }, position,
                KitchenMaterials.StoveLed, meshName: "StoveLamp");
            return lamp.GetComponent<Renderer>();
        }

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

            // バーナー：アルミの胴、炎口の並ぶ帯、黒いホーローのキャップ（真ん中にセンサーの穴）
            const int metal = 0, enamel = 1;
            Lathe(burner, "Burner", new[]
            {
                Part(metal, Mm(31f, 0.3f), Mm(31.6f, 6f), Mm(31.4f, 11.5f)),
                Part(metal, Mm(31.4f, 11.5f), Mm(36.5f, 12.2f), Mm(37.8f, 13.5f)),
                Part(metal, Mm(37.8f, 13.5f), Mm(37.8f, 17.5f)),
                Part(enamel, Mm(37.8f, 17.5f), Mm(36.8f, 19f), Mm(30f, 20.6f), Mm(18f, 21.6f), Mm(11.5f, 21.8f)),
                Part(enamel, Mm(11.5f, 21.8f), Mm(10.8f, 21f)),
            }, onTop, new[] { KitchenMaterials.BurnerAluminum, KitchenMaterials.BurnerEnamel }, meshName: "StoveBurner");

            BuildPorts(burner, onTop);
            BuildSensor(burner, c);
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

        /// <summary>
        /// 真ん中の温度センサー：ばねで持ち上がって、フライパンの底に当たる（頭の上面が五徳の上面と同じ高さ）
        /// </summary>
        static void BuildSensor(Transform burner, Vector3 c)
        {
            float head = KitchenLayout.TrivetTopZ - Top; // 天板から、センサーの頭の上面まで
            Lathe(burner, "Sensor", new[]
            {
                Part(Mm(4f, 15f), Mm(4f, head - 4.5f)),                                   // 軸
                Part(Mm(4f, head - 4.5f), Mm(9f, head - 4f)),                            // 頭の下
                Part(Mm(9f, head - 4f), Mm(10f, head - 3f), Mm(10f, head - 1f), Mm(9.2f, head - 0.1f)),
                Part(Mm(9.2f, head - 0.1f), Mm(5f, head), Mm(0, head)),                  // 頭の上
            }, new Vector3(c.x, c.y, Top), KitchenMaterials.Stainless, meshName: "StoveSensor");

            // ばね：細い線を、らせんに巻く
            var coil = new List<Vector3>();
            const float coilRadius = 6.5f, from = 20f, turns = 4.5f, stepsPerTurn = 16;
            float to = head - 5f;
            for (int i = 0; i <= turns * stepsPerTurn; i++)
            {
                float t = i / (turns * stepsPerTurn);
                float angle = t * turns * 2 * Mathf.PI;
                coil.Add(new Vector3(c.x + Mathf.Cos(angle) * coilRadius, c.y + Mathf.Sin(angle) * coilRadius, Top + Mathf.Lerp(from, to, t)));
            }
            Sweep(burner, "SensorSpring", SweepMesh.Circle(0.7f, 8), coil.ToArray(), Up, KitchenMaterials.Stainless,
                meshName: "StoveSensorSpring");
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
