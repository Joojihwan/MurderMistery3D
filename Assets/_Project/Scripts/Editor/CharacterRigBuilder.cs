using System.Collections.Generic;
using System.IO;
using System.Linq;
using MurderMystery.Gameplay;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MurderMystery.EditorTools
{
    /// <summary>
    /// Turns the Mixamo round trip into walking characters (docs/design/asset_plan.md §3):
    /// rigged outfits &lt;id&gt;.fbx plus Idle.fbx / Walking.fbx (Without Skin, In Place) dropped into
    /// Assets/ThirdParty/KHS/Mixamo become Humanoid models with their KHS garment materials restored,
    /// scaled to each character's height, and saved over Resources/Characters/&lt;id&gt; with an idle/walk
    /// blend driven by <see cref="CharacterAnimator"/>. Characters without a rigged FBX keep the static outfit.
    /// </summary>
    public static class CharacterRigBuilder
    {
        public const string MixamoDir = KhsAssetTools.Root + "/Mixamo";
        private const string OutfitDir = "Assets/_Project/Resources/Characters";
        private const string ControllerPath = "Assets/_Project/Art/Characters/Locomotion.controller";
        private const float WalkSpeed = 1.3f;   // m/s at which the walk clip plays at its authored pace

        [MenuItem("Tools/Murder Mystery/Characters/Build Rigged Characters (Mixamo)")]
        public static void BuildAll() => Debug.Log("[Rig] " + BuildAllReport());

        public static string BuildAllReport()
        {
            if (!AssetDatabase.IsValidFolder(MixamoDir)) return $"{MixamoDir} not found";
            var report = new List<string>();

            var idle = PrepareClip("Idle", report);
            var walk = PrepareClip("Walking", report);
            if (idle == null || walk == null) return string.Join("\n", report) + "\nneed Idle.fbx and Walking.fbx";
            var controller = BuildController(idle, walk);

            foreach (var id in MixamoExport.Ids)
            {
                var fbx = $"{MixamoDir}/{id}.fbx";
                if (!File.Exists(fbx))
                {
                    report.Add($"{id}: no rigged FBX, keeps the static outfit");
                    continue;
                }
                report.Add($"{id}: {BuildCharacter(id, fbx, controller)}");
            }
            AssetDatabase.SaveAssets();
            return string.Join("\n", report);
        }

        // ---------------------------------------------------------------- animation

        private static AnimationClip PrepareClip(string name, List<string> report)
        {
            var path = $"{MixamoDir}/{name}.fbx";
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
            {
                report.Add($"{name}.fbx missing");
                return null;
            }
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = true;
                // In-place locomotion: the body is moved by the character controller / network, not the clip.
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(a => !a.name.StartsWith("__preview__"));
            report.Add($"{name}: {(clip != null ? clip.name + $" {clip.length:F2}s" : "no clip")}");
            return clip;
        }

        private static AnimatorController BuildController(AnimationClip idle, AnimationClip walk)
        {
            ManorBlockoutBuilder.EnsureFolder(Path.GetDirectoryName(ControllerPath)!.Replace('\\', '/'));
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, WalkSpeed);
            // Faster than authored walking (running) just plays the walk faster.
            tree.AddChild(walk, WalkSpeed * 2.5f);
            var children = tree.children;
            children[2].timeScale = 2.2f;
            tree.children = children;
            return controller;
        }

        // ---------------------------------------------------------------- characters

        private static string BuildCharacter(string id, string fbx, AnimatorController controller)
        {
            var outfitPath = $"{OutfitDir}/{id}.prefab";
            var staticOutfit = AssetDatabase.LoadAssetAtPath<GameObject>(outfitPath);
            var targetHeight = staticOutfit != null ? MeasuredHeight(staticOutfit) : 1.65f;
            var garmentMaterials = staticOutfit != null
                ? staticOutfit.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null)
                    .GroupBy(m => m.name).ToDictionary(g => g.Key, g => g.First())
                : new Dictionary<string, Material>();

            // Humanoid, and the FBX's materials (named after the garments on export) mapped back to the KHS ones.
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbx);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.SaveAndReimport();
            var remapped = 0;
            foreach (var embedded in AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Material>().ToList())
            {
                var key = CleanName(embedded.name);
                if (!garmentMaterials.TryGetValue(key, out var original)) continue;
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), original);
                remapped++;
            }
            importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isHuman) return "avatar is not humanoid (re-rig in Mixamo?)";

            var root = new GameObject(id);
            var body = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            body.name = "Model";
            if (!body.TryGetComponent<Animator>(out var animator)) animator = body.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // Never trust the import scale: fit the rig to the outfit's measured height, feet on the ground.
            var h = Height(root);
            if (h > 0.01f) body.transform.localScale *= targetHeight / h;
            var minY = Bounds(root).min.y;
            body.transform.localPosition -= new Vector3(0f, minY, 0f);

            var skirts = AttachSkirts(staticOutfit, root, animator);

            root.AddComponent<CharacterAnimator>();
            PrefabUtility.SaveAsPrefabAsset(root, outfitPath);
            Object.DestroyImmediate(root);
            return $"rigged, {remapped} materials restored, height {targetHeight:F2} m" + (skirts > 0 ? $", {skirts} skirt on the hips" : "");
        }

        /// <summary>
        /// Skirts were left out of the Mixamo export; copy them from the static outfit (same coordinates: both
        /// are fitted to the same height with feet at 0) and let them ride on the hips bone.
        /// </summary>
        private static int AttachSkirts(GameObject staticOutfit, GameObject root, Animator animator)
        {
            if (staticOutfit == null) return 0;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) return 0;
            var source = (GameObject)PrefabUtility.InstantiatePrefab(staticOutfit);
            var count = 0;
            try
            {
                foreach (var mf in source.GetComponentsInChildren<MeshFilter>(true).Where(m => CharacterOutfitBuilder.Skirts.Contains(m.name)).ToList())
                {
                    var copy = Object.Instantiate(mf.gameObject, mf.transform.position, mf.transform.rotation);
                    copy.name = mf.name;
                    copy.transform.localScale = mf.transform.lossyScale;
                    copy.transform.SetParent(hips, true);
                    count++;
                }
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
            return count;
        }

        private static string CleanName(string name)
        {
            // Mixamo / FBX round trips can append ".001" style suffixes.
            var dot = name.IndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        private static float Height(GameObject go) => Bounds(go).size.y;

        /// <summary>Renderer bounds of a prefab asset are only valid on an instance.</summary>
        private static float MeasuredHeight(GameObject prefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                return Height(instance);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static Bounds Bounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
