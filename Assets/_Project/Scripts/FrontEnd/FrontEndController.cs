using System;
using System.Collections.Generic;
using MurderMystery.Gameplay;
using MurderMystery.Network;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace MurderMystery.FrontEnd
{
    /// <summary>
    /// The whole front end on one persistent UI Toolkit document (Title scene, kept across scene loads):
    /// title, main menu, scenario files, room / join / settings dialogs, loading screen, and — inside a map —
    /// the room overlay for picking characters. Built in code; styles in Resources/UI/FrontEnd.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public partial class FrontEndController : MonoBehaviour
    {
        public static FrontEndController Instance { get; private set; }

        private VisualElement root;
        private VisualElement backLayer;
        private VisualElement screenLayer;
        private VisualElement modalLayer;
        private VisualElement fade;
        private Label fadeTitle;
        private Label fadeTip;
        private Label toast;
        private FrontEndAudio sound;

        private static FontAsset sans;
        private static FontAsset serif;

        private bool InTitleScene => SceneManager.GetActiveScene().name == GameLauncher.TitleScene;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Coming back to the Title scene: the persistent front end (with its EventSystem) stays.
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            // Created here, not in the scene, so returning to the Title scene never makes a second one.
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(transform, false);
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
            GameSettings.Apply();
            sound = gameObject.AddComponent<FrontEndAudio>();
        }

        private void OnEnable()
        {
            if (Instance != this) return;
            Build();
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private void OnDisable() => SceneManager.activeSceneChanged -= OnActiveSceneChanged;

        private void Build()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();
            var sheet = Resources.Load<StyleSheet>("UI/FrontEnd");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.AddToClassList("root");
            ApplyFonts();

            backLayer = Add(root, "back-layer");
            backLayer.style.position = Position.Absolute;
            backLayer.style.left = backLayer.style.right = backLayer.style.top = backLayer.style.bottom = 0;
            BuildBackdrop(backLayer);

            screenLayer = Add(root, "screen-layer");
            screenLayer.style.position = Position.Absolute;
            screenLayer.style.left = screenLayer.style.right = screenLayer.style.top = screenLayer.style.bottom = 0;
            screenLayer.pickingMode = PickingMode.Ignore;
            BuildTitle();
            BuildMenu();
            BuildScenarios();

            BuildRoom();

            modalLayer = Add(root, "modal-layer");
            modalLayer.AddToClassList("off");

            fade = Add(root, "fade");
            fade.pickingMode = PickingMode.Ignore;
            fadeTitle = Serif(new Label());
            fadeTitle.AddToClassList("loading-title");
            fade.Add(fadeTitle);
            fadeTip = new Label();
            fadeTip.AddToClassList("loading-tip");
            fade.Add(fadeTip);

            toast = new Label();
            toast.AddToClassList("toast");
            toast.AddToClassList("toast--hidden");
            toast.pickingMode = PickingMode.Ignore;
            root.Add(toast);

            OnActiveSceneChanged(default, SceneManager.GetActiveScene());
            ShowScreen(titleScreen);
        }

        private void Update()
        {
            if (root == null) return;
            if (InTitleScene)
            {
                AnimateBackdrop();
                UpdateTitle();
                UpdateTypewriter();
            }
            UpdateRoom();
        }

        // ---------------------------------------------------------------- scenes

        private void OnActiveSceneChanged(Scene from, Scene to)
        {
            var title = to.name == GameLauncher.TitleScene;
            backLayer.EnableInClassList("off", !title);
            screenLayer.EnableInClassList("off", !title);
            sound.SetAmbience(title);
            if (title)
            {
                HideLoading();
                FirstPersonController.SetCursorLocked(false);
            }
            else
            {
                // Map loaded: let the room overlay or the offline HUD take over.
                schedule(HideLoading, 900);
            }
        }

        // ---------------------------------------------------------------- loading / toast

        private static readonly string[] Tips =
        {
            "범인만 거짓말을 할 수 있습니다. 나머지는 숨길 수는 있어도 거짓을 말하지 않습니다.",
            "밀담은 조사 시간에만 청할 수 있습니다. 둘만 아는 이야기가 판을 바꿉니다.",
            "도 검률은 묻는 사람이 이미 아는 것만 다시 말해 줍니다. 질문을 아끼세요.",
            "봉투는 열기 전까지 무엇이 들었는지 모릅니다. 언제 열지도 전략입니다.",
            "Tab으로 수첩을 펼치면 단서와 목표를 다시 볼 수 있습니다.",
        };

        public void ShowLoading(string title)
        {
            fadeTitle.text = title;
            fadeTip.text = Tips[UnityEngine.Random.Range(0, Tips.Length)];
            fade.pickingMode = PickingMode.Position;
            fade.AddToClassList("fade--on");
        }

        public void HideLoading()
        {
            fade.RemoveFromClassList("fade--on");
            fade.pickingMode = PickingMode.Ignore;
        }

        public void Toast(string text, float seconds = 3f)
        {
            toast.text = text;
            toast.RemoveFromClassList("toast--hidden");
            schedule(() => toast.AddToClassList("toast--hidden"), (long)(seconds * 1000));
        }

        // ---------------------------------------------------------------- helpers

        private void schedule(Action action, long delayMs) => root.schedule.Execute(action).StartingIn(delayMs);

        private static VisualElement Add(VisualElement parent, string cls)
        {
            var e = new VisualElement();
            if (!string.IsNullOrEmpty(cls)) e.AddToClassList(cls);
            parent.Add(e);
            return e;
        }

        private static Label AddLabel(VisualElement parent, string cls, string text)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            parent.Add(l);
            return l;
        }

        private Button AddButton(VisualElement parent, string text, Action onClick, params string[] classes)
        {
            var b = new Button(() =>
            {
                sound.Click();
                onClick();
            }) { text = text };
            b.AddToClassList("btn");
            foreach (var c in classes) b.AddToClassList(c);
            b.RegisterCallback<PointerEnterEvent>(_ => sound.Hover());
            parent.Add(b);
            return b;
        }

        private void ApplyFonts()
        {
            sans ??= TryFont("Malgun Gothic");
            serif ??= TryFont("Batang") ?? sans;
            if (sans != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(sans);
        }

        /// <summary>System font used at runtime (not shipped in the build).</summary>
        private static FontAsset TryFont(string family)
        {
            try
            {
                return FontAsset.CreateFontAsset(family, "Regular");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FrontEnd] font {family} unavailable: {e.Message}");
                return null;
            }
        }

        private static T Serif<T>(T element) where T : VisualElement
        {
            if (serif != null) element.style.unityFontDefinition = FontDefinition.FromSDFFont(serif);
            return element;
        }

        // Screen switching: off → visible, then the opacity transition runs.
        private readonly List<VisualElement> screens = new List<VisualElement>();

        private VisualElement NewScreen(string cls)
        {
            var s = Add(screenLayer, "screen");
            s.AddToClassList(cls);
            s.AddToClassList("screen--hidden");
            s.AddToClassList("off");
            screens.Add(s);
            return s;
        }

        private void ShowScreen(VisualElement screen)
        {
            foreach (var s in screens)
            {
                if (s == screen) continue;
                s.AddToClassList("screen--hidden");
                s.AddToClassList("off");
            }
            screen.RemoveFromClassList("off");
            schedule(() => screen.RemoveFromClassList("screen--hidden"), 30);
        }

        private static Texture2D Portrait(string id) => Resources.Load<Texture2D>($"UI/Portraits/{id}");
    }
}
