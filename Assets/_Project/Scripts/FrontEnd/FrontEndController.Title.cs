using System.Collections.Generic;
using MurderMystery.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MurderMystery.FrontEnd
{
    public partial class FrontEndController
    {
        public const string WorkingTitle = "조선 추리극";

        private VisualElement titleScreen;
        private VisualElement menuScreen;
        private Label titlePrompt;
        private Label namePlate;
        private float promptTimer;

        // Backdrop: two layers crossfading through the manor stills, slowly drifting (Ken Burns), with mist and
        // embers over them.
        private VisualElement[] bgLayers;
        private Texture2D[] backdrops;
        private int bgIndex;
        private float bgTime;
        private const float BgPeriod = 14f;
        private VisualElement[] mists;
        private readonly List<(VisualElement el, Vector2 pos, float speed, float phase)> embers =
            new List<(VisualElement, Vector2, float, float)>();

        private void BuildBackdrop(VisualElement parent)
        {
            backdrops = Resources.LoadAll<Texture2D>("UI/Backdrops");
            bgLayers = new VisualElement[2];
            for (var i = 0; i < 2; i++)
            {
                bgLayers[i] = Add(parent, "backdrop");
                bgLayers[i].style.backgroundColor = new Color(0.12f, 0.1f, 0.09f);
            }
            if (backdrops.Length > 0) bgLayers[0].style.backgroundImage = new StyleBackground(backdrops[0]);
            bgLayers[1].AddToClassList("backdrop--hidden");

            Add(parent, "tint");
            var mistTex = MistTexture();
            mists = new VisualElement[2];
            for (var i = 0; i < 2; i++)
            {
                mists[i] = Add(parent, "mist");
                mists[i].style.backgroundImage = new StyleBackground(mistTex);
                mists[i].style.opacity = i == 0 ? 0.35f : 0.22f;
            }
            Add(parent, "vignette").style.backgroundImage = new StyleBackground(VignetteTexture());

            var dot = EmberTexture();
            var rng = new System.Random(3);
            for (var i = 0; i < 28; i++)
            {
                var e = Add(parent, "ember");
                e.style.backgroundImage = new StyleBackground(dot);
                e.pickingMode = PickingMode.Ignore;
                var size = 3f + (float)rng.NextDouble() * 5f;
                e.style.width = e.style.height = size;
                embers.Add((e, new Vector2((float)rng.NextDouble(), (float)rng.NextDouble()),
                    0.01f + (float)rng.NextDouble() * 0.025f, (float)rng.NextDouble() * 10f));
            }
        }

        private void AnimateBackdrop()
        {
            bgTime += Time.unscaledDeltaTime;
            var front = bgLayers[bgIndex % 2];
            var k = bgTime / BgPeriod;
            front.style.scale = new Scale(Vector3.one * (1.0f + 0.07f * k));
            front.style.translate = new Translate(Length.Percent(-1.5f * k), Length.Percent(-0.8f * k));
            if (bgTime > BgPeriod && backdrops.Length > 1)
            {
                bgTime = 0f;
                bgIndex++;
                var next = bgLayers[bgIndex % 2];
                next.style.backgroundImage = new StyleBackground(backdrops[bgIndex % backdrops.Length]);
                next.style.scale = new Scale(Vector3.one);
                next.style.translate = new Translate(0, 0);
                next.RemoveFromClassList("backdrop--hidden");
                front.AddToClassList("backdrop--hidden");
            }

            var t = Time.unscaledTime;
            if (mists != null)
            {
                mists[0].style.translate = new Translate(Length.Percent(Mathf.Repeat(t * 0.6f, 25f) - 12.5f), 0);
                mists[1].style.translate = new Translate(Length.Percent(12.5f - Mathf.Repeat(t * 0.35f, 25f)), Length.Percent(4f));
            }

            var w = root.resolvedStyle.width;
            var h = root.resolvedStyle.height;
            if (float.IsNaN(w) || w <= 0f) return;
            for (var i = 0; i < embers.Count; i++)
            {
                var (el, pos, speed, phase) = embers[i];
                var y = Mathf.Repeat(pos.y - t * speed, 1.1f);
                var x = pos.x + Mathf.Sin(t * 0.4f + phase) * 0.02f;
                el.style.translate = new Translate(x * w, y * h);
                el.style.opacity = Mathf.Clamp01(Mathf.Sin(y * Mathf.PI)) * (0.35f + 0.35f * Mathf.Sin(t * 2f + phase));
            }
        }

        // ---------------------------------------------------------------- title

        private void BuildTitle()
        {
            titleScreen = NewScreen("title-screen");
            var seal = Add(titleScreen, "title-seal");
            seal.Add(Serif(new Label("事件") { name = "seal" }));
            seal.Q<Label>("seal").AddToClassList("title-seal__text");
            Serif(AddLabel(titleScreen, "title-main", WorkingTitle));
            Serif(AddLabel(titleScreen, "title-sub", "한 집, 다섯 사람, 하룻밤의 진실"));
            AddLabel(titleScreen, "title-working", "(가제)");
            titlePrompt = AddLabel(titleScreen, "title-prompt", "아무 키나 눌러 시작");
        }

        private void UpdateTitle()
        {
            promptTimer += Time.unscaledDeltaTime;
            if (promptTimer > 1.2f)
            {
                promptTimer = 0f;
                titlePrompt.ToggleInClassList("title-prompt--dim");
            }
            if (titleScreen.ClassListContains("off") || modalLayer.ClassListContains("off") == false) return;
            var key = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
            var click = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            if (key || click)
            {
                sound.Stamp();
                ShowScreen(menuScreen);
            }
        }

        // ---------------------------------------------------------------- main menu

        private void BuildMenu()
        {
            menuScreen = NewScreen("menu-screen");
            var left = Add(menuScreen, "menu-left");
            var top = new VisualElement();
            left.Add(top);
            Serif(AddLabel(top, "menu-logo", WorkingTitle));
            AddLabel(top, "menu-logo-sub", "조선 추리극 — 함께 푸는 하룻밤의 사건");

            MenuItem(top, "사건 고르기", "사건첩을 펼칩니다", () =>
            {
                RefreshScenarios();
                ShowScreen(scenariosScreen);
            });
            MenuItem(top, "코드로 참가", "친구가 연 방에 들어갑니다", OpenJoinDialog);
            MenuItem(top, "설정", "이름 · 마우스 · 소리 · 음성", OpenSettingsDialog);
            MenuItem(top, "나가기", "", () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });

            AddLabel(left, "menu-footer", "3D 자원: 국가유산청 · 개발 빌드");

            var right = Add(menuScreen, "menu-right");
            var plate = Add(right, "name-plate");
            AddLabel(plate, "name-plate__label", "이름");
            namePlate = AddLabel(plate, "name-plate__name", GameSettings.PlayerName);
            plate.RegisterCallback<ClickEvent>(_ => OpenSettingsDialog());
        }

        private void MenuItem(VisualElement parent, string label, string hint, System.Action onClick)
        {
            var item = Add(parent, "menu-item");
            Add(item, "menu-item__mark");
            Serif(AddLabel(item, "menu-item__label", label));
            AddLabel(item, "menu-item__hint", hint);
            item.RegisterCallback<PointerEnterEvent>(_ => sound.Hover());
            item.RegisterCallback<ClickEvent>(_ =>
            {
                sound.Click();
                onClick();
            });
        }

        // ---------------------------------------------------------------- procedural textures

        private static Texture2D MistTexture()
        {
            const int w = 512, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                // Tileable in x: sample noise on a circle.
                var a = x / (float)w * Mathf.PI * 2f;
                var n = Mathf.PerlinNoise(Mathf.Cos(a) * 1.6f + 10f, Mathf.Sin(a) * 1.6f + y / 70f) * 0.7f +
                        Mathf.PerlinNoise(Mathf.Cos(a) * 4f + 30f, Mathf.Sin(a) * 4f + y / 25f) * 0.3f;
                var band = Mathf.Clamp01(1f - Mathf.Abs(y / (float)h - 0.62f) * 2.2f);
                var alpha = Mathf.Clamp01((n - 0.42f) * 2.4f) * band;
                px[y * w + x] = new Color32(235, 228, 214, (byte)(alpha * 200));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        private static Texture2D VignetteTexture()
        {
            const int s = 256;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            for (var y = 0; y < s; y++)
            for (var x = 0; x < s; x++)
            {
                var dx = (x / (float)s - 0.5f) * 2f;
                var dy = (y / (float)s - 0.5f) * 2f;
                var d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy);
                var a = Mathf.Clamp01((d - 0.55f) * 1.4f);
                px[y * s + x] = new Color32(6, 5, 4, (byte)(a * 235));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        private static Texture2D EmberTexture()
        {
            const int s = 32;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var px = new Color32[s * s];
            for (var y = 0; y < s; y++)
            for (var x = 0; x < s; x++)
            {
                var d = Vector2.Distance(new Vector2(x, y), new Vector2(s / 2f, s / 2f)) / (s / 2f);
                var a = Mathf.Clamp01(1f - d);
                px[y * s + x] = new Color32(255, 196, 120, (byte)(a * a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
