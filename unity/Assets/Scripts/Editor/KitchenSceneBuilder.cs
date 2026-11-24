using Omamagoto.Events;
using Omamagoto.Kitchen;
using Omamagoto.Kitchen.Shapes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static Omamagoto.Editor.KitchenBuildTools;

namespace Omamagoto.Editor
{
    /// <summary>
    /// メニュー「Omamagoto → キッチンを作り直す」で、今のシーンにキッチンを並べる。
    /// 寸法は KitchenLayout（＝ tools/kitchen_plan.py の P）のとおり。現実のキッチンと同じ配置になる。
    ///
    /// 作り直すと「Kitchen」の中は全部消えて作り直されるので、手で直したい所は、ここから呼んでいるコードを直す：
    ///   KitchenRoomBuilder（天板・引き出し・壁）、KitchenStoveBuilder（ガスコンロ）、KitchenMicrowaveBuilder（電子レンジ）、
    ///   KitchenToolBuilder（まな板・皿・フライパン・塩こしょう）、KitchenLightingSetup（カメラ・光・空・描画）、
    ///   KitchenMaterials（色・つや）
    ///
    /// 再生中に動くもの（電子レンジの扉・炎・フライパン・塩こしょう・包丁・にんじん）には、ここで動かすスクリプトを付ける
    /// </summary>
    public static class KitchenSceneBuilder
    {
        const string RootName = "Kitchen";

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
            KitchenRoomBuilder.Build(Group(root, "Room"));
            var appliances = Group(root, "Appliances");
            var stove = KitchenStoveBuilder.Build(appliances);
            var microwaveDoor = KitchenMicrowaveBuilder.Build(appliances);
            var tools = KitchenToolBuilder.Build(Group(root, "Tools"));

            AddMicrowaveDoor(microwaveDoor, router);
            var burner = AddBurner(stove, router);
            var pan = AddPan(tools.Pan, burner, router);
            AddShaker(tools.SaltShaker, "salt", KitchenMaterials.SaltGrain, sideOffset: -0.02f, router);
            AddShaker(tools.PepperShaker, "pepper", KitchenMaterials.PepperGrain, sideOffset: 0.02f, router);

            var items = Group(root, "Items");
            BuildKnife(items, router);
            BuildCarrot(items, router, pan);

            KitchenLightingSetup.Apply();

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Selection.activeGameObject = root.gameObject;
            Debug.Log("[キッチン] 作り直しました。シーンを保存してください");
        }

        /// <summary>電子レンジの扉（door で開く・閉まる）</summary>
        static void AddMicrowaveDoor(Transform door, EventRouter router)
        {
            var view = door.gameObject.AddComponent<MicrowaveDoorView>();
            SetFields(view, ("router", router));
        }

        /// <summary>右のバーナーの炎（再生したときに GasBurnerView が炎を作る）と、右のつまみ</summary>
        static GasBurnerView AddBurner(KitchenStoveBuilder.Result stove, EventRouter router)
        {
            var view = stove.RightBurner.gameObject.AddComponent<GasBurnerView>();
            SetFields(view,
                ("router", router),
                ("flameMaterial", KitchenMaterials.GasFlame),
                ("knob", stove.RightKnob));
            return view;
        }

        /// <summary>フライパン（入れる・焼ける・湯気・皿へ移す）</summary>
        static PanView AddPan(Transform pan, GasBurnerView burner, EventRouter router)
        {
            var view = pan.gameObject.AddComponent<PanView>();
            SetFields(view,
                ("router", router),
                ("burner", burner),
                ("steamMaterial", KitchenMaterials.Steam));
            return view;
        }

        /// <summary>塩・こしょうの入れ物（振る）。sideOffset はフライパンの真ん中から、どちらに寄せて振るか（m）</summary>
        static void AddShaker(Transform shaker, string item, Material grainMaterial, float sideOffset, EventRouter router)
        {
            var view = shaker.gameObject.AddComponent<ShakerView>();
            SetFields(view,
                ("router", router),
                ("grainMaterial", grainMaterial));
            var serialized = new SerializedObject(view);
            serialized.FindProperty("item").stringValue = item;
            serialized.FindProperty("sideOffset").floatValue = sideOffset;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>包丁（再生したときに KnifeView が形を作る）</summary>
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

        /// <summary>にんじん（仮。野菜はチームで作ったモデルに入れかえる予定）</summary>
        static void BuildCarrot(Transform parent, EventRouter router, PanView pan)
        {
            var carrot = new GameObject("Carrot");
            carrot.transform.SetParent(parent, false);
            carrot.transform.position = KitchenLayout.BoardCenter + Vector3.up * CarrotShape.MaxRadius;
            var view = carrot.AddComponent<CarrotView>();
            SetFields(view,
                ("router", router),
                ("pan", pan),
                ("skinMaterial", KitchenMaterials.CarrotSkin),
                ("cutMaterial", KitchenMaterials.CarrotCut),
                ("leafMaterial", KitchenMaterials.CarrotLeaf));
        }
    }
}
