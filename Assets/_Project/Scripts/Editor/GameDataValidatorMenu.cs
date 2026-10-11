using System.Linq;
using MurderMystery.Core;
using MurderMystery.Investigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MurderMystery.EditorTools
{
    /// <summary>
    /// Runs DataValidator and cross-checks clue spots against the blockout scene's InvestigationSpots.
    /// </summary>
    public static class GameDataValidatorMenu
    {
        private const string ScenePath = "Assets/_Project/Scenes/Manor_Blockout.unity";

        [MenuItem("Tools/Murder Mystery/Validate Data")]
        public static void ValidateMenu()
        {
            var count = Run();
            if (count == 0) Debug.Log("[DataValidator] OK — no issues.");
            else Debug.LogError($"[DataValidator] {count} issue(s) found. See messages above.");
        }

        /// <summary>Batch-mode entry: exits with code 1 when issues are found.</summary>
        public static void ValidateBatch()
        {
            EditorApplication.Exit(Run() == 0 ? 0 : 1);
        }

        private static int Run()
        {
            var data = GameData.LoadFromResources();
            var issues = DataValidator.Validate(data);

            if (EditorSceneManager.GetActiveScene().path != ScenePath && System.IO.File.Exists(ScenePath))
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            var spots = Object.FindObjectsByType<InvestigationSpot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (spots.Length > 0)
            {
                foreach (var clue in data.Clues.clues)
                foreach (var spotId in clue.SpotIds)
                {
                    var spot = spots.FirstOrDefault(s => s.SpotId == spotId);
                    if (spot == null) issues.Add($"{clue.id}: spot '{spotId}' missing in scene");
                    else if (!spot.ClueIds.Contains(clue.id)) issues.Add($"{clue.id}: scene spot '{spotId}' does not list this clue");
                }
            }
            else
            {
                issues.Add("no InvestigationSpot found in the open scenes (scene cross-check skipped)");
            }

            foreach (var issue in issues) Debug.LogWarning($"[DataValidator] {issue}");
            return issues.Count;
        }
    }
}
