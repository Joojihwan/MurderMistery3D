using System.Linq;
using MurderMystery.Level;
using UnityEngine;

namespace MurderMystery.Gameplay
{
    /// <summary>
    /// On Play: if the scene has gameplay markers but no player, spawns the offline walk-around prototype —
    /// unless the scene is set up for networking (a NetworkManager object exists), in which case the lobby
    /// decides and calls <see cref="SpawnOffline"/> for solo testing.
    /// </summary>
    public static class PrototypeBootstrap
    {
        public const string NetworkManagerName = "NetworkManager";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            // Keep simulating when the editor window is not focused (MCP-driven playtests, second monitor).
            Application.runInBackground = true;
            if (GameObject.Find(NetworkManagerName) != null) return;
            SpawnOffline();
        }

        public static void SpawnOffline()
        {
            if (Object.FindFirstObjectByType<FirstPersonController>() != null) return;
            var spawn = Object.FindObjectsByType<GameplayMarker>(FindObjectsSortMode.None)
                .FirstOrDefault(m => m.Type == MarkerType.PlayerSpawn);
            if (spawn == null) return;

            if (GameSessionHost.Instance == null) new GameObject("GameSessionHost").AddComponent<GameSessionHost>();

            var player = new GameObject("Player (prototype)");
            player.transform.SetPositionAndRotation(spawn.transform.position + Vector3.up * 0.05f, Quaternion.identity);
            LocalPlayerRig.Attach(player);
        }
    }
}
