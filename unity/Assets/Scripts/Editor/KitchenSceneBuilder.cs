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
    ///   KitchenRoomBuilder（天板・引き出し・壁）、KitchenApplianceBuilder（IH・電子レンジ）、
    ///   KitchenToolBuilder（まな板・皿・フライパン・塩こしょう）、KitchenLightingSetup（カメラ・光・空・描画）、
    ///   KitchenMaterials（色・つや）
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
            KitchenApplianceBuilder.Build(Group(root, "Appliances"));
            KitchenToolBuilder.Build(Group(root, "Tools"));

            var items = Group(root, "Items");
            BuildKnife(items, router);
            BuildCarrot(items, router);

            KitchenLightingSetup.Apply(root);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Selection.activeGameObject = root.gameObject;
            Debug.Log("[キッチン] 作り直しました。シーンを保存してください");
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
    }
}
