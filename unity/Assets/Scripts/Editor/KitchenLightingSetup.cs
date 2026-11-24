using System.Linq;
using Omamagoto.Kitchen;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Omamagoto.Editor
{
    /// <summary>
    /// カメラ・光・空・映り込み・描画の設定。明るい家のキッチン（窓から光が入る昼の台所）にする。
    /// </summary>
    public static class KitchenLightingSetup
    {
        const string SkyTexturePath = "Assets/Art/HDRI/lebombo_2k.hdr";
        const string SkyMaterialPath = "Assets/Art/Materials/SkyLebombo.mat";
        const string RendererPath = "Assets/Settings/PC_Renderer.asset";

        public static void Apply()
        {
            SetUpCamera();
            SetUpSunLight();
            SetUpSky();
            AddReflectionProbe();
            SetUpRenderQuality();
            AddAmbientOcclusion();
            SetUpPostProcessing();
        }

        /// <summary>手前の少し上から、斜めに見下ろす（three.js 版と同じ）</summary>
        static void SetUpCamera()
        {
            var camera = Camera.main;
            // 台の真ん中の、手前の上から見下ろす（台の横幅・奥行きに合わせる。2026-11-24 に実物の 900 × 340 mm にした）
            Vector3 eye = KitchenLayout.ToWorld(KitchenLayout.Width / 2, -720, 640);
            Vector3 lookAt = KitchenLayout.ToWorld(KitchenLayout.Width / 2, KitchenLayout.Depth * 0.6f, 110);
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(lookAt - eye));
            camera.fieldOfView = 38;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 30f;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowMSAA = true;
            camera.allowHDR = true;

            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            EditorUtility.SetDirty(camera);
        }

        /// <summary>
        /// 手前の左上の窓から入る、昼の光。影はやわらかく。
        /// 奥の壁にもきちんと当たるように、手前（カメラの側）から差す（横から差すと、壁が暗い灰色に見える）
        /// </summary>
        static void SetUpSunLight()
        {
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            if (sun == null) return;

            Vector3 from = KitchenLayout.ToWorld(-700, -1100, 1600);
            Vector3 to = KitchenLayout.ToWorld(600, 250, 0);
            sun.transform.rotation = Quaternion.LookRotation(to - from);
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.intensity = 2.2f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            EditorUtility.SetDirty(sun);
        }

        /// <summary>空（背景と、まわりからの光・映り込み）を、窓のある明るい室内の HDRI にする</summary>
        static void SetUpSky()
        {
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Panoramic"));
                AssetDatabase.CreateAsset(sky, SkyMaterialPath);
            }
            sky.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture>(SkyTexturePath));
            sky.SetFloat("_Mapping", 1);   // 1 = 緯度経度（HDRI のふつうの形）
            sky.SetFloat("_ImageType", 0); // 0 = 360 度
            sky.SetFloat("_Exposure", 1.1f);
            sky.SetFloat("_Rotation", 0);
            EditorUtility.SetDirty(sky);

            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>
        /// キッチンのまわりを写す「映り込み」のカメラ。ステンレスやガラス・皿に、タイルや天板が映るようにする。
        /// 再生したときに 1 回だけ写す（毎フレーム写すと重い）。
        ///
        /// Kitchen の外（シーンのいちばん上）に置いて、作り直しても消さずに使い回す。
        /// 作り直すたびに消して作ると、Unity の描画が消えた映り込みを探してエラーになり、画面が真っ白になる（2026-11-09 に確かめた）
        /// </summary>
        static void AddReflectionProbe()
        {
            const string probeName = "KitchenReflectionProbe";
            var probeObject = GameObject.Find(probeName);
            if (probeObject == null) probeObject = new GameObject(probeName);
            probeObject.transform.position = KitchenLayout.ToWorld(KitchenLayout.Width / 2, KitchenLayout.Depth / 2, 250);

            var probe = probeObject.GetComponent<ReflectionProbe>();
            if (probe == null) probe = probeObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.boxProjection = true;
            probe.size = new Vector3(1.6f, 1.0f, 1.2f);
            probe.resolution = 256;
            probe.hdr = true;
        }

        /// <summary>影を細かく・近くだけに、ふちのギザギザを消す（URP の設定）</summary>
        static void SetUpRenderQuality()
        {
            if (!(QualitySettings.renderPipeline is UniversalRenderPipelineAsset urp)) return;

            var settings = new SerializedObject(urp);
            SetInt(settings, "m_MSAA", 4);
            SetFloat(settings, "m_ShadowDistance", 3f);           // キッチンは 1 m くらいしかない
            SetInt(settings, "m_MainLightShadowmapResolution", 4096);
            SetInt(settings, "m_SoftShadowQuality", 3);           // 3 = High
            SetInt(settings, "m_ShadowCascadeCount", 1);
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>物のすき間・接する所を暗くする（SSAO）。物が台に「置いてある」感じが出る</summary>
        static void AddAmbientOcclusion()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null) return;

            var ssao = rendererData.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().FirstOrDefault();
            if (ssao == null)
            {
                ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "ScreenSpaceAmbientOcclusion";
                AssetDatabase.AddObjectToAsset(ssao, rendererData);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out _, out long localId);

                var data = new SerializedObject(rendererData);
                var features = data.FindProperty("m_RendererFeatures");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = ssao;
                var map = data.FindProperty("m_RendererFeatureMap");
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                data.ApplyModifiedPropertiesWithoutUndo();
            }

            // 小さな物（数 cm）が多いので、半径を小さくする
            var ssaoSettings = new SerializedObject(ssao);
            SetFloat(ssaoSettings, "m_Settings.Intensity", 1.6f);
            SetFloat(ssaoSettings, "m_Settings.Radius", 0.04f);
            SetFloat(ssaoSettings, "m_Settings.DirectLightingStrength", 0.35f);
            SetFloat(ssaoSettings, "m_Settings.Falloff", 3f);
            SetBool(ssaoSettings, "m_Settings.Downsample", false);
            ssaoSettings.ApplyModifiedPropertiesWithoutUndo();
            ssao.SetActive(true);
            EditorUtility.SetDirty(ssao);
            EditorUtility.SetDirty(rendererData);
        }

        /// <summary>色の仕上げ：明るい昼の台所らしく、自然な色に</summary>
        static void SetUpPostProcessing()
        {
            var volume = Object.FindFirstObjectByType<Volume>();
            if (volume == null || volume.sharedProfile == null) return;
            var profile = volume.sharedProfile;

            if (!profile.TryGet(out Tonemapping tonemapping)) tonemapping = profile.Add<Tonemapping>();
            tonemapping.mode.Override(TonemappingMode.Neutral);

            if (!profile.TryGet(out ColorAdjustments color)) color = profile.Add<ColorAdjustments>();
            color.postExposure.Override(0.15f);
            color.contrast.Override(8f);
            color.saturation.Override(4f);

            if (profile.TryGet(out Vignette vignette)) vignette.intensity.Override(0.18f);
            if (profile.TryGet(out Bloom bloom)) bloom.intensity.Override(0.25f);

            EditorUtility.SetDirty(profile);
        }

        static void SetInt(SerializedObject settings, string name, int value)
        {
            var property = settings.FindProperty(name);
            if (property != null) property.intValue = value;
            else Debug.LogWarning($"[キッチン] 設定 {name} が見つかりません（Unity のバージョンで名前が変わったかも）");
        }

        static void SetFloat(SerializedObject settings, string name, float value)
        {
            var property = settings.FindProperty(name);
            if (property != null) property.floatValue = value;
            else Debug.LogWarning($"[キッチン] 設定 {name} が見つかりません（Unity のバージョンで名前が変わったかも）");
        }

        static void SetBool(SerializedObject settings, string name, bool value)
        {
            var property = settings.FindProperty(name);
            if (property != null) property.boolValue = value;
            else Debug.LogWarning($"[キッチン] 設定 {name} が見つかりません（Unity のバージョンで名前が変わったかも）");
        }
    }
}
