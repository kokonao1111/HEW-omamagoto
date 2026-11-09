using UnityEngine;

namespace Omamagoto.Kitchen
{
    /// <summary>
    /// 湯気と、塩・こしょうの粒（パーティクル）を作る。
    /// 設定をコードに書いておくと、シーンを作り直しても同じになり、テストでも同じものが作れる。
    /// どちらも、はじめは出さない（rateOverTime = 0）。出す量は、使う側（PanView・ShakerView）が決める。
    /// </summary>
    public static class KitchenParticles
    {
        /// <summary>
        /// フライパンから上がる湯気。parent（フライパン）の底の上の、平たい範囲から出て、ゆらゆら上がりながら広がって消える
        /// </summary>
        public static ParticleSystem CreateSteam(Transform parent, Material material, float panRadius)
        {
            var system = CreateSystem(parent, "Steam", material, out var main);
            system.transform.localPosition = new Vector3(0, 0.012f, 0);

            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.0f);
            main.startSpeed = 0;
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.05f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, 2 * Mathf.PI);
            main.startColor = new Color(1, 1, 1, 0.5f);
            main.maxParticles = 300;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(panRadius * 1.4f, 0.004f, panRadius * 1.4f);

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.01f, 0.01f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.06f, 0.1f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.01f, 0.01f);

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 2f)));

            // 出てすぐ濃くなって、上がるにつれて薄く消える
            var color = system.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.45f, 0.2f), new GradientAlphaKey(0, 1) });
            color.color = fade;

            var noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.015f;
            noise.frequency = 2f;
            noise.scrollSpeed = 0.3f;

            return system;
        }

        /// <summary>
        /// 塩・こしょうの粒。parent（入れ物の口）の +Y の向きに、少し広がって飛び出し、落ちて、物に当たったら止まる
        /// </summary>
        public static ParticleSystem CreateGrains(Transform parent, Material material, Vector3 localPosition)
        {
            var system = CreateSystem(parent, "Grains", material, out var main);
            system.transform.localPosition = localPosition;
            system.transform.localRotation = Quaternion.Euler(-90, 0, 0); // パーティクルは +Z に飛ぶので、入れ物の +Y（口の向き）に合わせる

            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.0016f, 0.0026f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, 2 * Mathf.PI);
            main.gravityModifier = 1;
            main.maxParticles = 600;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14;
            shape.radius = 0.008f;

            // 当たったら止まる（はねない）
            var collision = system.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.dampen = 1;
            collision.bounce = 0;
            collision.lifetimeLoss = 0;
            collision.radiusScale = 0.5f;
            collision.enableDynamicColliders = true;

            return system;
        }

        static ParticleSystem CreateSystem(Transform parent, string name, Material material, out ParticleSystem.MainModule main)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            var system = gameObject.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // 設定を変えるために、いったん止める

            main = system.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;
            main.loop = true;
            main.duration = 1;

            var emission = system.emission;
            emission.rateOverTime = 0;

            var renderer = gameObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            system.Play();
            return system;
        }

        /// <summary>1 秒あたりに出す数を変える</summary>
        public static void SetRate(ParticleSystem system, float perSecond)
        {
            var emission = system.emission;
            emission.rateOverTime = perSecond;
        }
    }
}
