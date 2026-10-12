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
        private const float WalkSpeed = 1.3f;
        private const float SkirtFlare = 1.15f;   // m/s at which the walk clip plays at its authored pace

        [MenuItem("Tools/Murder Mystery/Characters/Build Rigged Characters (Mixamo)")]
        public static void BuildAll() => Debug.Log("[Rig] " + BuildAllReport());

        public static string BuildAllReport()
        {
            if (!AssetDatabase.IsValidFolder(MixamoDir)) return $"{MixamoDir} not found";
            var report = new List<string>();

            // Start from freshly built static outfits: they are the source of heights, materials and the
            // pieces put on bones, and re-running on already-rigged prefabs would compound those.
            CharacterOutfitBuilder.BuildAll();

            var idle = PrepareClip("Idle", report);
            var walk = PrepareClip("Walking", report);
            if (idle == null || walk == null) return string.Join("\n", report) + "\nneed Idle.fbx and Walking.fbx";
            // Optional: a floor-sitting pose and a jump (Sitting.fbx / Jump.fbx, Without Skin).
            var sit = File.Exists($"{MixamoDir}/Sitting.fbx") ? PrepareClip("Sitting", report) : null;
            var jump = File.Exists($"{MixamoDir}/Jump.fbx") ? PrepareClip("Jump", report, loop: false) : null;
            var controller = BuildController(idle, walk, sit, jump);
            walkClip = walk;
            idleClip = idle;

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
            report.Add(LevelHeads(idle));
            AssetDatabase.SaveAssets();
            return string.Join("\n", report);
        }

        /// <summary>
        /// Some rigs come back with the neck/head bones tilted (a long braid drags them back), so the head
        /// looks up in every pose. Compare each character's neck/head pitch in the idle pose with the cast's
        /// median and store a correction on its CharacterAnimator when it is off by more than a few degrees.
        /// </summary>
        private static string LevelHeads(AnimationClip idle)
        {
            var pitches = new Dictionary<string, (float neck, float head)>();
            foreach (var id in MixamoExport.Ids)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{OutfitDir}/{id}.prefab");
                if (prefab == null || prefab.GetComponent<CharacterAnimator>() == null) continue;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    var animator = instance.GetComponentInChildren<Animator>();
                    AnimationMode.StartAnimationMode();
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(animator.gameObject, idle, 0f);
                    AnimationMode.EndSampling();
                    pitches[id] = (Pitch(animator, HumanBodyBones.Neck, instance.transform), Pitch(animator, HumanBodyBones.Head, instance.transform));
                }
                finally
                {
                    AnimationMode.StopAnimationMode();
                    Object.DestroyImmediate(instance);
                }
            }
            if (pitches.Count < 3) return "heads: too few rigs to compare";

            float Median(IEnumerable<float> values)
            {
                var sorted = values.OrderBy(v => v).ToList();
                return sorted[sorted.Count / 2];
            }
            var neckMedian = Median(pitches.Values.Select(p => p.neck));
            var headMedian = Median(pitches.Values.Select(p => p.head));

            var fixedList = new List<string>();
            foreach (var pair in pitches)
            {
                var neckFix = neckMedian - pair.Value.neck;
                if (Mathf.Abs(neckFix) < 8f) neckFix = 0f;
                // Turning the neck turns the head with it; the head only needs what is left over.
                var headFix = headMedian - (pair.Value.head + neckFix);
                if (Mathf.Abs(headFix) < 4f) headFix = 0f;
                var path = $"{OutfitDir}/{pair.Key}.prefab";
                var contents = PrefabUtility.LoadPrefabContents(path);
                contents.GetComponent<CharacterAnimator>().SetPitchFix(neckFix, headFix);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
                if (neckFix != 0f || headFix != 0f) fixedList.Add($"{pair.Key} neck {neckFix:+0;-0}° head {headFix:+0;-0}°");
            }
            return "heads: " + (fixedList.Count > 0 ? string.Join(", ", fixedList) : "all level");
        }

        /// <summary>Bone 'up' pitch around the character's right axis (+ = leaning forward).</summary>
        private static float Pitch(Animator animator, HumanBodyBones bone, Transform character)
        {
            var t = animator.GetBoneTransform(bone);
            if (t == null) return 0f;
            var right = character.right;
            return Vector3.SignedAngle(Vector3.up, Vector3.ProjectOnPlane(t.up, right), right);
        }

        // ---------------------------------------------------------------- animation

        private static AnimationClip PrepareClip(string name, List<string> report, bool loop = true)
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
                c.loopTime = loop;
                // In-place locomotion: the body is moved by the character controller / network, not the clip.
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionXZ = true;
                // Height based on the feet, so the walk keeps them on the ground instead of bobbing the body up.
                c.keepOriginalPositionY = false;
                c.heightFromFeet = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(a => !a.name.StartsWith("__preview__"));
            report.Add($"{name}: {(clip != null ? clip.name + $" {clip.length:F2}s" : "no clip")}");
            return clip;
        }

        private static AnimatorController BuildController(AnimationClip idle, AnimationClip walk, AnimationClip sit, AnimationClip jump)
        {
            ManorBlockoutBuilder.EnsureFolder(Path.GetDirectoryName(ControllerPath)!.Replace('\\', '/'));
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            controller.layers[0].stateMachine.defaultState.iKOnFeet = true;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, WalkSpeed);
            // Faster than authored walking (running) just plays the walk faster.
            tree.AddChild(walk, WalkSpeed * 2.5f);
            var children = tree.children;
            children[2].timeScale = 2.2f;
            tree.children = children;

            var machine = controller.layers[0].stateMachine;
            var locomotion = machine.defaultState;
            if (sit != null)
            {
                controller.AddParameter("Sitting", AnimatorControllerParameterType.Bool);
                var sitState = machine.AddState("Sit");
                sitState.motion = sit;
                sitState.iKOnFeet = true;
                var down = locomotion.AddTransition(sitState);
                down.hasExitTime = false;
                down.duration = 0.35f;
                down.AddCondition(AnimatorConditionMode.If, 0f, "Sitting");
                var up = sitState.AddTransition(locomotion);
                up.hasExitTime = false;
                up.duration = 0.35f;
                up.AddCondition(AnimatorConditionMode.IfNot, 0f, "Sitting");
            }
            if (jump != null)
            {
                controller.AddParameter("Airborne", AnimatorControllerParameterType.Bool);
                var air = machine.AddState("Jump");
                air.motion = jump;
                var off = locomotion.AddTransition(air);
                off.hasExitTime = false;
                off.duration = 0.1f;
                off.AddCondition(AnimatorConditionMode.If, 0f, "Airborne");
                var land = air.AddTransition(locomotion);
                land.hasExitTime = false;
                land.duration = 0.15f;
                land.AddCondition(AnimatorConditionMode.IfNot, 0f, "Airborne");
            }
            return controller;
        }

        // ---------------------------------------------------------------- characters

        private static AnimationClip walkClip;
        private static AnimationClip idleClip;

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
            remapped = Mathf.Max(remapped, importer.GetExternalObjectMap().Count(kv => kv.Key.type == typeof(Material)));

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
            // Always animate: only six characters, and the neck fix and foot grounding need fresh poses.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Never trust the import scale: fit the rig to the outfit's measured height, feet on the ground.
            var h = Height(root);
            if (h > 0.01f) body.transform.localScale *= targetHeight / h;
            var minY = Bounds(root).min.y;
            body.transform.localPosition -= new Vector3(0f, minY, 0f);

            // The rig's ankles sit high inside the shoes, so feet placed at 0 in the bind pose float once the
            // clips play: lower the body by the lowest point the walk's planted foot actually reaches.
            // Feet are grounded at runtime by CharacterAnimator from the bind-pose sole height (the clips'
            // foot heights differ and editor sampling does not match the played result).
            var soleHeight = Mathf.Min(animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y,
                animator.GetBoneTransform(HumanBodyBones.RightFoot).position.y) - root.transform.position.y;

            var attached = AttachToBones(staticOutfit, animator) + AddHands(animator, targetHeight);
            var hidden = attached > 0 ? HideLegsUnderSkirt(body) : 0;

            root.AddComponent<CharacterAnimator>().SetSoleHeight(soleHeight);
            PrefabUtility.SaveAsPrefabAsset(root, outfitPath);
            Object.DestroyImmediate(root);
            return $"rigged, {remapped} materials restored, height {targetHeight:F2} m, sole {soleHeight * 100f:F0} cm" + (attached > 0 ? $", {attached} pieces on bones" : "") + (hidden > 0 ? $", {hidden} leg slots hidden under the skirt" : "");
        }

        /// <summary>
        /// Pieces left out of the Mixamo export are copied from the static outfit (same coordinates: both are
        /// fitted to the same height with feet at 0) onto a bone: skirts ride the hips, men's hair the head.
        /// </summary>
        private static int AttachToBones(GameObject staticOutfit, Animator animator)
        {
            if (staticOutfit == null) return 0;
            var source = (GameObject)PrefabUtility.InstantiatePrefab(staticOutfit);
            var count = 0;
            try
            {
                foreach (var mf in source.GetComponentsInChildren<MeshFilter>(true).ToList())
                {
                    HumanBodyBones bone;
                    if (CharacterOutfitBuilder.Skirts.Contains(mf.name)) bone = HumanBodyBones.Hips;
                    else if (mf.name == CharacterOutfitBuilder.HairCap || mf.name.StartsWith(CharacterOutfitBuilder.AttachHead)) bone = HumanBodyBones.Head;
                    else continue;
                    var target = animator.GetBoneTransform(bone);
                    if (target == null) continue;
                    var copy = Object.Instantiate(mf.gameObject, mf.transform.position, mf.transform.rotation);
                    copy.name = mf.name;
                    copy.transform.localScale = mf.transform.lossyScale;
                    // A walking stride reaches past a skirt made for standing legs; widen it a little.
                    if (bone == HumanBodyBones.Hips) copy.transform.localScale = Vector3.Scale(copy.transform.localScale, new Vector3(SkirtFlare, 1f, SkirtFlare));
                    copy.transform.SetParent(target, true);
                    count++;
                }
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
            return count;
        }

        /// <summary>
        /// The garments have no body, so the sleeves end in nothing: a simple mitten hand on each hand bone,
        /// pointing along the forearm, palm facing the body in the arms-down pose.
        /// </summary>
        private static int AddHands(Animator animator, float height)
        {
            var skin = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/Skin.mat");
            var s = height / 1.7f;
            var count = 0;
            foreach (var (handBone, armBone, side) in new[] { (HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm, -1f), (HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm, 1f) })
            {
                var hand = animator.GetBoneTransform(handBone);
                var arm = animator.GetBoneTransform(armBone);
                if (hand == null || arm == null) continue;
                var dir = (hand.position - arm.position).normalized;
                var forward = animator.transform.forward;
                var holder = new GameObject(side < 0f ? "Hand L" : "Hand R").transform;
                holder.SetPositionAndRotation(hand.position, Quaternion.LookRotation(dir, forward));
                HandPart(holder, "Palm", new Vector3(0f, 0f, 0.055f) * s, new Vector3(0.035f, 0.075f, 0.1f) * s, skin);
                HandPart(holder, "Fingers", new Vector3(0f, 0f, 0.115f) * s, new Vector3(0.028f, 0.068f, 0.07f) * s, skin);
                var thumb = HandPart(holder, "Thumb", new Vector3(-side * 0.018f, 0.03f, 0.05f) * s, new Vector3(0.022f, 0.022f, 0.06f) * s, skin);
                thumb.localRotation = Quaternion.Euler(-25f, 0f, 0f);
                holder.SetParent(hand, true);
                count++;
            }
            return count;
        }

        private static Transform HandPart(Transform holder, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(holder, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        // Legs and shoes of the skirt wearers: the walk's stride carries them through a skirt that only rides
        // the hips, and the floor-length skirt hides them anyway.
        private static readonly HashSet<string> UnderSkirt = new HashSet<string> { "MI_Underpants", "MI_Beoseon02a", "MI_Danghye", "MI_Clog" };
        private const string InvisiblePath = "Assets/_Project/Art/Materials/Invisible.mat";

        private static int HideLegsUnderSkirt(GameObject body)
        {
            var renderers = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (!renderers.Any(r => r.sharedMaterials.Any(m => m != null && m.name == "MI_Underpants"))) return 0;
            var invisible = InvisibleMaterial();
            var count = 0;
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !UnderSkirt.Contains(CleanName(mats[i].name))) continue;
                    mats[i] = invisible;
                    count++;
                }
                r.sharedMaterials = mats;
            }
            return count;
        }

        private static Material InvisibleMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(InvisiblePath);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Invisible" };
            mat.SetFloat("_Surface", 1f);   // transparent
            mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f));
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.SetInt("_ZWrite", 0);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.SetShaderPassEnabled("DepthOnly", false);
            AssetDatabase.CreateAsset(mat, InvisiblePath);
            return mat;
        }

        /// <summary>Lowest skinned vertex over a walk cycle, relative to the ground (positive = floating).</summary>
        private static float AnimatedFloorLift(GameObject body, AnimationClip clip)
        {
            if (clip == null) return 0f;
            var renderers = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var lowest = float.MaxValue;
            var baked = new Mesh();
            AnimationMode.StartAnimationMode();
            try
            {
                for (var i = 0; i < 12; i++)
                {
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(body, clip, clip.length * i / 12f);
                    AnimationMode.EndSampling();
                    foreach (var smr in renderers)
                    {
                        smr.BakeMesh(baked, true);
                        var m = smr.transform.localToWorldMatrix;
                        foreach (var v in baked.vertices) lowest = Mathf.Min(lowest, m.MultiplyPoint3x4(v).y);
                    }
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(baked);
            }
            return lowest == float.MaxValue ? 0f : lowest - body.transform.parent.position.y;
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
