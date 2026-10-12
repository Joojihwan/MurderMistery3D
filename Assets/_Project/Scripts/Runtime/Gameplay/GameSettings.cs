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

        public static void Apply() => AudioListener.volume = Volume;

        public static void Save() => PlayerPrefs.Save();
    }
}
