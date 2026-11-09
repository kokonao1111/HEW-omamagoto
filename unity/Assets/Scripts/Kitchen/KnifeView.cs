using System.Collections;
using Omamagoto.Events;
using Omamagoto.Kitchen.Shapes;
using UnityEngine;

namespace Omamagoto.Kitchen
{
    /// <summary>
    /// 画面の包丁。
    ///
    /// - position（item: knife）：刃先と柄の位置から、まな板の上の位置と向きを決めて、なめらかに動く。
    ///   見えなくなったら 3 秒で消す
    /// - swing：トンと振り下ろす
    ///
    /// 形は three.js 版と同じ（刃の長さ 214 mm を、おもちゃに合わせて 0.82 倍）。
    /// </summary>
    public class KnifeView : MonoBehaviour
    {
        [SerializeField] EventRouter router;

        [Header("見た目")]
        [SerializeField] Material bladeMaterial;
        [SerializeField] Material handleMaterial;
        [Tooltip("おもちゃの包丁の大きさに合わせる（実物を測って直す）")]
        [SerializeField] float sizeScale = 0.82f;

        [Header("動き")]
        [SerializeField] float followSpeed = 12f;
        [Tooltip("まな板から浮かせる高さ（m）。振り下ろすと 0 まで下がる")]
        [SerializeField] float hoverHeight = 0.03f;
        [SerializeField] float hideAfterSeconds = 3f;

        // 刃の外側の線（mm。刃の背が y = 0、刃が y = -46、柄の付け根が x = 0、刃先が x = 214）
        static readonly Vector2[] BladeOutlineMm =
        {
            new Vector2(0, 0), new Vector2(150, 0), new Vector2(180, -1), new Vector2(200, -5), new Vector2(214, -15),
            new Vector2(205, -28), new Vector2(185, -40), new Vector2(160, -45), new Vector2(120, -46), new Vector2(0, -46),
        };
        const float BladeHeightMm = 46;
        const float KnifeMiddleXMm = 45;   // 包丁の真ん中（ここを位置に合わせる）
        const float HandleEndXMm = -124;   // 柄のはし（振り下ろすときの回転の中心）

        Transform pivot;      // 柄のはし。ここを回して振り下ろす
        GameObject model;     // 表示する形（消すときはこれを消す）
        Vector3 targetPosition;
        float targetYaw;
        float chopAngle;      // 振り下ろしの角度（度。正で刃先が上がる）
        float dropRatio;      // 0 = 浮いている、1 = まな板に着いた
        bool hasBeenSeen;
        float hideTimer = -1;
        Coroutine chopping;

        /// <summary>今、見えているか（テストから見る）</summary>
        public bool IsVisible => model.activeSelf;

        void Awake()
        {
            BuildModel();
            model.SetActive(false); // カメラが見つけるまで出さない
        }

        void OnEnable()
        {
            router.OnPosition += HandlePosition;
            router.OnSwing += HandleSwing;
            router.OnReset += HandleReset;
        }

        void OnDisable()
        {
            router.OnPosition -= HandlePosition;
            router.OnSwing -= HandleSwing;
            router.OnReset -= HandleReset;
        }

        void Update()
        {
            float k = 1 - Mathf.Exp(-followSpeed * Time.deltaTime);
            float height = BladeHeightMm * Scale + hoverHeight * (1 - dropRatio);
            transform.position = Vector3.Lerp(transform.position, targetPosition + Vector3.up * height, k);
            transform.rotation = Quaternion.Euler(0, Mathf.LerpAngle(transform.eulerAngles.y, targetYaw, k), 0);
            pivot.localRotation = Quaternion.Euler(0, 0, chopAngle);

            if (hideTimer >= 0)
            {
                hideTimer -= Time.deltaTime;
                if (hideTimer < 0) model.SetActive(false);
            }
        }

        float Scale => sizeScale / 1000f; // mm → m（おもちゃの大きさに合わせた分も入れる）

        // ---------------- イベント ----------------

        void HandlePosition(ScreenEvent e)
        {
            if (e.Item != "knife") return;

            if (e.Visible == false)
            {
                hideTimer = hideAfterSeconds;
                return;
            }
            if (e.Tip == null || e.Grip == null || e.Tip.Length < 2 || e.Grip.Length < 2) return;

            Vector3 tip = KitchenLayout.BoardToWorld(e.Tip[0], e.Tip[1]);
            Vector3 grip = KitchenLayout.BoardToWorld(e.Grip[0], e.Grip[1]);
            Vector3 direction = tip - grip;

            targetPosition = (tip + grip) / 2;
            targetYaw = -Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg; // 包丁の +X を、柄 → 刃先 に向ける

            hideTimer = -1;
            if (!hasBeenSeen || !model.activeSelf)
            {
                // はじめて見えたときは、なめらかに動かさずにその場に出す
                transform.SetPositionAndRotation(targetPosition + Vector3.up * (BladeHeightMm * Scale + hoverHeight), Quaternion.Euler(0, targetYaw, 0));
                hasBeenSeen = true;
            }
            model.SetActive(true);
        }

        void HandleSwing(ScreenEvent e)
        {
            if (chopping != null) return; // 振り下ろしている途中なら、重ねない
            chopping = StartCoroutine(Chop());
        }

        void HandleReset(ScreenEvent e)
        {
            model.SetActive(false);
            hasBeenSeen = false;
            hideTimer = -1;
        }

        /// <summary>刃先を少し上げてから、トンとまな板まで下ろし、元の高さに戻す</summary>
        IEnumerator Chop()
        {
            yield return Animate(0.12f, k => chopAngle = 20f * EaseOut(k));                 // 上げる
            yield return Animate(0.08f, k => { chopAngle = 20f * (1 - k); dropRatio = k; }); // トン
            yield return Animate(0.15f, k => dropRatio = 1 - EaseOut(k));                    // 戻る
            chopAngle = 0;
            dropRatio = 0;
            chopping = null;
        }

        static IEnumerator Animate(float seconds, System.Action<float> apply)
        {
            for (float time = 0; time < seconds; time += Time.deltaTime)
            {
                apply(time / seconds);
                yield return null;
            }
            apply(1);
        }

        static float EaseOut(float k) => 1 - (1 - k) * (1 - k) * (1 - k);

        // ---------------- 形を作る ----------------

        /// <summary>
        /// 包丁の形を作る。つながり：この GameObject（包丁の真ん中）→ pivot（柄のはし）→ model（刃と柄）
        /// </summary>
        void BuildModel()
        {
            pivot = new GameObject("Pivot (handle end)").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = new Vector3((HandleEndXMm - KnifeMiddleXMm) * Scale, 0, 0);

            model = new GameObject("Model");
            model.transform.SetParent(pivot, false);
            model.transform.localPosition = new Vector3(-HandleEndXMm * Scale, 0, 0);

            // 刃
            var outline = new Vector2[BladeOutlineMm.Length];
            for (int i = 0; i < outline.Length; i++) outline[i] = BladeOutlineMm[i] * Scale;
            var blade = new GameObject("Blade");
            blade.transform.SetParent(model.transform, false);
            blade.AddComponent<MeshFilter>().sharedMesh = ExtrudedMesh.Build(outline, 1.6f * Scale);
            blade.AddComponent<MeshRenderer>().sharedMaterial = bladeMaterial;

            // 口金（刃と柄のあいだの金具）と柄（角を丸める）
            AddRoundedBox("Bolster", new Vector3(-6, -15, 0), new Vector3(12, 30, 11), 3, bladeMaterial);
            AddRoundedBox("Handle", new Vector3(-68, -17, 0), new Vector3(112, 26, 20), 8, handleMaterial);

            // 柄をとめるステンレスのびょう（3 つ）。柄をつらぬく短い円柱を、横（Z の向き）に向ける
            Mesh rivet = LatheMesh.Build(new[]
            {
                new LathePart(0, new Vector2(0, -10.6f) * Scale, new Vector2(2.6f, -10.6f) * Scale),
                new LathePart(0, new Vector2(2.6f, -10.6f) * Scale, new Vector2(2.6f, 10.6f) * Scale),
                new LathePart(0, new Vector2(2.6f, 10.6f) * Scale, new Vector2(0, 10.6f) * Scale),
            }, 16);
            foreach (float x in new[] { -34f, -68f, -102f })
            {
                var part = AddPart($"Rivet {x}", rivet, bladeMaterial);
                part.transform.localPosition = new Vector3(x, -17, 0) * Scale;
                part.transform.localRotation = Quaternion.Euler(90, 0, 0);
            }
        }

        void AddRoundedBox(string boxName, Vector3 centerMm, Vector3 sizeMm, float radiusMm, Material material)
        {
            var box = AddPart(boxName, RoundedBoxMesh.Build(sizeMm * Scale, radiusMm * Scale), material);
            box.transform.localPosition = centerMm * Scale;
        }

        /// <summary>包丁の形の 1 つの部品を足す（当たり判定はなし。見た目だけ）</summary>
        GameObject AddPart(string partName, Mesh mesh, Material material)
        {
            var part = new GameObject(partName);
            part.transform.SetParent(model.transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = material;
            return part;
        }
    }
}
