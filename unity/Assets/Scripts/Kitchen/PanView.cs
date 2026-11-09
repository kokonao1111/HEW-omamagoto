using System.Collections.Generic;
using Omamagoto.Events;
using UnityEngine;

namespace Omamagoto.Kitchen
{
    /// <summary>
    /// フライパンと皿（焼いて、盛りつける所）。
    ///
    /// - ピースを受け取る（Receive）：山なりに飛ばしてフライパンに入れる。火がついていれば、ジュッと湯気が上がる
    /// - 火（GasBurnerView）がついているあいだ、フライパンが温まり、中のピースが焼けて色が変わる。湯気が上がり、ピースがときどき跳ねる
    /// - serve：フライパンのピースを皿に移す
    /// - reset：中を空にして、冷ます（ピースを消すのは、ピースを作った側。CarrotView など）
    ///
    /// このコンポーネントは、フライパン（底の真ん中が原点）に付ける。
    /// </summary>
    public class PanView : MonoBehaviour
    {
        [SerializeField] EventRouter router;
        [SerializeField] GasBurnerView burner;
        [SerializeField] Material steamMaterial;

        [Header("焼ける")]
        [Tooltip("火の強さ 1 で、1 秒に進む焼け具合（1 で焼けた、2.2 でこげた）。強火（3）なら 3 倍")]
        [SerializeField] float cookPerSecondAtLevel1 = 0.04f;
        [Tooltip("フライパンが温まる・冷めるのにかかる時間（秒）")]
        [SerializeField] float heatUpSeconds = 4f;

        [Header("湯気と跳ねる")]
        [Tooltip("強火で、ピースがいっぱいのときの、1 秒の湯気の数")]
        [SerializeField] float steamPerSecond = 14f;
        [Tooltip("強火のとき、1 秒にピースが跳ねる回数")]
        [SerializeField] float hopsPerSecond = 3f;

        [Header("置き場所")]
        [Tooltip("ピースを置く範囲の半径（m）。フライパンの平らな底の中")]
        [SerializeField] float panSpotRadius = 0.05f;
        [Tooltip("皿の上の、ピースを置く範囲の半径（m）")]
        [SerializeField] float plateSpotRadius = 0.035f;

        const float PanFloorHeight = 0.003f;    // フライパンの底の、焼く面の高さ（m）
        const float PlateFloorHeight = 0.0046f; // 皿の、料理をのせる面の高さ（m）
        const float DropHeight = 0.02f;         // この高さまで飛ばして、そこから落とす

        readonly List<CookablePiece> inPan = new List<CookablePiece>();
        readonly List<CookablePiece> onPlate = new List<CookablePiece>();
        ParticleSystem steam;

        /// <summary>フライパンの中のピース</summary>
        public IReadOnlyList<CookablePiece> InPan => inPan;

        /// <summary>皿の上のピース</summary>
        public IReadOnlyList<CookablePiece> OnPlate => onPlate;

        /// <summary>フライパンの熱さ（0〜3。火の強さに、ゆっくり近づく）</summary>
        public float Heat { get; private set; }

        /// <summary>フライパンの中のピースの、焼け具合の平均（ピースがなければ 0）</summary>
        public float AverageDoneness
        {
            get
            {
                if (inPan.Count == 0) return 0;
                float sum = 0;
                foreach (var piece in inPan) sum += piece.Doneness;
                return sum / inPan.Count;
            }
        }

        /// <summary>湯気の粒の数（テスト用）</summary>
        public int SteamParticleCount => steam.particleCount;

        void Awake()
        {
            steam = KitchenParticles.CreateSteam(transform, steamMaterial, panSpotRadius);
        }

        void OnEnable()
        {
            router.OnServe += HandleServe;
            router.OnReset += HandleReset;
        }

        void OnDisable()
        {
            router.OnServe -= HandleServe;
            router.OnReset -= HandleReset;
        }

        // ---------------- ピースを受け取る・移す ----------------

        /// <summary>ピースを、フライパンの中へ飛ばして入れる。delay 秒待ってから飛ぶ</summary>
        public void Receive(GameObject piece, float delay = 0)
        {
            var cookable = piece.GetComponent<CookablePiece>();
            Vector3 spot = RandomSpot(transform.position + Vector3.up * PanFloorHeight, panSpotRadius);
            Flight.Begin(piece, spot, RandomYaw(piece.transform.rotation), seconds: 0.7f, arcHeight: 0.12f, delay: delay, onLanded: () =>
            {
                if (cookable != null) inPan.Add(cookable);
                if (Heat > 0.3f) steam.Emit(Mathf.RoundToInt(4 + Heat * 2)); // ジュッ
            });
        }

        /// <summary>ピースを、皿の上へ飛ばしてのせる</summary>
        public void PutOnPlate(GameObject piece, float delay = 0)
        {
            var cookable = piece.GetComponent<CookablePiece>();
            if (cookable != null) inPan.Remove(cookable);
            Vector3 spot = RandomSpot(KitchenLayout.PlateCenter + Vector3.up * PlateFloorHeight, plateSpotRadius);
            Flight.Begin(piece, spot, RandomYaw(piece.transform.rotation), seconds: 0.6f, arcHeight: 0.1f, delay: delay, onLanded: () =>
            {
                if (cookable != null && !onPlate.Contains(cookable)) onPlate.Add(cookable);
            });
        }

        void HandleServe(ScreenEvent e)
        {
            var pieces = new List<CookablePiece>(inPan);
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i] != null) PutOnPlate(pieces[i].gameObject, i * 0.08f);
            }
        }

        void HandleReset(ScreenEvent e)
        {
            inPan.Clear();
            onPlate.Clear();
            Heat = 0;
            steam.Clear();
        }

        // ---------------- 焼く ----------------

        void Update()
        {
            inPan.RemoveAll(piece => piece == null); // 消えたピース（リセットなど）は数えない

            float fire = burner != null ? burner.Flame : 0;
            Heat = Mathf.Lerp(Heat, fire, 1 - Mathf.Exp(-Time.deltaTime / heatUpSeconds));
            if (fire == 0 && Heat < 0.02f) Heat = 0;

            foreach (var piece in inPan) piece.Cook(Time.deltaTime * Heat * cookPerSecondAtLevel1);

            // 湯気：熱いほど、ピースが多いほど（6 個でいっぱい）たくさん
            float fill = Mathf.Min(1, inPan.Count / 6f);
            KitchenParticles.SetRate(steam, steamPerSecond * Heat / 3 * fill);

            Hop();
        }

        /// <summary>熱いと、ピースがときどきピョンと跳ねる（油がはねる感じ）</summary>
        void Hop()
        {
            if (inPan.Count == 0 || Heat < 1) return;
            if (Random.value > hopsPerSecond * (Heat / 3) * Time.deltaTime) return;

            var piece = inPan[Random.Range(0, inPan.Count)];
            var body = piece.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic) return;
            body.WakeUp();
            body.linearVelocity += Vector3.up * Random.Range(0.08f, 0.14f) + Random.insideUnitSphere * 0.02f;
            body.angularVelocity += Random.insideUnitSphere * 3f;
        }

        // ---------------- 小さな計算 ----------------

        /// <summary>center のまわり radius の円の中の、どこか 1 か所（少し上から落とす）</summary>
        static Vector3 RandomSpot(Vector3 center, float radius)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            return center + new Vector3(offset.x, DropHeight, offset.y);
        }

        /// <summary>今の向きのまま、上から見た向きだけ、ばらばらに回す</summary>
        static Quaternion RandomYaw(Quaternion rotation)
        {
            return Quaternion.Euler(0, Random.Range(0f, 360f), 0) * rotation;
        }
    }
}
