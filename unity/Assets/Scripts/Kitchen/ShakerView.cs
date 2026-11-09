using Omamagoto.Events;
using UnityEngine;

namespace Omamagoto.Kitchen
{
    /// <summary>
    /// 塩・こしょうの入れ物。
    ///
    /// - season（item が自分）：フライパンの上へ飛んでいって、さかさまにして振る。粒が落ちる。
    ///   振るたびに振る時間がのびる。振り終わったら、元の場所へ戻る
    /// - reset：元の場所に戻す
    ///
    /// このコンポーネントは、入れ物の底の真ん中（ここを原点にして、口は +Y）に付ける。
    /// </summary>
    public class ShakerView : MonoBehaviour
    {
        enum State { Home, Going, Shaking, Returning }

        [SerializeField] EventRouter router;
        [Tooltip("どの調味料か。season の item と同じ（\"salt\" か \"pepper\"）")]
        [SerializeField] string item = "salt";
        [SerializeField] Material grainMaterial;

        [Header("振る")]
        [Tooltip("1 回の season で振る秒数")]
        [SerializeField] float secondsPerSeason = 0.45f;
        [Tooltip("続けて届いても、ここまでしか振らない（秒）")]
        [SerializeField] float maxShakeSeconds = 3f;
        [Tooltip("振っているあいだの、1 秒の粒の数")]
        [SerializeField] float grainsPerSecond = 90f;
        [Tooltip("口の高さ（底から。m）")]
        [SerializeField] float mouthHeight = 0.088f;
        [Tooltip("フライパンの真ん中から、どちらに寄せるか（m。+ は右）")]
        [SerializeField] float sideOffset = -0.02f;

        const float TiltDegrees = 150;      // さかさまにする角度
        const float MouthAbovePan = 0.11f;  // フライパンの底から口までの高さ（m）
        const float ShakeStroke = 0.008f;   // 振る幅（m）
        const float ShakesPerSecond = 4.5f;

        State state = State.Home;
        Vector3 homePosition;
        Quaternion homeRotation;
        Vector3 shakeCenter;
        float shakeLeft;
        float shakeTime;
        ParticleSystem grains;

        /// <summary>元の場所にいるか</summary>
        public bool IsHome => state == State.Home;

        /// <summary>今、振っているか</summary>
        public bool IsShaking => state == State.Shaking;

        /// <summary>見えている粒の数（テスト用）</summary>
        public int GrainCount => grains.particleCount;

        void Awake()
        {
            homePosition = transform.position;
            homeRotation = transform.rotation;
            grains = KitchenParticles.CreateGrains(transform, grainMaterial, new Vector3(0, mouthHeight, 0));
        }

        void OnEnable()
        {
            router.OnSeason += HandleSeason;
            router.OnReset += HandleReset;
        }

        void OnDisable()
        {
            router.OnSeason -= HandleSeason;
            router.OnReset -= HandleReset;
        }

        void HandleSeason(ScreenEvent e)
        {
            if (e.Item != item) return;
            shakeLeft = Mathf.Min(maxShakeSeconds, shakeLeft + secondsPerSeason);
            if (state == State.Home || state == State.Returning) GoToPan();
        }

        void HandleReset(ScreenEvent e)
        {
            Flight.Stop(gameObject);
            state = State.Home;
            shakeLeft = 0;
            transform.SetPositionAndRotation(homePosition, homeRotation);
            KitchenParticles.SetRate(grains, 0);
            grains.Clear();
        }

        void Update()
        {
            if (state != State.Shaking) return;

            shakeLeft -= Time.deltaTime;
            shakeTime += Time.deltaTime;

            // 口の向きに、小きざみに振る
            float stroke = Mathf.Sin(shakeTime * ShakesPerSecond * 2 * Mathf.PI) * ShakeStroke;
            transform.position = shakeCenter + transform.up * stroke;
            KitchenParticles.SetRate(grains, grainsPerSecond);

            if (shakeLeft <= 0) GoHome();
        }

        /// <summary>フライパンの上へ飛んで、口が下（フライパンの真ん中寄り）を向くように、さかさまにする</summary>
        void GoToPan()
        {
            state = State.Going;
            // 右に寄せた入れ物は、左（真ん中）へ口を向ける。左に寄せたものは右へ
            float tilt = sideOffset >= 0 ? TiltDegrees : -TiltDegrees;
            Quaternion rotation = Quaternion.Euler(0, 0, tilt);
            Vector3 mouth = KitchenLayout.PanBottomCenter + new Vector3(sideOffset, MouthAbovePan, 0);
            Vector3 position = mouth - rotation * new Vector3(0, mouthHeight, 0);

            Flight.Begin(gameObject, position, rotation, seconds: 0.5f, arcHeight: 0.06f, onLanded: () =>
            {
                state = State.Shaking;
                shakeCenter = transform.position;
                shakeTime = 0;
            });
        }

        void GoHome()
        {
            state = State.Returning;
            shakeLeft = 0;
            KitchenParticles.SetRate(grains, 0);
            Flight.Begin(gameObject, homePosition, homeRotation, seconds: 0.55f, arcHeight: 0.06f, onLanded: () => state = State.Home);
        }
    }
}
