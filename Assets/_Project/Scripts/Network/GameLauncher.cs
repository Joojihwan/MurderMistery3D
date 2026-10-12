using System;
using System.Threading.Tasks;
using MurderMystery.Core;
using MurderMystery.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MurderMystery.Network
{
    /// <summary>
    /// Front-end flow: open a room for a scenario (online via Sessions + Relay, or LAN), join one, practise
    /// alone, or go back to the title. The host makes Netcode load the scenario's map for everybody.
    /// </summary>
    public static class GameLauncher
    {
        public const string TitleScene = "Title";

        /// <summary>The scenario this peer is playing (the host's choice; clients learn it from NetGame).</summary>
        public static ScenarioManifest Scenario { get; set; }

        public static bool InRoom => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        public static async Task HostOnlineAsync(ScenarioManifest scenario)
        {
            Scenario = scenario;
            await SessionService.HostOnlineAsync(scenario.id);
            LoadMapForEveryone();
        }

        public static bool HostLan(ScenarioManifest scenario)
        {
            Scenario = scenario;
            if (!SessionService.HostLan()) return false;
            LoadMapForEveryone();
            return true;
        }

        /// <summary>The host's map arrives through Netcode scene synchronisation.</summary>
        public static Task JoinOnlineAsync(string code) => SessionService.JoinOnlineAsync(code);

        public static bool JoinLan(string address) => SessionService.JoinLan(address);

        /// <summary>Walk the scenario's map alone, offline (the prototype session with character switching).</summary>
        public static void PractiseAlone(ScenarioManifest scenario)
        {
            Scenario = scenario;
            void OnLoaded(Scene scene, LoadSceneMode mode)
            {
                SceneManager.sceneLoaded -= OnLoaded;
                PrototypeBootstrap.SpawnOffline();
            }
            SceneManager.sceneLoaded += OnLoaded;
            SceneManager.LoadScene(scenario.mapScene);
        }

        public static async Task ReturnToTitleAsync()
        {
            try
            {
                await SessionService.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Launcher] leave failed: {e.Message}");
            }
            // The title scene brings its own NetworkManager; drop the persistent one once it has shut down.
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                if (nm.IsListening) nm.Shutdown();
                while (nm != null && nm.ShutdownInProgress) await Task.Yield();
                if (nm != null) UnityEngine.Object.Destroy(nm.gameObject);
            }
            FirstPersonController.SetCursorLocked(false);
            SceneManager.LoadScene(TitleScene);
        }

        private static void LoadMapForEveryone()
        {
            var nm = NetworkManager.Singleton;
            var status = nm.SceneManager.LoadScene(Scenario.mapScene, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started) Debug.LogError($"[Launcher] map load failed: {status}");
        }
    }
}
