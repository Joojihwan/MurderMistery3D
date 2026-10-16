using System.Collections.Generic;
using MurderMystery.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MurderMystery.Gameplay
{
    /// <summary>
    /// Background music for the whole game, one cue per stage: the title and lobby, reading the scenario, the
    /// autopsy report, the two searches, the vote and the ending. While people talk (discussions, the council,
    /// final statements, a private room) the music is off unless the player turns it on; M switches it on or
    /// off for whatever is going on at the moment. Tracks live in Resources/Audio/Music (sources and licence in docs/audio_credits.md)
    /// and crossfade when the cue changes; a cue with several tracks plays them in turn.
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        public enum Cue { None, Title, Reading, Autopsy, Search, Search2, Talk, Vote, Ending }

        private static readonly Dictionary<Cue, string[]> Tracks = new Dictionary<Cue, string[]>
        {
            [Cue.Title] = new[] { "title_grace_in_hanbok" },
            [Cue.Reading] = new[] { "reading_echoes_of_hanbok" },
            [Cue.Autopsy] = new[] { "autopsy_autumn_evenings" },
            [Cue.Search] = new[] { "search_gayageum_cinematic", "search_drama_mystery" },
            [Cue.Search2] = new[] { "search2_mystery_time_loop", "search_drama_mystery" },
            [Cue.Vote] = new[] { "vote_drama_tension" },
            [Cue.Ending] = new[] { "ending_ethereal_rhythms" },
        };

        private const string TitleScene = "Title";   // GameLauncher.TitleScene (Network assembly)
        private const float FadeSeconds = 2f;
        private const float ToastSeconds = 2.5f;

        public static MusicPlayer Instance { get; private set; }

        /// <summary>The stage the music follows now.</summary>
        public Cue Current { get; private set; }

        /// <summary>True when the current stage is one where people talk (music off by default).</summary>
        public bool TalkTime => Current == Cue.Talk;

        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private AudioSource[] sources;
        private int active;
        private int trackIndex;
        private string toast;
        private float toastUntil;
        private GUIStyle toastStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Instance != null || Application.isBatchMode) return;
            var go = new GameObject(nameof(MusicPlayer));
            DontDestroyOnLoad(go);
            go.AddComponent<MusicPlayer>();
        }

        private void Awake()
        {
            Instance = this;
            sources = new AudioSource[2];
            for (var i = 0; i < 2; i++)
            {
                sources[i] = gameObject.AddComponent<AudioSource>();
                sources[i].playOnAwake = false;
                sources[i].loop = false;
                sources[i].spatialBlend = 0f;
                sources[i].volume = 0f;
            }
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            // M works in the game only (the front end has text fields; its settings carry the same switches).
            if (keyboard != null && keyboard.mKey.wasPressedThisFrame && GameClient.Current != null && !TypingInField()) ToggleNow();

            var cue = CueNow();
            if (cue != Current)
            {
                Current = cue;
                trackIndex = 0;
                StartTrack();
            }
            else if (Audible && !sources[active].isPlaying)
            {
                // Track finished: next one in the cue (or the same one again).
                trackIndex++;
                StartTrack();
            }

            // Crossfade: the active source rises to the wanted volume, the other falls silent.
            var target = Audible ? GameSettings.MusicVolume : 0f;
            var step = Time.unscaledDeltaTime / FadeSeconds;
            sources[active].volume = Mathf.MoveTowards(sources[active].volume, target, step);
            var other = sources[1 - active];
            other.volume = Mathf.MoveTowards(other.volume, 0f, step);
            if (other.isPlaying && other.volume <= 0f) other.Stop();
            if (!Audible && sources[active].isPlaying && sources[active].volume <= 0f) sources[active].Stop();
        }

        /// <summary>Whether music plays at this moment (the cue has tracks and the player has it on).</summary>
        public bool Audible
        {
            get
            {
                if (!Tracks.ContainsKey(Current)) return GameSettings.MusicDuringTalk && TalkTime && LastTracks != null;
                return !GameSettings.MusicMuted;
            }
        }

        // During talk the last stage's tracks keep playing (quietly) when the player wants music then.
        private string[] LastTracks => lastMusicCue != Cue.None ? Tracks[lastMusicCue] : null;
        private Cue lastMusicCue = Cue.None;

        /// <summary>M: music on or off for what is going on now (talk time has its own switch).</summary>
        public void ToggleNow()
        {
            if (TalkTime)
            {
                GameSettings.MusicDuringTalk = !GameSettings.MusicDuringTalk;
                ShowToast(GameSettings.MusicDuringTalk ? "대화 중 음악 켬 (M)" : "대화 중 음악 끔 (M)");
            }
            else
            {
                GameSettings.MusicMuted = !GameSettings.MusicMuted;
                ShowToast(GameSettings.MusicMuted ? "음악 끔 (M)" : "음악 켬 (M)");
            }
            GameSettings.Save();
            if (Audible && !sources[active].isPlaying) StartTrack();
        }

        private void StartTrack()
        {
            string[] names;
            if (Tracks.TryGetValue(Current, out var own))
            {
                names = own;
                lastMusicCue = Current;
            }
            else if (TalkTime && LastTracks != null)
            {
                names = LastTracks;
            }
            else
            {
                return;   // nothing to play; the fade takes the old track out
            }

            var clip = Load(names[trackIndex % names.Length]);
            if (clip == null) return;
            if (sources[active].clip == clip && sources[active].isPlaying) return;
            active = 1 - active;
            sources[active].clip = clip;
            sources[active].volume = 0f;
            sources[active].Play();
        }

        private AudioClip Load(string name)
        {
            if (!clips.TryGetValue(name, out var clip))
            {
                clip = Resources.Load<AudioClip>("Audio/Music/" + name);
                if (clip == null) Debug.LogWarning($"Music track missing: Audio/Music/{name}");
                clips[name] = clip;
            }
            return clip;
        }

        /// <summary>The stage the music should follow: no game yet means the title and lobby.</summary>
        private static Cue CueNow()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == TitleScene) return Cue.Title;
            var snapshot = GameClient.Current?.Snapshot;
            if (snapshot == null || string.IsNullOrEmpty(snapshot.phase)) return Cue.Title;
            var phase = snapshot.Phase;
            // Someone in a small private room is talking, whatever the stage.
            if (!string.IsNullOrEmpty(snapshot.room) && phase != GamePhase.Council1 && phase != GamePhase.Council2) return Cue.Talk;
            switch (phase)
            {
                case GamePhase.CharacterSelect:
                case GamePhase.Reading:
                case GamePhase.Introductions:
                    return Cue.Reading;
                case GamePhase.AutopsyBriefing:
                    return Cue.Autopsy;
                case GamePhase.Investigation1:
                    return Cue.Search;
                case GamePhase.Investigation2:
                    return Cue.Search2;
                case GamePhase.Council1:
                case GamePhase.Council2:
                case GamePhase.Discussion1:
                case GamePhase.Discussion2:
                case GamePhase.FinalStatements:
                    return Cue.Talk;
                case GamePhase.Vote:
                    return Cue.Vote;
                case GamePhase.Ending:
                    return Cue.Ending;
                default:
                    return Cue.Reading;
            }
        }

        private static bool TypingInField() => GUIUtility.keyboardControl != 0;

        private void ShowToast(string text)
        {
            toast = text;
            toastUntil = Time.unscaledTime + ToastSeconds;
        }

        private void OnGUI()
        {
            if (toast == null || Time.unscaledTime > toastUntil) return;
            toastStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect(Screen.width / 2f - 130f, 60f, 260f, 34f), toast, toastStyle);
        }
    }
}
