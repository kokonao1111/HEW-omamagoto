using System.Collections.Generic;
using System.Linq;
using Omamagoto.Events;
using Omamagoto.Kitchen.Shapes;
using UnityEngine;

namespace Omamagoto.Kitchen
{
    /// <summary>
    /// 画面のにんじん（仮。野菜はチームで作ったモデルに入れかえる予定）。
    ///
    /// - position（item: carrot）：まな板の上の位置と向きに合わせて、なめらかに動く。見えなくなったら 3 秒で消す
    /// - cut：最初の 1 回で、一気に輪切りになって、バラッと少し転がる（2026-11-09 に決めた見せ方）。
    ///        2 回目からの cut は、ピースは増やさない（数だけ覚えておく。add で使う）
    /// - add：まな板の上のピースを、フライパン（zone が "plate" なら皿）へ飛ばす。
    ///        サーバーの数（cut の count）と画面のピースの数はちがうので、「まな板の上の何割が入ったか」で、飛ばす数を決める
    /// - reset：まるごとの 1 本に戻して、まな板の真ん中に置く
    /// </summary>
    public class CarrotView : MonoBehaviour
    {
        [SerializeField] EventRouter router;
        [Tooltip("ピースを入れるフライパン（なければ add は何もしない）")]
        [SerializeField] PanView pan;

        [Header("見た目")]
        [SerializeField] Material skinMaterial;
        [SerializeField] Material cutMaterial;
        [SerializeField] Material leafMaterial;

        [Header("動き")]
        [Tooltip("位置に追いつく速さ（大きいほど速い）")]
        [SerializeField] float followSpeed = 10f;
        [Tooltip("見えなくなってから消すまでの秒数")]
        [SerializeField] float hideAfterSeconds = 3f;

        [Header("切ったとき")]
        [Tooltip("輪切りの枚数（葉の付いた端を除く）")]
        [SerializeField] int sliceCount = 18;
        [Tooltip("葉の付いた端の長さ（にんじん全体を 1 としたとき）")]
        [SerializeField] float leafEndLength = 0.1f;
        [Tooltip("バラッと散る強さ（m/秒）")]
        [SerializeField] float scatterSpeed = 0.3f;

        [Header("フライパンに入れるとき")]
        [Tooltip("ピースを 1 つずつ、この秒数ずつずらして飛ばす")]
        [SerializeField] float secondsBetweenPieces = 0.08f;

        const float StartYaw = 12f; // リセットしたときの向き（度）

        GameObject wholeCarrot;                                         // まるごとの 1 本
        readonly List<GameObject> slices = new List<GameObject>();      // 切ったあとのピース（どこにあっても）
        readonly List<GameObject> onBoard = new List<GameObject>();     // そのうち、まな板の上に残っているもの
        Vector3 targetPosition;
        float targetYaw;
        float hideTimer = -1;  // 0 以上なら、0 になったら消す
        bool isCut;
        bool wholeOnBoard;     // まるごとの 1 本が、まな板の上にあるか（切らずにフライパンへ入れたら false）
        int reportedPieces;    // サーバーが数えた、ピースの数（cut の count）
        int reportedOnBoard;   // サーバーが数えた、まな板の上のピースの数

        /// <summary>切ってあるか（テストや、ほかの演出から見る）</summary>
        public bool IsCut => isCut;

        /// <summary>切ったあとのピース</summary>
        public IReadOnlyList<GameObject> Slices => slices;

        /// <summary>切ったあとのピースのうち、まな板の上に残っているもの</summary>
        public IReadOnlyList<GameObject> SlicesOnBoard => onBoard;

        void Awake()
        {
            PutBackOnBoard();
        }

        void OnEnable()
        {
            router.OnPosition += HandlePosition;
            router.OnCut += HandleCut;
            router.OnAdd += HandleAdd;
            router.OnReset += HandleReset;
        }

        void OnDisable()
        {
            router.OnPosition -= HandlePosition;
            router.OnCut -= HandleCut;
            router.OnAdd -= HandleAdd;
            router.OnReset -= HandleReset;
        }

        void Update()
        {
            if (wholeOnBoard && !isCut)
            {
                float k = 1 - Mathf.Exp(-followSpeed * Time.deltaTime); // フレームの速さが変わっても同じ動きになる
                transform.position = Vector3.Lerp(transform.position, targetPosition, k);
                float yaw = Mathf.LerpAngle(transform.eulerAngles.y, targetYaw, k);
                transform.rotation = Quaternion.Euler(0, yaw, 0);
            }

            if (hideTimer >= 0)
            {
                hideTimer -= Time.deltaTime;
                if (hideTimer < 0 && wholeOnBoard) wholeCarrot.SetActive(false);
            }
        }

        // ---------------- イベント ----------------

        void HandlePosition(ScreenEvent e)
        {
            if (e.Item != "carrot") return;
            if (isCut || !wholeOnBoard) return; // 切ったあと・フライパンへ入れたあとは、ピースをその場に残す

            if (e.Visible == false)
            {
                hideTimer = hideAfterSeconds;
                return;
            }

            hideTimer = -1;
            wholeCarrot.SetActive(true);
            if (e.X != null && e.Y != null)
            {
                targetPosition = KitchenLayout.BoardToWorld(e.X.Value, e.Y.Value) + Vector3.up * CarrotShape.MaxRadius;
            }
            if (e.Rotation != null)
            {
                targetYaw = -e.Rotation.Value; // カメラの角度は左回り、Unity の Y 回転は右回り
            }
        }

        void HandleCut(ScreenEvent e)
        {
            if (e.Item != null && e.Item != "carrot") return;

            // サーバーの数を覚えておく（ピースが増えた分だけ、まな板の上も増える）
            if (e.Count != null && e.Count.Value > reportedPieces)
            {
                reportedOnBoard += e.Count.Value - reportedPieces;
                reportedPieces = e.Count.Value;
            }

            if (isCut || !wholeOnBoard) return; // 2 回目からは、見た目は変えない
            CutIntoSlices();
        }

        void HandleAdd(ScreenEvent e)
        {
            if (e.Item != null && e.Item != "carrot") return;
            if (pan == null) return;
            bool toPlate = e.Zone == "plate";
            int count = Mathf.Max(1, e.Count ?? 1);

            if (!isCut)
            {
                if (wholeOnBoard) SendWhole(toPlate);
                return;
            }

            int moveCount = PiecesToMove(count);
            reportedOnBoard = Mathf.Max(0, reportedOnBoard - count);

            // 行き先に近いピースから飛ばす
            Vector3 destination = toPlate ? KitchenLayout.PlateCenter : pan.transform.position;
            var chosen = onBoard.OrderBy(piece => Vector3.Distance(piece.transform.position, destination)).Take(moveCount).ToList();
            for (int i = 0; i < chosen.Count; i++)
            {
                onBoard.Remove(chosen[i]);
                Send(chosen[i], toPlate, i * secondsBetweenPieces);
            }
        }

        void HandleReset(ScreenEvent e)
        {
            PutBackOnBoard();
        }

        /// <summary>
        /// add の count（サーバーが数えたピースの数）を、画面のピースの数に直す。
        /// 例：サーバーではまな板に 4 個あって 2 個入った → 画面のまな板の上のピースの半分を飛ばす。
        /// まな板の上の全部が入ったときは、残りを全部飛ばす
        /// </summary>
        int PiecesToMove(int count)
        {
            if (onBoard.Count == 0) return 0;
            if (reportedOnBoard <= 0 || count >= reportedOnBoard) return onBoard.Count;
            int share = Mathf.RoundToInt(onBoard.Count * (float)count / reportedOnBoard);
            return Mathf.Clamp(share, 1, onBoard.Count);
        }

        void Send(GameObject piece, bool toPlate, float delay)
        {
            piece.transform.SetParent(null, true); // にんじんの入れ物から外す（入れ物が動いても、ついていかない）
            if (toPlate) pan.PutOnPlate(piece, delay);
            else pan.Receive(piece, delay);
        }

        /// <summary>切らずに、まるごとの 1 本を入れる</summary>
        void SendWhole(bool toPlate)
        {
            wholeOnBoard = false;
            hideTimer = -1;
            wholeCarrot.SetActive(true);
            MakeLoose(wholeCarrot);
            Send(wholeCarrot, toPlate, 0);
        }

        // ---------------- 切る・戻す ----------------

        /// <summary>まるごとの 1 本を消して、輪切りのピースに置きかえ、少し散らす</summary>
        void CutIntoSlices()
        {
            isCut = true;
            hideTimer = -1;
            wholeCarrot.SetActive(false);

            // 葉の付いた端：転がると葉がまな板に刺さって見えるので、横にすべるだけにする
            var leafEnd = CreatePiece("LeafEnd", 0, leafEndLength);
            AddLeaves(leafEnd.transform);
            Rigidbody leafBody = Scatter(leafEnd);
            leafBody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            AddSlice(leafEnd);

            // 残りを同じ厚さの輪切りにして、パタッと倒す
            float sliceLength = (1 - leafEndLength) / sliceCount;
            for (int i = 0; i < sliceCount; i++)
            {
                float t0 = leafEndLength + i * sliceLength;
                var slice = CreatePiece($"Slice {i + 1}", t0, t0 + sliceLength);
                Rigidbody body = Scatter(slice);
                Stagger(slice, i);
                TipOver(slice, body);
                AddSlice(slice);
            }
        }

        void AddSlice(GameObject slice)
        {
            slices.Add(slice);
            onBoard.Add(slice);
        }

        /// <summary>
        /// 切る前の 1 本に戻して、まな板の真ん中に置く。
        /// ピースは、フライパンや皿にあるものも含めて全部消す
        /// </summary>
        void PutBackOnBoard()
        {
            foreach (var slice in slices)
            {
                if (slice != null) Destroy(slice);
            }
            slices.Clear();
            onBoard.Clear();

            // まるごとの 1 本がまな板から出ていったときは、作り直す
            if (wholeCarrot != null && !wholeOnBoard) Destroy(wholeCarrot);
            if (wholeCarrot == null || !wholeOnBoard)
            {
                wholeCarrot = CreatePiece("Whole", 0, 1);
                AddLeaves(wholeCarrot.transform);
            }

            isCut = false;
            wholeOnBoard = true;
            hideTimer = -1;
            reportedPieces = 1;
            reportedOnBoard = 1;
            wholeCarrot.SetActive(true);

            targetPosition = KitchenLayout.BoardCenter + Vector3.up * CarrotShape.MaxRadius;
            targetYaw = StartYaw;
            transform.SetPositionAndRotation(targetPosition, Quaternion.Euler(0, targetYaw, 0));
        }

        // ---------------- 形を作る ----------------

        /// <summary>にんじんの t0〜t1 の部分を作って、にんじんの中の同じ所に置く。焼けるようにする</summary>
        GameObject CreatePiece(string pieceName, float t0, float t1)
        {
            var piece = new GameObject(pieceName);
            piece.transform.SetParent(transform, false);
            piece.transform.localPosition = new Vector3(CarrotShape.PieceCenterX(t0, t1), 0, 0);

            piece.AddComponent<MeshFilter>().sharedMesh = CarrotShape.BuildPiece(t0, t1);
            piece.AddComponent<MeshRenderer>().sharedMaterials = new[] { skinMaterial, cutMaterial };
            piece.AddComponent<CookablePiece>();
            return piece;
        }

        /// <summary>葉の付け根から、細い茎を何本か出す</summary>
        void AddLeaves(Transform piece)
        {
            float leafEndX = -CarrotShape.Length / 2 - piece.localPosition.x; // 部分の中での葉の側の端
            var stemMesh = LatheMesh.Build(new[]
            {
                new LathePart(0, new Vector2(0, 0), new Vector2(0.0022f, 0)),
                new LathePart(0, new Vector2(0.0022f, 0), new Vector2(0.0012f, 0.04f), new Vector2(0, 0.07f)),
            }, 8);

            float[] spreadAngles = { -28f, -10f, 12f, 30f };
            for (int i = 0; i < spreadAngles.Length; i++)
            {
                var stem = new GameObject($"Leaf {i + 1}");
                stem.transform.SetParent(piece, false);
                stem.transform.localPosition = new Vector3(leafEndX + 0.004f, 0, 0);
                // 茎は上（+Y）向きに作ってあるので、Z で 75 度倒して -X（葉の側）の少し上へ向け、Y で扇のように開く
                stem.transform.localRotation = Quaternion.Euler(0, spreadAngles[i], 75);
                stem.AddComponent<MeshFilter>().sharedMesh = stemMesh;
                stem.AddComponent<MeshRenderer>().sharedMaterial = leafMaterial;
            }
        }

        /// <summary>ピースに重さと当たり判定を付ける（転がったり、落ちたりするようになる）</summary>
        static Rigidbody MakeLoose(GameObject piece)
        {
            var meshCollider = piece.AddComponent<MeshCollider>();
            meshCollider.convex = true;
            meshCollider.contactOffset = 0.001f; // 小さい物なので、当たり判定のすき間も小さく

            var body = piece.AddComponent<Rigidbody>();
            body.mass = 0.01f;
            body.linearDamping = 0.5f;
            body.angularDamping = 0.8f;
            body.sleepThreshold = 0.0005f; // 小さくて軽いので、止まったと決めるのを遅くする（立ったまま止まらないように）
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            return body;
        }

        /// <summary>切ったピースに重さを付けて、少しはじく（バラッと散る）</summary>
        Rigidbody Scatter(GameObject piece)
        {
            Rigidbody body = MakeLoose(piece);

            // となりのピースとくっついたままだと、寄りかかって倒れないので、少しすき間をあける
            piece.transform.localPosition *= 1.25f;

            // 真ん中から両はしへ広がるように、横にも少しずつはじく
            float along = piece.transform.localPosition.x / (CarrotShape.Length / 2); // にんじんの中の位置（-1〜1）
            Vector3 spread = transform.right * along;
            Vector3 side = transform.forward * Random.Range(-1f, 1f);
            body.linearVelocity = (spread + side + Vector3.up * 0.5f) * scatterSpeed;
            return body;
        }

        /// <summary>
        /// 輪切りを 1 枚おきに、手前と奥へ少しずらす。
        /// 1 列に並んだままだと、倒れた 1 枚に次の 1 枚が寄りかかって、立ったまま止まってしまう
        /// </summary>
        void Stagger(GameObject slice, int index)
        {
            float side = index % 2 == 0 ? 1 : -1;
            slice.transform.position += transform.forward * side * 0.009f;
        }

        /// <summary>
        /// 輪切りは縁で立っているので、コインのように立ったまま止まりやすい。
        /// はじめから少し傾けて、倒れる向き（にんじんの向きと直角な横の軸）に回す。
        /// 全部を同じ向き（先っぽの側）に倒す。ばらばらの向きだと、となりどうしで寄りかかって立ったままになる
        /// </summary>
        void TipOver(GameObject slice, Rigidbody body)
        {
            slice.transform.Rotate(transform.forward, -Random.Range(20f, 35f), Space.World);
            body.angularVelocity = -transform.forward * Random.Range(6f, 10f) + Random.insideUnitSphere;
        }
    }
}
