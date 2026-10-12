using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MurderMystery.Core
{
    /// <summary>Spoiler-free public face of one character, shown on scenario and seat cards.</summary>
    [Serializable]
    public class CastCard
    {
        public string id;
        public string name;
        public int age;
        public string status;    // 신분
        public string blurb;     // what everyone knows
    }

    /// <summary>
    /// One scenario's manifest (Resources/Scenarios/&lt;id&gt;/scenario.json). Everything here is public:
    /// it is shown before anyone has read a scenario book (docs/design/scenario_platform_proposal.md §4).
    /// </summary>
    [Serializable]
    public class ScenarioManifest
    {
        public string id;
        public string title;
        public string subtitle;
        public string tagline;
        public string synopsis;
        public int players = 5;
        public int durationMin = 100;
        public int difficulty = 2;           // 1..3
        public string[] tags = Array.Empty<string>();
        public string mapScene;
        public string dataPath = "Data";     // Resources folder with the rule data
        public string cover;                 // Resources path of the cover image
        public string version = "1";
        public string credit;
        public bool locked;                  // shown sealed ("준비 중")
        public string status;
        public CastCard[] cast = Array.Empty<CastCard>();
    }

    public static class ScenarioCatalog
    {
        private static List<ScenarioManifest> all;

        /// <summary>Every scenario in the build, playable ones first.</summary>
        public static IReadOnlyList<ScenarioManifest> All => all ??= Load();

        public static ScenarioManifest Find(string id) => All.FirstOrDefault(s => s.id == id);

        public static ScenarioManifest Default => All.FirstOrDefault(s => !s.locked);

        private static List<ScenarioManifest> Load() =>
            Resources.LoadAll<TextAsset>("Scenarios")
                .Where(t => t.name == "scenario")
                .Select(t => JsonUtility.FromJson<ScenarioManifest>(t.text))
                .Where(s => !string.IsNullOrEmpty(s.id))
                .OrderBy(s => s.locked)
                .ThenBy(s => s.id)
                .ToList();

        // Local play record (Cloud Save later, see the proposal §3).
        private static string PlayedKey(string id) => $"played:{id}";

        public static bool IsPlayed(string id) => PlayerPrefs.GetInt(PlayedKey(id), 0) == 1;

        public static void MarkPlayed(string id)
        {
            if (string.IsNullOrEmpty(id) || IsPlayed(id)) return;
            PlayerPrefs.SetInt(PlayedKey(id), 1);
            PlayerPrefs.Save();
        }
    }
}
