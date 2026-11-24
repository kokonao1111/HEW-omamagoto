using System.Collections.Generic;
using Omamagoto.Events;
using UnityEngine;

namespace Omamagoto.Kitchen
{
    /// <summary>
    /// カメラに見えている物（ピーマン・ブロッコリーなど）を、コンロの上に出す（試し。2026-11-24）。
    ///
    /// - items（zone: "stove"）：届いた一覧のとおりに、物を出して位置に合わせて動かす。一覧にない物はかくす
    /// - しばらく items が届かなければ、全部かくす（カメラが止まったとき）
    /// - reset：全部かくす
    ///
    /// 形は、本番の 3D モデルができるまでの仮（丸と円柱を組み合わせただけ）。
    /// </summary>
    public class StoveItemsView : MonoBehaviour
    {
        const string Zone = "stove";

        [SerializeField] EventRouter router;
        [SerializeField] Material pepperMaterial;
        [SerializeField] Material broccoliMaterial;
        [SerializeField] Material stemMaterial;

        [Tooltip("仮の野菜の大きさ（1 = 本物くらい。ピーマンの直径 7 cm）")]
        [SerializeField] float itemScale = 0.7f;
        [Tooltip("目標の位置に追いつくまでの時間（秒）のめやす")]
        [SerializeField] float followSeconds = 0.06f;
        [Tooltip("これだけ items が届かなければ、全部かくす（秒）")]
        [SerializeField] float hideAfterSeconds = 1.0f;

        // 物の種類ごとに、作った形を取っておいて使い回す
        readonly Dictionary<string, List<Transform>> shown = new Dictionary<string, List<Transform>>();
        readonly Dictionary<Transform, Vector3> targets = new Dictionary<Transform, Vector3>();
        readonly Dictionary<Transform, float> fallSpeeds = new Dictionary<Transform, float>(); // 落ちている速さ（m/秒）
        const float Gravity = 9.8f;
        float lastItemsTime = -999;

        /// <summary>今出ている物の数（テスト用）</summary>
        public int VisibleCount
        {
            get
            {
                int count = 0;
                foreach (var list in shown.Values)
                {
                    foreach (var item in list) if (item.gameObject.activeSelf) count++;
                }
                return count;
            }
        }

        /// <summary>今出ている、ある種類の物（テスト用）</summary>
        public List<Transform> Visible(string item)
        {
            var result = new List<Transform>();
            if (shown.TryGetValue(item, out var list))
            {
                foreach (var shape in list) if (shape.gameObject.activeSelf) result.Add(shape);
            }
            return result;
        }

        void OnEnable()
        {
            router.OnItems += HandleItems;
            router.OnReset += HandleReset;
        }

        void OnDisable()
        {
            router.OnItems -= HandleItems;
            router.OnReset -= HandleReset;
        }

        void HandleItems(ScreenEvent e)
        {
            if (e.Zone != Zone) return;
            lastItemsTime = Time.time;

            // 種類ごとに、届いた位置を集める
            var positions = new Dictionary<string, List<Vector3>>();
            foreach (var item in e.Items ?? new SeenItem[0])
            {
                if (string.IsNullOrEmpty(item.Item)) continue;
                if (!positions.ContainsKey(item.Item)) positions[item.Item] = new List<Vector3>();
                positions[item.Item].Add(KitchenLayout.StoveAreaToWorld(Mathf.Clamp01(item.X), Mathf.Clamp01(item.Y)));
            }

            foreach (var kind in shown.Keys) if (!positions.ContainsKey(kind)) positions[kind] = new List<Vector3>();
            foreach (var pair in positions) Place(pair.Key, pair.Value);
        }

        /// <summary>ある種類の物を、届いた位置の数だけ出す。前に出ていた物は、いちばん近い位置へ動かす（入れかわって飛ばないように）</summary>
        void Place(string kind, List<Vector3> positions)
        {
            if (!shown.TryGetValue(kind, out var list))
            {
                list = new List<Transform>();
                shown[kind] = list;
            }
            while (list.Count < positions.Count) list.Add(CreateShape(kind));

            var remaining = new List<Vector3>(positions);
            foreach (var shape in list)
            {
                if (remaining.Count == 0)
                {
                    shape.gameObject.SetActive(false);
                    continue;
                }
                int nearest = NearestIndex(remaining, shape.gameObject.activeSelf ? shape.position : remaining[0]);
                Vector3 target = remaining[nearest];
                remaining.RemoveAt(nearest);
                if (!shape.gameObject.activeSelf) shape.position = target; // 新しく出た物は、その場に出す
                shape.gameObject.SetActive(true);
                targets[shape] = target;
            }
        }

        static int NearestIndex(List<Vector3> candidates, Vector3 from)
        {
            int best = 0;
            for (int i = 1; i < candidates.Count; i++)
            {
                if ((candidates[i] - from).sqrMagnitude < (candidates[best] - from).sqrMagnitude) best = i;
            }
            return best;
        }

        void HandleReset(ScreenEvent e)
        {
            HideAll();
        }

        void HideAll()
        {
            foreach (var list in shown.Values)
            {
                foreach (var shape in list) shape.gameObject.SetActive(false);
            }
        }

        void Update()
        {
            if (Time.time - lastItemsTime > hideAfterSeconds)
            {
                HideAll();
                return;
            }
            foreach (var pair in targets)
            {
                if (pair.Key.gameObject.activeSelf) Follow(pair.Key, pair.Value);
            }
        }

        /// <summary>
        /// 目標の位置へ動かす。横（前後左右）はなめらかに追いかける。
        /// 高さは、下の面が低くなったら重力で落ちる（フライパンに入ったときなど）。高くなったら、すぐ上にのる
        /// </summary>
        void Follow(Transform shape, Vector3 target)
        {
            float k = 1 - Mathf.Exp(-Time.deltaTime / (followSeconds / 3));
            Vector3 position = shape.position;
            Vector3 horizontal = Vector3.Lerp(new Vector3(position.x, 0, position.z), new Vector3(target.x, 0, target.z), k);

            fallSpeeds.TryGetValue(shape, out float speed);
            float y = position.y;
            float targetY = target.y;
            if (IsInPan(target) && !IsInPan(position))
            {
                targetY = Mathf.Max(targetY, PanRimY); // フライパンに入るときは、まずふちを越える（越えたら底まで落ちる）
                if (y < PanRimY - 0.002f && IsInPan(new Vector3(horizontal.x, 0, horizontal.z)))
                {
                    horizontal = new Vector3(position.x, 0, position.z); // ふちの高さになるまで、ふちの外で待つ（ふちをすりぬけない）
                }
            }

            if (y > targetY + 0.0005f)
            {
                speed += Gravity * Time.deltaTime;
                y = Mathf.Max(targetY, y - speed * Time.deltaTime);
                if (y <= targetY) speed = 0; // のった
            }
            else
            {
                y = Mathf.Lerp(y, targetY, k * 2); // 上の面にのる（下からめりこまないよう、早めに）
                speed = 0;
            }
            fallSpeeds[shape] = speed;
            shape.position = new Vector3(horizontal.x, y, horizontal.z);
        }

        static float PanRimY => KitchenLayout.ToWorld(0, 0, KitchenLayout.PanRimZ).y;

        /// <summary>その位置が、フライパンのふちの内側か（上から見て）</summary>
        static bool IsInPan(Vector3 world)
        {
            Vector3 center = KitchenLayout.PanBottomCenter;
            float distance = new Vector2(world.x - center.x, world.z - center.z).magnitude;
            return distance < KitchenLayout.PanInnerRadius / 1000f;
        }

        // ---- 仮の形 ----

        Transform CreateShape(string kind)
        {
            var root = new GameObject(kind).transform;
            root.SetParent(transform, false);
            root.localScale = Vector3.one * itemScale;
            switch (kind)
            {
                case "pepper": BuildPepper(root); break;
                case "broccoli": BuildBroccoli(root); break;
                default: Part(root, PrimitiveType.Sphere, new Vector3(0, 0.025f, 0), new Vector3(0.05f, 0.05f, 0.05f), stemMaterial); break;
            }
            root.gameObject.SetActive(false);
            return root;
        }

        /// <summary>ピーマン：少しつぶれた緑の玉に、上に短いへた（直径 7 cm くらい）</summary>
        void BuildPepper(Transform root)
        {
            Part(root, PrimitiveType.Sphere, new Vector3(0, 0.034f, 0), new Vector3(0.07f, 0.068f, 0.066f), pepperMaterial);
            // 縦のみぞに見えるように、細い玉を 4 つ重ねる
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90 * Mathf.Deg2Rad;
                Part(root, PrimitiveType.Sphere, new Vector3(Mathf.Cos(angle) * 0.012f, 0.034f, Mathf.Sin(angle) * 0.012f),
                    new Vector3(0.05f, 0.066f, 0.05f), pepperMaterial);
            }
            Part(root, PrimitiveType.Cylinder, new Vector3(0, 0.072f, 0), new Vector3(0.012f, 0.008f, 0.012f), stemMaterial);
        }

        /// <summary>ブロッコリー：太い茎の上に、小さい玉をいくつか集めたつぼみ（高さ 8 cm くらい）</summary>
        void BuildBroccoli(Transform root)
        {
            Part(root, PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0), new Vector3(0.022f, 0.02f, 0.022f), stemMaterial);
            Part(root, PrimitiveType.Sphere, new Vector3(0, 0.055f, 0), new Vector3(0.045f, 0.035f, 0.045f), broccoliMaterial);
            for (int i = 0; i < 6; i++)
            {
                float angle = i * 60 * Mathf.Deg2Rad;
                Part(root, PrimitiveType.Sphere, new Vector3(Mathf.Cos(angle) * 0.02f, 0.05f, Mathf.Sin(angle) * 0.02f),
                    new Vector3(0.028f, 0.028f, 0.028f), broccoliMaterial);
            }
        }

        static void Part(Transform root, PrimitiveType type, Vector3 localPosition, Vector3 size, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            Object.Destroy(part.GetComponent<Collider>()); // 物理にはまざらない（にんじんのピースなどを押さない）
            part.transform.SetParent(root, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = size;
            if (material != null) part.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
