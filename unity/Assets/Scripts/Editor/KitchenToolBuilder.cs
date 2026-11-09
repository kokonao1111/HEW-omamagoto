using Omamagoto.Kitchen;
using UnityEngine;
using static Omamagoto.Editor.KitchenBuildTools;

namespace Omamagoto.Editor
{
    /// <summary>道具：まな板・皿・フライパン・塩とこしょうの入れ物</summary>
    public static class KitchenToolBuilder
    {
        /// <summary>作ったもののうち、再生中に動かすもの（PanView・ShakerView を付ける所）</summary>
        public struct Result
        {
            public Transform Pan;
            public Transform SaltShaker;
            public Transform PepperShaker;
        }

        public static Result Build(Transform parent)
        {
            BuildBoard(parent);
            BuildPlate(parent);
            var result = new Result { Pan = BuildPan(parent) };
            BuildShakers(parent, out result.SaltShaker, out result.PepperShaker);
            return result;
        }

        /// <summary>まな板：明るい木。角を 6 mm 丸める</summary>
        static void BuildBoard(Transform parent)
        {
            float x = KitchenLayout.BoardX, y = KitchenLayout.BoardY;
            float w = KitchenLayout.BoardWidth, d = KitchenLayout.BoardDepth, t = KitchenLayout.BoardThickness;
            RoundedBox(parent, "CuttingBoard", new Vector3(x + w / 2, y + d / 2, t / 2), new Vector3(w, d, t), 6,
                KitchenMaterials.BoardOak, collider: true);
        }

        /// <summary>直径 150 mm の白い平皿。裏に高台（輪の足）がある</summary>
        static void BuildPlate(Transform parent)
        {
            var parts = new[]
            {
                Part(Mm(0, 1), Mm(45, 1)),                                                  // 高台の内側の底
                Part(Mm(45, 1), Mm(46, 0), Mm(50, 0), Mm(52, 2)),                           // 高台
                Part(Mm(52, 2), Mm(62, 6), Mm(71, 12), Mm(75, 14.5f)),                      // 裏
                Part(Mm(75, 14.5f), Mm(76, 15.3f), Mm(75.6f, 16.3f), Mm(74.2f, 16.6f)),     // ふち（丸くする）
                Part(Mm(74.2f, 16.6f), Mm(70, 14.5f), Mm(60, 8.5f), Mm(50, 5.2f), Mm(44, 4.8f)), // 内側のふち
                Part(Mm(44, 4.8f), Mm(0, 4.6f)),                                            // 料理をのせる面
            };
            Vector3 center = new Vector3(KitchenLayout.PlateX + KitchenLayout.PlateWidth / 2, KitchenLayout.PlateY + KitchenLayout.PlateDepth / 2, 0);
            Lathe(parent, "Plate", parts, center, KitchenMaterials.PlateGlaze, collider: true);
        }

        /// <summary>
        /// 直径 180 mm のフライパン（右のガスコンロの五徳の上）。内側は黒いコーティング、外側は濃い灰色の金属。
        /// 柄は黒い樹脂で、ステンレスの金具でとめてある
        /// </summary>
        static Transform BuildPan(Transform parent)
        {
            const int outside = 0, inside = 1;
            var parts = new[]
            {
                Part(outside, Mm(0, 0), Mm(70, 0)),                                         // 底の裏
                Part(outside, Mm(70, 0), Mm(76, 1.5f), Mm(80, 7), Mm(87, 32), Mm(90, 40)),  // 外側
                Part(outside, Mm(90, 40), Mm(90.6f, 41.2f), Mm(89.6f, 42), Mm(88.4f, 41.6f)), // ふち（丸くする）
                Part(inside, Mm(88.4f, 41.6f), Mm(86, 35), Mm(79, 9), Mm(73, 4.2f), Mm(66, 3)), // 内側
                Part(inside, Mm(66, 3), Mm(0, 3)),                                          // 焼く面
            };
            Vector3 burner = KitchenLayout.PanBottomCenter * 1000f; // Unity（m）→ mm
            var pan = Lathe(parent, "Pan", parts, new Vector3(burner.x, burner.z, burner.y),
                new[] { KitchenMaterials.PanOutside, KitchenMaterials.PanInside }, collider: true);

            // 柄：手前の右 30 度の向き（上から見て、+X から右回りに 60 度）。先を 8 度上げる
            var handleRoot = new GameObject("PanHandle").transform;
            handleRoot.SetParent(pan, false);
            handleRoot.localPosition = new Vector3(0, 0.034f, 0);
            handleRoot.localRotation = Quaternion.Euler(0, 60, 8);

            AddLocalBox(handleRoot, "PanHandleBracket", new Vector3(0.096f, 0, 0), new Vector3(0.03f, 0.012f, 0.024f), 0.003f, KitchenMaterials.Stainless);
            AddLocalBox(handleRoot, "PanHandleGrip", new Vector3(0.19f, 0, 0), new Vector3(0.165f, 0.018f, 0.026f), 0.008f, KitchenMaterials.BlackResin);
            return pan;
        }

        /// <summary>
        /// 塩とこしょうのガラスの入れ物（中身が見える）。ふたはステンレス。木のトレイにのせる。
        /// 入れ物は再生中に動く（ShakerView）ので、底の真ん中を原点にした入れ物にまとめ、動かない印（static）を外す
        /// </summary>
        static void BuildShakers(Transform parent, out Transform salt, out Transform pepper)
        {
            float x = KitchenLayout.SpiceX, y = KitchenLayout.SpiceY;
            float w = KitchenLayout.SpiceWidth, d = KitchenLayout.SpiceDepth;
            RoundedBox(parent, "SpiceTray", new Vector3(x + w / 2, y + d / 2, 6), new Vector3(w, d, 12), 4, KitchenMaterials.BoardOak);

            var glassParts = new[]
            {
                Part(Mm(0, 0), Mm(19, 0)),
                Part(Mm(19, 0), Mm(20, 1.5f), Mm(20, 68)),
                Part(Mm(20, 68), Mm(0, 68)),
            };
            var contentParts = new[]
            {
                Part(Mm(0, 1.5f), Mm(18, 1.5f)),
                Part(Mm(18, 1.5f), Mm(18, 42)),
                Part(Mm(18, 42), Mm(0, 44)),                 // 上の面は少しだけ盛り上がる
            };
            var capParts = new[]
            {
                Part(Mm(0, 66), Mm(20.7f, 66)),
                Part(Mm(20.7f, 66), Mm(20.7f, 80), Mm(19.5f, 84), Mm(15, 87), Mm(8, 88.3f), Mm(0, 88.5f)),
            };

            salt = pepper = null;
            foreach (bool isSalt in new[] { true, false })
            {
                string label = isSalt ? "Salt" : "Pepper";
                var position = new Vector3(x + (isSalt ? 45 : 135), y + d / 2, 12);
                var shaker = Group(parent, $"{label}Shaker");
                shaker.position = KitchenLayout.ToWorld(position.x, position.y, position.z);

                Lathe(shaker, $"{label}Content", contentParts, position, isSalt ? KitchenMaterials.Salt : KitchenMaterials.Pepper,
                    meshName: "ShakerContent");
                Lathe(shaker, $"{label}Glass", glassParts, position, KitchenMaterials.Glass, meshName: "ShakerGlass");
                Lathe(shaker, $"{label}Cap", capParts, position, KitchenMaterials.Stainless, meshName: "ShakerCap");
                foreach (Transform part in shaker) part.gameObject.isStatic = false;

                if (isSalt) salt = shaker;
                else pepper = shaker;
            }
        }

        /// <summary>親の中の位置（m）に、角の丸い箱を置く</summary>
        static void AddLocalBox(Transform parent, string objectName, Vector3 localPosition, Vector3 size, float radius, Material material)
        {
            var box = new GameObject(objectName);
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.AddComponent<MeshFilter>().sharedMesh = SaveMesh(Kitchen.Shapes.RoundedBoxMesh.Build(size, radius), objectName);
            box.AddComponent<MeshRenderer>().sharedMaterial = material;
            box.isStatic = true;
        }
    }
}
