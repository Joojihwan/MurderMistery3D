using System.IO;
using System.Linq;
using MurderMystery.FrontEnd;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace MurderMystery.EditorTools
{
    /// <summary>
    /// Builds the Title scene (front end + persistent NetworkManager) and renders the front end's pictures from
    /// the manor: title backdrops, the scenario cover and character portraits.
    /// </summary>
    public static class FrontEndBuilder
    {
        public const string TitleScenePath = "Assets/_Project/Scenes/Title.unity";
        private const string UiDir = "Assets/_Project/UI";
        private const string ThemePath = UiDir + "/RuntimeTheme.tss";
        private const string PanelPath = UiDir + "/FrontEndPanel.asset";
        private const string BackdropDir = "Assets/_Project/Resources/UI/Backdrops";
        private const string PortraitDir = "Assets/_Project/Resources/UI/Portraits";
        private const string CoverPath = "Assets/_Project/Resources/Scenarios/yun-manor/cover.png";

        [MenuItem("Tools/Murder Mystery/Front End/Build Title Scene")]
        public static void BuildTitleScene()
        {
            EnsurePanelSettings();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.04f, 0.035f);
            camGo.AddComponent<AudioListener>();

            NetworkSceneSetup.AddNetworkManager();

            var fe = new GameObject("FrontEnd");
            fe.AddComponent<UIDocument>();
            fe.AddComponent<FrontEndController>();

            EditorSceneManager.SaveScene(scene, TitleScenePath);

            // The panel reference does not persist on a brand-new scene: reopen it and set it then.
            scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
            var so = new SerializedObject(GameObject.Find("FrontEnd").GetComponent<UIDocument>());
            so.FindProperty("m_PanelSettings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            SetBuildScenes();
            Debug.Log($"[FrontEndBuilder] Saved {TitleScenePath}");
        }

        /// <summary>Title first; then the maps Netcode may load.</summary>
        public static void SetBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(TitleScenePath, true),
                new EditorBuildSettingsScene(KhsManorBuilder.ScenePath, true),
                new EditorBuildSettingsScene("Assets/_Project/Scenes/Manor_Blockout.unity", true),
            };
        }

        private static PanelSettings EnsurePanelSettings()
        {
            ManorBlockoutBuilder.EnsureFolder(UiDir);
            if (!File.Exists(ThemePath))
            {
                File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemePath);
            }
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelPath);
            }
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.sortingOrder = 10;
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();
            // Reload so the scene stores a reference to the saved asset, not the in-memory instance.
            return AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
        }

        // ---------------------------------------------------------------- pictures

        private struct Shot
        {
            public string Name;
            public Vector3 Pos, Target;
            public float Fov;
            public bool Dusk;
        }

        private static readonly Shot[] Backdrops =
        {
            new Shot { Name = "01_sarangchae_dusk", Pos = new Vector3(17f, 1.7f, -21.5f), Target = new Vector3(9f, 2.4f, -11f), Fov = 55f, Dusk = true },
            new Shot { Name = "02_aerial_dusk", Pos = new Vector3(30f, 20f, -44f), Target = new Vector3(2f, 0f, -4f), Fov = 45f, Dusk = true },
            new Shot { Name = "03_anchae_morning", Pos = new Vector3(-5f, 1.6f, -1.5f), Target = new Vector3(-12f, 1.8f, 6f), Fov = 60f },
            new Shot { Name = "04_gate_morning", Pos = new Vector3(4f, 1.6f, -16f), Target = new Vector3(0f, 2f, -27f), Fov = 60f },
        };

        [MenuItem("Tools/Murder Mystery/Front End/Render Pictures")]
        public static void RenderPictures() => Debug.Log("[FrontEndBuilder] " + RenderPicturesReport());

        public static string RenderPicturesReport()
        {
            EditorSceneManager.OpenScene(KhsManorBuilder.ScenePath, OpenSceneMode.Single);
            ManorBlockoutBuilder.EnsureFolder(BackdropDir);
            ManorBlockoutBuilder.EnsureFolder(PortraitDir);
            ShaderUtil.allowAsyncCompilation = false;
            var count = 0;
            try
            {
                var light = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
                // The NPC stands in the bind pose outside Play; keep him out of the stills.
                var npc = GameObject.Find("NPC (도 검률)");
                if (npc != null) npc.SetActive(false);
                foreach (var shot in Backdrops)
                {
                    using (new Mood(light, shot.Dusk))
                        Render(shot.Pos, shot.Target, shot.Fov, 1920, 1080, $"{BackdropDir}/{shot.Name}.png", shot.Dusk);
                    count++;
                }
                using (new Mood(light, false))
                    Render(new Vector3(11.4f, 1.45f, -19.8f), new Vector3(12.3f, 1.75f, -13.3f), 42f, 1200, 800, CoverPath, false);
                count++;
                count += RenderPortraits();
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = true;
            }
            AssetDatabase.Refresh();
            return $"{count} pictures";
        }

        private static int RenderPortraits()
        {
            var count = 0;
            foreach (var prefab in Resources.LoadAll<GameObject>("Characters"))
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.SetPositionAndRotation(new Vector3(0f, -200f, 0f), Quaternion.identity);
                var animator = go.GetComponentInChildren<Animator>();
                Transform head = null;
                AnimationMode.StartAnimationMode();
                try
                {
                    if (animator != null && animator.runtimeAnimatorController != null)
                    {
                        var idle = animator.runtimeAnimatorController.animationClips.FirstOrDefault();
                        if (idle != null)
                        {
                            AnimationMode.BeginSampling();
                            AnimationMode.SampleAnimationClip(animator.gameObject, idle, 0.5f);
                            AnimationMode.EndSampling();
                        }
                        head = animator.GetBoneTransform(HumanBodyBones.Head);
                    }
                    var headPos = head != null ? head.position : go.transform.position + Vector3.up * 1.6f;
                    var face = headPos + Vector3.up * 0.02f;
                    Render(face + new Vector3(0.3f, 0.02f, 1.7f), face - new Vector3(0f, 0.18f, 0f), 32f, 512, 640,
                        $"{PortraitDir}/{prefab.name}.png", false, new Color(0.84f, 0.78f, 0.66f));
                    count++;
                }
                finally
                {
                    AnimationMode.StopAnimationMode();
                    Object.DestroyImmediate(go);
                }
            }
            return count;
        }

        private static void Render(Vector3 pos, Vector3 target, float fov, int w, int h, string path, bool dusk, Color? clear = null)
        {
            var camGo = new GameObject("PictureCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // Sky left transparent and painted afterwards (a skybox tints oddly at a low sun in URP).
            cam.backgroundColor = clear ?? new Color(0f, 0f, 0f, 0f);
            camGo.transform.position = pos;
            camGo.transform.LookAt(target);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = null;
            cam.targetTexture = null;
            if (!clear.HasValue) PaintSky(tex, dusk);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }

        private static void PaintSky(Texture2D tex, bool dusk)
        {
            Color top = dusk ? new Color(0.12f, 0.11f, 0.2f) : new Color(0.56f, 0.62f, 0.7f);
            Color mid = dusk ? new Color(0.46f, 0.26f, 0.3f) : new Color(0.74f, 0.77f, 0.8f);
            Color low = dusk ? new Color(0.96f, 0.58f, 0.34f) : new Color(0.88f, 0.88f, 0.86f);
            var px = tex.GetPixels();
            var w = tex.width;
            var h = tex.height;
            for (var y = 0; y < h; y++)
            {
                var t = y / (float)(h - 1);   // 0 bottom, 1 top
                var sky = t > 0.55f ? Color.Lerp(mid, top, (t - 0.55f) / 0.45f) : Color.Lerp(low, mid, Mathf.Clamp01((t - 0.25f) / 0.3f));
                for (var x = 0; x < w; x++)
                {
                    var c = px[y * w + x];
                    var a = c.a;
                    px[y * w + x] = new Color(c.r * a + sky.r * (1f - a), c.g * a + sky.g * (1f - a), c.b * a + sky.b * (1f - a), 1f);
                }
            }
            tex.SetPixels(px);
        }

        /// <summary>Temporary dusk lighting for the title pictures (the scene itself is not saved).</summary>
        private sealed class Mood : System.IDisposable
        {
            private readonly Light light;
            private readonly Quaternion rot;
            private readonly Color color;
            private readonly float intensity;
            private readonly Color sky, equator, ground, fog;
            private readonly bool fogOn;

            public Mood(Light light, bool dusk)
            {
                this.light = light;
                sky = RenderSettings.ambientSkyColor;
                equator = RenderSettings.ambientEquatorColor;
                ground = RenderSettings.ambientGroundColor;
                fog = RenderSettings.fogColor;
                fogOn = RenderSettings.fog;
                if (light != null)
                {
                    rot = light.transform.rotation;
                    color = light.color;
                    intensity = light.intensity;
                }
                RenderSettings.fog = false;
                if (!dusk || light == null) return;
                light.transform.rotation = Quaternion.Euler(9f, -125f, 0f);
                light.color = new Color(1f, 0.62f, 0.38f);
                light.intensity = 1.5f;
                RenderSettings.ambientSkyColor = new Color(0.42f, 0.32f, 0.36f);
                RenderSettings.ambientEquatorColor = new Color(0.36f, 0.26f, 0.22f);
                RenderSettings.ambientGroundColor = new Color(0.16f, 0.12f, 0.1f);
            }

            public void Dispose()
            {
                if (light != null)
                {
                    light.transform.rotation = rot;
                    light.color = color;
                    light.intensity = intensity;
                }
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientEquatorColor = equator;
                RenderSettings.ambientGroundColor = ground;
                RenderSettings.fogColor = fog;
                RenderSettings.fog = fogOn;
            }
        }
    }
}
