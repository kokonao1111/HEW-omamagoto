using Omamagoto.Events;
using UnityEngine;

namespace Omamagoto.Kitchen
{
    /// <summary>
    /// ガスコンロの右のバーナー（フライパンをのせる方）。
    ///
    /// - fire：火の強さ（0=切、1=弱、2=中、3=強）に合わせて、炎口から青い炎を出す。前のつまみが、その強さの所まで回る
    /// - reset：火を消す
    ///
    /// 炎は、炎口ごとに「十字に組んだ 2 枚の板」に炎の画像を貼ったもの。少しずつゆらす。
    /// このコンポーネントは、バーナーの真ん中（天板の上面）に置く。
    /// </summary>
    public class GasBurnerView : MonoBehaviour
    {
        [SerializeField] EventRouter router;
        [SerializeField] Material flameMaterial;
        [Tooltip("前のつまみ（回す所。前から見て Z のまわりに回す）")]
        [SerializeField] Transform knob;

        [Header("炎")]
        [Tooltip("火の強さ 0〜3 ごとの、炎の長さ（mm）")]
        [SerializeField] float[] flameLengthMm = { 0, 7, 12, 18 };
        [Tooltip("炎の太さ（mm）")]
        [SerializeField] float flameWidthMm = 3.6f;
        [Tooltip("炎が上を向く角度（度）。0 なら真横に出る")]
        [SerializeField] float flameUpAngle = 25;
        [Tooltip("火の強さが変わるときの速さ（秒）")]
        [SerializeField] float changeSeconds = 0.15f;

        [Header("光")]
        [Tooltip("強火のときの、炎の光の強さ")]
        [SerializeField] float lightIntensity = 0.004f;

        [Header("つまみ")]
        [Tooltip("つまみが回りきるまでの時間（秒）")]
        [SerializeField] float knobTurnSeconds = 0.25f;

        Transform[] flames;
        Light flameLight;
        float knobAngle;

        /// <summary>今の火の強さ（0〜3。イベントで届いた値）</summary>
        public int Level { get; private set; }

        /// <summary>炎の大きさ（0〜3。Level に向かって、なめらかに変わる）</summary>
        public float Flame { get; private set; }

        /// <summary>炎が見えているか</summary>
        public bool IsBurning => Flame > 0.05f;

        void Awake()
        {
            CreateFlames();
            CreateLight();
            Apply();
        }

        void OnEnable()
        {
            router.OnFire += HandleFire;
            router.OnReset += HandleReset;
        }

        void OnDisable()
        {
            router.OnFire -= HandleFire;
            router.OnReset -= HandleReset;
        }

        void HandleFire(ScreenEvent e)
        {
            if (e.Level == null) return;
            Level = Mathf.Clamp(e.Level.Value, 0, 3);
        }

        void HandleReset(ScreenEvent e)
        {
            Level = 0;
        }

        void Update()
        {
            float k = 1 - Mathf.Exp(-Time.deltaTime / changeSeconds);
            Flame = Mathf.Lerp(Flame, Level, k);
            if (Level == 0 && Flame < 0.05f) Flame = 0;

            float turn = 1 - Mathf.Exp(-Time.deltaTime / (knobTurnSeconds / 3));
            knobAngle = Mathf.LerpAngle(knobAngle, GasBurnerShape.KnobDegrees[Level], turn);
            Apply();
        }

        /// <summary>つまみの今の角度（度。前から見て左回り）</summary>
        public float KnobAngle => knobAngle;

        /// <summary>今の炎の大きさを、炎・光・つまみに映す</summary>
        void Apply()
        {
            float length = FlameLengthMm(Flame) / 1000f;
            float width = flameWidthMm / 1000f * Mathf.Lerp(0.8f, 1.1f, Flame / 3);
            for (int i = 0; i < flames.Length; i++)
            {
                // 炎口ごとに、ちがう速さで少しゆれる
                float flicker = 0.85f + 0.3f * Mathf.PerlinNoise(Time.time * 9f, i * 1.7f);
                flames[i].localScale = new Vector3(width, length * flicker, width);
                flames[i].gameObject.SetActive(length > 0.0005f);
            }

            flameLight.intensity = lightIntensity * Flame / 3 * (0.9f + 0.1f * Mathf.PerlinNoise(Time.time * 12f, 0));
            flameLight.enabled = Flame > 0.01f;

            if (knob != null) knob.localRotation = Quaternion.Euler(0, 0, knobAngle);
        }

        /// <summary>火の強さ（0〜3 の小数）のときの炎の長さ（mm）。あいだの値は、となりの段からなめらかに決める</summary>
        float FlameLengthMm(float level)
        {
            int below = Mathf.Clamp(Mathf.FloorToInt(level), 0, flameLengthMm.Length - 1);
            int above = Mathf.Min(below + 1, flameLengthMm.Length - 1);
            return Mathf.Lerp(flameLengthMm[below], flameLengthMm[above], level - below);
        }

        /// <summary>炎口の 1 つずつに、炎を置く。炎の形（板）は Y の向きに長さ 1</summary>
        void CreateFlames()
        {
            Mesh mesh = CrossedQuads();
            flames = new Transform[GasBurnerShape.PortCount];
            float radius = GasBurnerShape.PortRadius / 1000f;
            float height = GasBurnerShape.PortHeight / 1000f;
            for (int i = 0; i < flames.Length; i++)
            {
                float angle = 360f * i / flames.Length * Mathf.Deg2Rad;
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 direction = Vector3.Slerp(outward, Vector3.up, flameUpAngle / 90f);

                var flame = new GameObject($"Flame {i + 1}");
                flame.transform.SetParent(transform, false);
                flame.transform.localPosition = outward * radius + Vector3.up * height;
                flame.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
                flame.AddComponent<MeshFilter>().sharedMesh = mesh;
                var meshRenderer = flame.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = flameMaterial;
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                flames[i] = flame.transform;
            }
        }

        /// <summary>炎の光：フライパンの底や五徳、天板が、青く照らされる</summary>
        void CreateLight()
        {
            var lightObject = new GameObject("FlameLight");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = new Vector3(0, 0.022f, 0);
            flameLight = lightObject.AddComponent<Light>();
            flameLight.type = LightType.Point;
            flameLight.color = new Color(0.35f, 0.55f, 1f);
            flameLight.range = 0.25f;
            flameLight.shadows = LightShadows.None;
        }

        /// <summary>十字に組んだ 2 枚の板（幅 1・長さ 1。根もとが原点で、+Y へのびる）。どこから見ても炎の形に見える</summary>
        static Mesh CrossedQuads()
        {
            var vertices = new Vector3[8];
            var uvs = new Vector2[8];
            var normals = new Vector3[8];
            for (int plane = 0; plane < 2; plane++)
            {
                Vector3 side = plane == 0 ? Vector3.right : Vector3.forward;
                int v = plane * 4;
                vertices[v + 0] = -side * 0.5f;
                vertices[v + 1] = side * 0.5f;
                vertices[v + 2] = side * 0.5f + Vector3.up;
                vertices[v + 3] = -side * 0.5f + Vector3.up;
                uvs[v + 0] = new Vector2(0, 0);
                uvs[v + 1] = new Vector2(1, 0);
                uvs[v + 2] = new Vector2(1, 1);
                uvs[v + 3] = new Vector2(0, 1);
                for (int k = 0; k < 4; k++) normals[v + k] = Vector3.Cross(side, Vector3.up);
            }

            // 表と裏の両方から見えるように、三角を両向きに入れる
            int[] triangles =
            {
                0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3,
                4, 6, 5, 4, 7, 6, 4, 5, 6, 4, 6, 7,
            };
            var mesh = new Mesh { name = "Flame", vertices = vertices, uv = uvs, normals = normals, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
