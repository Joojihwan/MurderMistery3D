using UnityEngine;

namespace MurderMystery.Gameplay
{
    /// <summary>Player preferences set in the front end's settings and read by the game.</summary>
    public static class GameSettings
    {
        private const string NameKey = "settings.name";
        private const string MouseKey = "settings.mouse";
        private const string VolumeKey = "settings.volume";
        private const string VoiceKey = "settings.voice";
        private const string MusicVolumeKey = "settings.musicVolume";
        private const string MusicMutedKey = "settings.musicMuted";
        private const string MusicTalkKey = "settings.musicDuringTalk";

        public const float DefaultMouse = 0.12f;

        public static string PlayerName
        {
            get => PlayerPrefs.GetString(NameKey, "나그네");
            set => PlayerPrefs.SetString(NameKey, string.IsNullOrWhiteSpace(value) ? "나그네" : value.Trim());
        }

        public static float MouseSensitivity
        {
            get => PlayerPrefs.GetFloat(MouseKey, DefaultMouse);
            set => PlayerPrefs.SetFloat(MouseKey, Mathf.Clamp(value, 0.03f, 0.4f));
        }

        public static float Volume
        {
            get => PlayerPrefs.GetFloat(VolumeKey, 0.8f);
            set
            {
                PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value));
                AudioListener.volume = Mathf.Clamp01(value);
            }
        }

        public static bool VoiceEnabled
        {
            get => PlayerPrefs.GetInt(VoiceKey, 1) == 1;
            set => PlayerPrefs.SetInt(VoiceKey, value ? 1 : 0);
        }

        /// <summary>Background music level (on top of the overall volume).</summary>
        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(MusicVolumeKey, 0.45f);
            set => PlayerPrefs.SetFloat(MusicVolumeKey, Mathf.Clamp01(value));
        }

        /// <summary>Music switched off (M in the game, or the settings).</summary>
        public static bool MusicMuted
        {
            get => PlayerPrefs.GetInt(MusicMutedKey, 0) == 1;
            set => PlayerPrefs.SetInt(MusicMutedKey, value ? 1 : 0);
        }

        /// <summary>Music while people talk (discussions, the council, final statements, private rooms). Off by default.</summary>
        public static bool MusicDuringTalk
        {
            get => PlayerPrefs.GetInt(MusicTalkKey, 0) == 1;
            set => PlayerPrefs.SetInt(MusicTalkKey, value ? 1 : 0);
        }

        public static void Apply() => AudioListener.volume = Volume;

        public static void Save() => PlayerPrefs.Save();
    }
}
