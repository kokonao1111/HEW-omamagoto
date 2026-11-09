using System.Collections.Generic;
using System.IO;
using Omamagoto.Kitchen;
using Omamagoto.Kitchen.Shapes;
using UnityEditor;
using UnityEngine;

namespace Omamagoto.Editor
{
    /// <summary>
    /// キッチンを並べるときに使う、小さな道具。
    /// 位置と大きさは現実の座標（mm。x 右・y 奥・z 上）で書く。KitchenLayout.ToWorld で Unity の座標（m）に直す。
    /// </summary>
    public static class KitchenBuildTools
    {
        const string MeshFolder = "Assets/Art/Meshes";

        /// <summary>まとめるための空の GameObject</summary>
        public static Transform Group(Transform parent, string groupName)
        {
            var group = new GameObject(groupName).transform;
            group.SetParent(parent, false);
            return group;
        }

        /// <summary>
        /// 角の丸い箱を置く。centerMm・sizeMm は現実の座標（mm）、radiusMm は角の丸み（mm）。
        /// テクスチャはメートルで貼られる（RoundedBoxMesh を見る）
        /// </summary>
        public static Transform RoundedBox(Transform parent, string objectName, Vector3 centerMm, Vector3 sizeMm, float radiusMm,
            Material material, bool collider = false)
        {
            Vector3 size = ToWorldSize(sizeMm);
            Mesh mesh = SaveMesh(RoundedBoxMesh.Build(size, radiusMm / 1000f), objectName);

            var gameObject = new GameObject(objectName);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = KitchenLayout.ToWorld(centerMm.x, centerMm.y, centerMm.z);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collider) gameObject.AddComponent<BoxCollider>().size = size;
            gameObject.isStatic = true;
            return gameObject.transform;
        }

        /// <summary>回転させた形（ろくろの形）を置く。positionMm は底の真ん中（mm）</summary>
        public static Transform Lathe(Transform parent, string objectName, LathePart[] parts, Vector3 positionMm, Material[] materials,
            bool collider = false, string meshName = null)
        {
            Mesh mesh = SaveMesh(LatheMesh.Build(parts, 64, materials.Length), meshName ?? objectName);

            var gameObject = new GameObject(objectName);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = KitchenLayout.ToWorld(positionMm.x, positionMm.y, positionMm.z);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterials = materials;
            if (collider) gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
            gameObject.isStatic = true;
            return gameObject.transform;
        }

        public static Transform Lathe(Transform parent, string objectName, LathePart[] parts, Vector3 positionMm, Material material,
            bool collider = false, string meshName = null)
        {
            return Lathe(parent, objectName, parts, positionMm, new[] { material }, collider, meshName);
        }

        /// <summary>
        /// 曲がった棒を置く（SweepMesh）。
        /// profileMm は断面（mm。左回り）、pathMm は道すじ（現実の座標 mm）、upReal ははじめの点での断面の「上」の向き（現実の向き）
        /// </summary>
        public static Transform Sweep(Transform parent, string objectName, Vector2[] profileMm, Vector3[] pathMm, Vector3 upReal,
            Material material, bool closedPath = false, string meshName = null)
        {
            Vector3 originMm = Vector3.zero;
            foreach (var point in pathMm) originMm += point / pathMm.Length; // 道すじの真ん中を、物の位置にする

            var profile = new Vector2[profileMm.Length];
            for (int i = 0; i < profile.Length; i++) profile[i] = profileMm[i] / 1000f;
            var path = new Vector3[pathMm.Length];
            for (int i = 0; i < path.Length; i++) path[i] = ToWorldSize(pathMm[i] - originMm);

            Mesh mesh = SaveMesh(SweepMesh.Build(profile, path, ToWorldSize(upReal), closedPath), meshName ?? objectName);
            return Place(parent, objectName, mesh, KitchenLayout.ToWorld(originMm.x, originMm.y, originMm.z), material);
        }

        /// <summary>
        /// 同じ形をたくさん並べて、1 つのメッシュにまとめて置く（穴・すき間・ギザギザなど、小さな物がたくさんある所）。
        /// placements は、形ごとの位置と向き（Unity の座標 m。positionMm からの相対）
        /// </summary>
        public static Transform Combined(Transform parent, string objectName, Vector3 positionMm, Material material,
            IEnumerable<(Mesh mesh, Matrix4x4 placement)> pieces)
        {
            var combine = new List<CombineInstance>();
            foreach (var (pieceMesh, placement) in pieces) combine.Add(new CombineInstance { mesh = pieceMesh, transform = placement });

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(combine.ToArray(), mergeSubMeshes: true, useMatrices: true);
            mesh.RecalculateBounds();
            return Place(parent, objectName, SaveMesh(mesh, objectName), KitchenLayout.ToWorld(positionMm.x, positionMm.y, positionMm.z), material);
        }

        /// <summary>Combined で使う「位置（現実の座標 mm）と向き（Unity の回転）」</summary>
        public static Matrix4x4 At(Vector3 offsetMm, Quaternion rotation)
        {
            return Matrix4x4.TRS(ToWorldSize(offsetMm), rotation, Vector3.one);
        }

        /// <summary>メッシュを、位置（m）に置く</summary>
        public static Transform Place(Transform parent, string objectName, Mesh mesh, Vector3 position, Material material)
        {
            var gameObject = new GameObject(objectName);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = position;
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            gameObject.isStatic = true;
            return gameObject.transform;
        }

        /// <summary>ろくろの形の 1 つの部分。points は (半径, 高さ) mm。materialIndex はマテリアルの番号</summary>
        public static LathePart Part(int materialIndex, params Vector2[] pointsMm)
        {
            var points = new Vector2[pointsMm.Length];
            for (int i = 0; i < points.Length; i++) points[i] = pointsMm[i] / 1000f;
            return new LathePart(materialIndex, points);
        }

        public static LathePart Part(params Vector2[] pointsMm) => Part(0, pointsMm);

        /// <summary>(半径, 高さ) を mm で書く</summary>
        public static Vector2 Mm(float radius, float height) => new Vector2(radius, height);

        /// <summary>現実の大きさ（mm。x 幅・y 奥行き・z 高さ）→ Unity の大きさ（m）</summary>
        public static Vector3 ToWorldSize(Vector3 sizeMm) => KitchenLayout.ToWorld(sizeMm.x, sizeMm.y, sizeMm.z);

        /// <summary>メッシュを Assets/Art/Meshes/ に保存する。もうあれば中身だけ入れかえる（シーンからの参照が切れないように）</summary>
        public static Mesh SaveMesh(Mesh mesh, string meshName)
        {
            Directory.CreateDirectory(MeshFolder);
            mesh.name = meshName;
            string path = $"{MeshFolder}/{meshName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>コンポーネントの [SerializeField] の欄に、値を入れる</summary>
        public static void SetFields(Object component, params (string field, Object value)[] fields)
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
