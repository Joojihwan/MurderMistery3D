using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MurderMystery.EditorTools
{
    /// <summary>
    /// Dresses the cast in garments from the KHS "Joseon Royal Wear" package and saves one prefab per
    /// character to Resources/Characters/&lt;id&gt; (loaded by NetPlayer and the manor builder).
    /// The garments are static meshes modelled around one shared body origin, so an outfit is just the
    /// chosen pieces at that origin. They come without a body, so a simple head is added.
    /// </summary>
    public static class CharacterOutfitBuilder
    {
        private const string DemoPath = KhsAssetTools.Root + "/Joseon_Royal_Wear/Scenes/Joseon_Royal_Wear.unity";
        private const string OutDir = "Assets/_Project/Resources/Characters";
        private const string SkinMatPath = "Assets/_Project/Art/Materials/Skin.mat";
        private const string EyeMatPath = "Assets/_Project/Art/Materials/Eye.mat";

        // Top of the head in the garments' own space (망건/쪽머리 tops), used to scale to each height.
        private const float MaleHeadTop = 1.86f;
        private const float FemaleHeadTop = 1.82f;

        private struct Outfit
        {
            public string Id;
            public bool Female;
            public float Height;   // docs/design/timeline.md §0 (build matters for the sightings)
            public float Girth;    // extra width/depth (변 행수 is stout)
            public string[] Pieces;
        }

        // Late-Joseon dress by status: 서자 선비 in 도포 and 갓; 중인 의원 indoors in 탕건 without the 갓;
        // a merchant head with a 쾌자(전복) over the 두루마기; a widowed daughter-in-law in a plain white
        // 적삼 with 쪽머리; an unmarried maid with a long 댕기 braid; the legal clerk in outdoor duty dress.
        private static readonly Outfit[] Cast =
        {
            new Outfit { Id = "jaeha", Height = 1.80f, Girth = 1f,
                Pieces = new[] { "SM_Pants", "SM_Dopo", "SM_Beoseon01a", "SM_Taesa_Hye", "SM_Gat" } },
            new Outfit { Id = "heo", Height = 1.55f, Girth = 1.05f,
                // No 저고리 under the 두루마기: the two meshes intersect.
                Pieces = new[] { "SM_Pants", "SM_Durumagi01a", "SM_Beoseon01a", "SM_Taesa_Hye", "SM_Tanggeon" } },
            new Outfit { Id = "byeon", Height = 1.70f, Girth = 1.3f,
                Pieces = new[] { "SM_Pants", "SM_Durumagi01a", "SM_Jeonbok01a", "SM_Leg_Wraps", "SM_Beoseon01a", "SM_Taesa_Hye", "SM_Gat" } },
            new Outfit { Id = "jeong", Female = true, Height = 1.58f, Girth = 1f,
                Pieces = new[] { "SM_Underpants", "SM_skirt", "SM_Jeoksam", "SM_Beoseon02a", "SM_Danghye", "SM_Jjokmeori" } },
            new Outfit { Id = "buni", Female = true, Height = 1.52f, Girth = 1f,
                Pieces = new[] { "SM_Underpants", "SM_Daesum_Chima", "SM_Inner_jeogori", "SM_Beoseon02a", "SM_Pigtails01b" } },
            new Outfit { Id = "dogeomnyul", Height = 1.68f, Girth = 1.05f,
                // Outdoor duty dress (동달이 + 전복, 행전, 갓) rather than the ankle-length 흑단령: the legs must show
                // for Mixamo's auto-rigger.
                Pieces = new[] { "SM_Pants", "SM_Leg_Wraps", "SM_Beoseon01a", "SM_Dongdari", "SM_Jeonbok01b", "SM_Black_Shoes", "SM_Gat" } },
        };

        /// <summary>
        /// Floor-length skirts. Mixamo cannot find the legs inside one, so they are left out of the rigging
        /// export (the 속바지 underneath gives it legs) and rejoined to the hips bone afterwards.
        /// </summary>
        public static readonly HashSet<string> Skirts = new HashSet<string> { "SM_skirt", "SM_Daesum_Chima", "SM_Suran_Skirt", "SM_Daeran_Chima" };

        [MenuItem("Tools/Murder Mystery/KHS/Build Character Outfits")]
        public static void BuildAll()
        {
            if (!System.IO.File.Exists(DemoPath))
            {
                Debug.LogError($"[Outfits] {DemoPath} not found. Import the KHS Joseon Royal Wear package first.");
                return;
            }
            ManorBlockoutBuilder.EnsureFolder(OutDir);
            var skin = EnsureMaterial(SkinMatPath, new Color(0.80f, 0.64f, 0.52f), 0.25f);
            var eye = EnsureMaterial(EyeMatPath, new Color(0.08f, 0.06f, 0.05f), 0.5f);

            var previous = SceneManager.GetActiveScene().path;
            var demo = EditorSceneManager.OpenScene(DemoPath, OpenSceneMode.Additive);
            try
            {
                var pieces = Pieces(demo);
                foreach (var outfit in Cast) Save(Build(outfit, pieces, skin, eye), outfit.Id);
            }
            finally
            {
                EditorSceneManager.CloseScene(demo, true);
                if (!string.IsNullOrEmpty(previous) && SceneManager.GetActiveScene().path != previous)
                    EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Outfits] Saved {Cast.Length} characters to {OutDir}");
        }

        /// <summary>Garment pieces laid out in the demo's two wardrobe rows, by name.</summary>
        private static Dictionary<string, Transform> Pieces(Scene demo)
        {
            var map = new Dictionary<string, Transform>();
            foreach (var row in demo.GetRootGameObjects().Where(g => g.name == "male_cloth" || g.name == "Female_cloth"))
                foreach (Transform piece in row.transform)
                    if (!map.ContainsKey(piece.name)) map[piece.name] = piece;
            return map;
        }

        private static GameObject Build(Outfit outfit, Dictionary<string, Transform> pieces, Material skin, Material eye)
        {
            var root = new GameObject(outfit.Id);
            var model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);
            var headTop = outfit.Female ? FemaleHeadTop : MaleHeadTop;
            var s = outfit.Height / headTop;
            model.localScale = new Vector3(s * outfit.Girth, s, s * outfit.Girth);

            foreach (var name in outfit.Pieces)
            {
                if (!pieces.TryGetValue(name, out var src))
                {
                    Debug.LogWarning($"[Outfits] {outfit.Id}: no piece {name}");
                    continue;
                }
                var copy = Object.Instantiate(src.gameObject, model);
                copy.name = name;
                // Each piece sits at (row x, y, row z) around the shared body origin; keep only its own height offset.
                copy.transform.localPosition = new Vector3(0f, src.position.y, 0f);
                copy.transform.localRotation = src.rotation;
                copy.transform.localScale = src.lossyScale;
                foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            }

            // Simple head (the garments are made for an invisible body): an egg with two dark eyes.
            var headY = outfit.Female ? 1.69f : 1.72f;
            // Sized to sit inside the hats and hair pieces without poking through them.
            var head = Part(model, "Head", PrimitiveType.Sphere, new Vector3(0f, headY, 0f), new Vector3(0.155f, 0.22f, 0.18f), skin);
            Part(head.transform, "Eye L", PrimitiveType.Sphere, new Vector3(-0.2f, 0.05f, 0.47f), new Vector3(0.13f, 0.06f, 0.1f), eye);
            Part(head.transform, "Eye R", PrimitiveType.Sphere, new Vector3(0.2f, 0.05f, 0.47f), new Vector3(0.13f, 0.06f, 0.1f), eye);
            Part(model, "Neck", PrimitiveType.Cylinder, new Vector3(0f, headY - 0.13f, 0.0f), new Vector3(0.09f, 0.05f, 0.09f), skin);

            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.On;
            return root;
        }

        private static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static void Save(GameObject root, string id)
        {
            PrefabUtility.SaveAsPrefabAsset(root, $"{OutDir}/{id}.prefab");
            Object.DestroyImmediate(root);
        }

        private static Material EnsureMaterial(string path, Color color, float smoothness)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                ManorBlockoutBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(path)!.Replace('\\', '/'));
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
