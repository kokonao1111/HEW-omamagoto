using System.IO;
using Omamagoto.Events;
using Omamagoto.Kitchen;
using Omamagoto.Kitchen.Shapes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Omamagoto.Editor
{
    /// <summary>
    /// メニュー「Omamagoto → キッチンを作り直す」で、今のシーンにキッチンを並べる。
    /// 寸法は KitchenLayout（＝ tools/kitchen_plan.py の P）のとおり。現実のキッチンと同じ配置になる。
    ///
    /// 作り直すと「Kitchen」の中は全部消えて作り直されるので、手で直したい所は、ここのコードを直す。
    /// カメラ・光・空（HDRI）もここで決める。
    /// </summary>
    public static class KitchenSceneBuilder
    {
        const string RootName = "Kitchen";
        const string MeshFolder = "Assets/Art/Meshes";
        const string SkyTexturePath = "Assets/Art/HDRI/comfy_cafe_2k.hdr";

        [MenuItem("Omamagoto/キッチンを作り直す")]
        public static void Build()
        {
            var router = Object.FindFirstObjectByType<EventRouter>();
            if (router == null)
            {
                EditorUtility.DisplayDialog("キッチンを作り直す", "シーンに EventRouter（ServerEvents）がありません。先に置いてください。", "OK");
                return;
            }

            var old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(RootName).transform;
            var furniture = Group(root, "Furniture");
            BuildCounter(furniture);
            BuildBoard(furniture);
            BuildPlate(furniture);
            BuildStove(furniture);
            BuildPan(furniture);
            BuildMicrowave(furniture);
            BuildSpices(furniture);

            var items = Group(root, "Items");
            BuildKnife(items, router);
            BuildCarrot(items, router);

            SetUpCamera();
            SetUpSunLight();
            SetUpSky();
            SetUpShadows();

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Selection.activeGameObject = root.gameObject;
            Debug.Log("[キッチン] 作り直しました。シーンを保存してください");
        }

        // ---------------- 動かない物 ----------------

        static void BuildCounter(Transform parent)
        {
            float w = KitchenLayout.Width, d = KitchenLayout.Depth, t = KitchenLayout.PlyThickness;
            float baseHeight = KitchenLayout.BaseHeight, backHeight = KitchenLayout.BackHeight, wingDepth = KitchenLayout.WingDepth;

            // 天板と土台（天板の上面が高さ 0）
            Box(parent, "Counter", new Vector3(w / 2, d / 2, -baseHeight / 2), new Vector3(w, d, baseHeight), KitchenMaterials.CounterWood, collider: true);
            // 奥の壁（背板）。現実ではここにモニターがはまっている
            Box(parent, "BackWall", new Vector3(w / 2, d + t / 2, backHeight / 2), new Vector3(w + 2 * t, t, backHeight), KitchenMaterials.WallTiles);
            // 左右の袖板
            Box(parent, "WingLeft", new Vector3(-t / 2, d - wingDepth / 2, backHeight / 2), new Vector3(t, wingDepth, backHeight), KitchenMaterials.CounterWood);
            Box(parent, "WingRight", new Vector3(w + t / 2, d - wingDepth / 2, backHeight / 2), new Vector3(t, wingDepth, backHeight), KitchenMaterials.CounterWood);
        }

        static void BuildBoard(Transform parent)
        {
            float x = KitchenLayout.BoardX, y = KitchenLayout.BoardY;
            float w = KitchenLayout.BoardWidth, d = KitchenLayout.BoardDepth, t = KitchenLayout.BoardThickness;
            Box(parent, "CuttingBoard", new Vector3(x + w / 2, y + d / 2, t / 2), new Vector3(w, d, t), KitchenMaterials.BoardWood, collider: true);
        }

        static void BuildPlate(Transform parent)
        {
            // 直径 150 mm の平皿。(半径, 高さ) mm
            var parts = new[]
            {
                Part(Mm(0, 1), Mm(50, 1)),                                // 裏
                Part(Mm(50, 1), Mm(55, 3), Mm(70, 12), Mm(75, 15)),       // 外側のふち
                Part(Mm(75, 15), Mm(74, 16.5f)),                          // ふちの上
                Part(Mm(74, 16.5f), Mm(69, 14), Mm(52, 5), Mm(48, 4.5f)), // 内側のふち
                Part(Mm(48, 4.5f), Mm(0, 4.5f)),                          // 料理をのせる面
            };
            Lathe(parent, "Plate", parts, KitchenLayout.PlateCenter, KitchenMaterials.PlateCeramic, collider: true);
        }

        static void BuildStove(Transform parent)
        {
            float x = KitchenLayout.StoveX, y = KitchenLayout.StoveY;
            float w = KitchenLayout.StoveWidth, d = KitchenLayout.StoveDepth, t = KitchenLayout.PlyThickness;
            var stove = Box(parent, "Stove", new Vector3(x + w / 2, y + d / 2, t / 2), new Vector3(w, d, t), KitchenMaterials.StoveTop, collider: true);

            var burnerParts = new[]
            {
                Part(Mm(0, 0), Mm(58, 0)),
                Part(Mm(58, 0), Mm(60, 3), Mm(58, 7)),
                Part(Mm(58, 7), Mm(30, 7)),
                Part(Mm(30, 7), Mm(28, 10), Mm(24, 12)),
                Part(Mm(24, 12), Mm(0, 12)),
            };
            Lathe(stove.parent, "BurnerLeft", burnerParts, KitchenLayout.BurnerCenter(right: false), KitchenMaterials.BurnerMetal);
            Lathe(stove.parent, "BurnerRight", burnerParts, KitchenLayout.BurnerCenter(right: true), KitchenMaterials.BurnerMetal);
        }

        static void BuildPan(Transform parent)
        {
            // 半径 90 mm、深さ 40 mm のフライパン。(半径, 高さ) mm
            var parts = new[]
            {
                Part(Mm(0, 0), Mm(72, 0)),                                  // 底の裏
                Part(Mm(72, 0), Mm(80, 6), Mm(88, 34), Mm(90, 40)),         // 外側
                Part(Mm(90, 40), Mm(87, 40)),                               // ふち
                Part(Mm(87, 40), Mm(85, 35), Mm(78, 8), Mm(71, 3)),         // 内側
                Part(Mm(71, 3), Mm(0, 3)),                                  // 底の表（焼く面）
            };
            var pan = Lathe(parent, "Pan", parts, KitchenLayout.PanBottomCenter, KitchenMaterials.PanCoating, collider: true);

            // 柄：手前の右へ 30 度の向きにのばす
            var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            handle.name = "PanHandle";
            Object.DestroyImmediate(handle.GetComponent<Collider>());
            handle.transform.SetParent(pan, false);
            // 0 度 = +X（右）。上から見て右回りに 60 度で「手前の右 30 度」。Z の 8 度で、柄の先を少し上げる
            handle.transform.localRotation = Quaternion.Euler(0, 60, 8);
            handle.transform.localPosition = handle.transform.localRotation * new Vector3(0.09f + 0.075f, 0, 0) + new Vector3(0, 0.036f, 0);
            handle.transform.localScale = new Vector3(0.16f, 0.014f, 0.022f);
            handle.GetComponent<MeshRenderer>().sharedMaterial = KitchenMaterials.PanHandle;
        }

        static void BuildMicrowave(Transform parent)
        {
            float x = KitchenLayout.MicrowaveX, y = KitchenLayout.MicrowaveY;
            float w = KitchenLayout.MicrowaveWidth, d = KitchenLayout.MicrowaveDepth, h = KitchenLayout.MicrowaveHeight;
            var body = Box(parent, "Microwave", new Vector3(x + w / 2, y + d / 2, h / 2), new Vector3(w, d, h), KitchenMaterials.MicrowaveBody);

            // 手前の面に、扉のガラスと操作パネル
            Box(body.parent, "MicrowaveDoor", new Vector3(x + 15 + 87, y - 1, 115), new Vector3(175, 2, 175), KitchenMaterials.MicrowaveGlass);
            Box(body.parent, "MicrowavePanel", new Vector3(x + 235, y - 1, 115), new Vector3(60, 2, 190), KitchenMaterials.BurnerMetal);
        }

        static void BuildSpices(Transform parent)
        {
            float x = KitchenLayout.SpiceX, y = KitchenLayout.SpiceY;
            float w = KitchenLayout.SpiceWidth, d = KitchenLayout.SpiceDepth;
            Box(parent, "SpiceTray", new Vector3(x + w / 2, y + d / 2, 5), new Vector3(w, d, 10), KitchenMaterials.CounterWood);

            var bodyParts = new[]
            {
                Part(Mm(0, 0), Mm(20, 0)),
                Part(Mm(20, 0), Mm(20, 70)),
                Part(Mm(20, 70), Mm(0, 70)),
            };
            var capParts = new[]
            {
                Part(Mm(0, 70), Mm(20.5f, 70)),
                Part(Mm(20.5f, 70), Mm(20.5f, 82), Mm(17, 88)),
                Part(Mm(17, 88), Mm(0, 88)),
            };
            Vector3 salt = KitchenLayout.ToWorld(x + 45, y + d / 2, 10);
            Vector3 pepper = KitchenLayout.ToWorld(x + 135, y + d / 2, 10);
            Lathe(parent, "SaltShaker", bodyParts, salt, KitchenMaterials.SaltShaker);
            Lathe(parent, "SaltShakerCap", capParts, salt, KitchenMaterials.ShakerCap, meshName: "ShakerCap");
            Lathe(parent, "PepperShaker", bodyParts, pepper, KitchenMaterials.PepperShaker, meshName: "SaltShaker");
            Lathe(parent, "PepperShakerCap", capParts, pepper, KitchenMaterials.ShakerCap, meshName: "ShakerCap");
        }

        // ---------------- イベントで動く物 ----------------

        static void BuildKnife(Transform parent, EventRouter router)
        {
            var knife = new GameObject("Knife");
            knife.transform.SetParent(parent, false);
            var view = knife.AddComponent<KnifeView>();
            SetFields(view,
                ("router", router),
                ("bladeMaterial", KitchenMaterials.KnifeBlade),
                ("handleMaterial", KitchenMaterials.KnifeHandle));
        }

        static void BuildCarrot(Transform parent, EventRouter router)
        {
            var carrot = new GameObject("Carrot");
            carrot.transform.SetParent(parent, false);
            carrot.transform.position = KitchenLayout.BoardCenter + Vector3.up * CarrotShape.MaxRadius;
            var view = carrot.AddComponent<CarrotView>();
            SetFields(view,
                ("router", router),
                ("skinMaterial", KitchenMaterials.CarrotSkin),
                ("cutMaterial", KitchenMaterials.CarrotCut),
                ("leafMaterial", KitchenMaterials.CarrotLeaf));
        }

        // ---------------- カメラ・光・空 ----------------

        /// <summary>手前の少し上から、斜めに見下ろす（three.js 版と同じ）</summary>
        static void SetUpCamera()
        {
            var camera = Camera.main;
            Vector3 eye = KitchenLayout.ToWorld(525, -600, 640);
            Vector3 lookAt = KitchenLayout.ToWorld(525, 420, 130);
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(lookAt - eye));
            camera.fieldOfView = 38;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 30f;
            camera.clearFlags = CameraClearFlags.Skybox;

            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            EditorUtility.SetDirty(camera);
        }

        /// <summary>窓からの光のように、左の手前・上から照らす</summary>
        static void SetUpSunLight()
        {
            Light sun = null;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional) sun = light;
            }
            if (sun == null) return;

            Vector3 from = KitchenLayout.ToWorld(-500, -500, 1500);
            Vector3 to = KitchenLayout.ToWorld(560, 330, 0);
            sun.transform.rotation = Quaternion.LookRotation(to - from);
            sun.color = new Color(1f, 0.94f, 0.86f);
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            EditorUtility.SetDirty(sun);
        }

        /// <summary>空（背景と、まわりからの光・映り込み）を、カフェの室内の HDRI にする</summary>
        static void SetUpSky()
        {
            var hdri = AssetDatabase.LoadAssetAtPath<Texture>(SkyTexturePath);
            string path = "Assets/Art/Materials/SkyComfyCafe.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Panoramic"));
                AssetDatabase.CreateAsset(sky, path);
            }
            sky.SetTexture("_MainTex", hdri);
            sky.SetFloat("_Mapping", 1);   // 1 = 緯度経度（HDRI のふつうの形）
            sky.SetFloat("_ImageType", 0); // 0 = 360 度
            sky.SetFloat("_Exposure", 1f);
            EditorUtility.SetDirty(sky);

            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>影を出す距離を短くして、近くの影をくっきりさせる（キッチンは 1 m くらいしかない）</summary>
        static void SetUpShadows()
        {
            if (QualitySettings.renderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = 3f;
                EditorUtility.SetDirty(urp);
            }
        }

        // ---------------- 小さな道具 ----------------

        static Transform Group(Transform parent, string groupName)
        {
            var group = new GameObject(groupName).transform;
            group.SetParent(parent, false);
            return group;
        }

        /// <summary>
        /// 箱を置く。centerMm・sizeMm は現実の座標（mm。x 右・y 奥・z 上）。
        /// 返すのは箱の Transform（parent は置いた所の親）。
        /// </summary>
        static Transform Box(Transform parent, string boxName, Vector3 centerMm, Vector3 sizeMm, Material material, bool collider = false)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = boxName;
            if (!collider) Object.DestroyImmediate(box.GetComponent<Collider>());
            box.transform.SetParent(parent, false);
            box.transform.position = KitchenLayout.ToWorld(centerMm.x, centerMm.y, centerMm.z);
            box.transform.localScale = KitchenLayout.ToWorld(sizeMm.x, sizeMm.y, sizeMm.z);
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
            box.isStatic = true;
            return box.transform;
        }

        /// <summary>回転させた形を置く。メッシュは Assets/Art/Meshes/ に保存する</summary>
        static Transform Lathe(Transform parent, string objectName, LathePart[] parts, Vector3 position, Material material,
            bool collider = false, string meshName = null)
        {
            Mesh mesh = SaveMesh(LatheMesh.Build(parts, 64), meshName ?? objectName);

            var gameObject = new GameObject(objectName);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = position;
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collider) gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
            gameObject.isStatic = true;
            return gameObject.transform;
        }

        static Mesh SaveMesh(Mesh mesh, string meshName)
        {
            Directory.CreateDirectory(MeshFolder);
            mesh.name = meshName;
            string path = $"{MeshFolder}/{meshName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing); // 同じファイルのまま中身を入れかえる（シーンからの参照が切れないように）
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static LathePart Part(params Vector2[] points) => new LathePart(0, points);

        /// <summary>(半径, 高さ) を mm で書いて、m にする</summary>
        static Vector2 Mm(float radius, float height) => new Vector2(radius, height) / 1000f;

        /// <summary>コンポーネントの [SerializeField] の欄に、値を入れる</summary>
        static void SetFields(Object component, params (string field, Object value)[] fields)
        {
            var serialized = new SerializedObject(component);
            foreach (var (field, value) in fields)
            {
                serialized.FindProperty(field).objectReferenceValue = value;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
