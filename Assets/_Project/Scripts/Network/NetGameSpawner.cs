using System.Collections.Generic;
using System.Linq;
using MurderMystery.Level;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MurderMystery.Network
{
    /// <summary>
    /// Lives on the persistent NetworkManager (Title scene). Registers the network prefabs on every peer and,
    /// on the server, spawns the game and the player bodies once a map scene has loaded for everyone — the
    /// room is made in the title screen, then <see cref="GameLauncher"/> has Netcode load the scenario's map.
    /// Players are spawned here rather than by NetworkConfig.PlayerPrefab so they appear in the map, not the
    /// title scene. (Script-created in-scene NetworkObjects get no scene id, hence prefabs.)
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class NetGameSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject netGamePrefab;
        [SerializeField] private GameObject netPlayerPrefab;

        private NetworkManager nm;

        public void Init(GameObject game, GameObject player)
        {
            netGamePrefab = game;
            netPlayerPrefab = player;
        }

        private void Start()
        {
            nm = GetComponent<NetworkManager>();
            nm.AddNetworkPrefab(netGamePrefab);
            nm.AddNetworkPrefab(netPlayerPrefab);
            nm.OnServerStarted += OnServerStarted;
            nm.OnServerStopped += OnServerStopped;
        }

        private void OnDestroy()
        {
            if (nm == null) return;
            nm.OnServerStarted -= OnServerStarted;
            nm.OnServerStopped -= OnServerStopped;
        }

        private void OnServerStarted()
        {
            nm.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
            nm.SceneManager.OnSynchronizeComplete += OnSynchronizeComplete;
            // Started from inside a map (editor tests): spawn right away.
            if (InMap()) SpawnGameAndPlayers(nm.ConnectedClientsIds);
        }

        private void OnServerStopped(bool wasHost)
        {
            if (nm.SceneManager == null) return;
            nm.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
            nm.SceneManager.OnSynchronizeComplete -= OnSynchronizeComplete;
        }

        private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
        {
            if (InMap()) SpawnGameAndPlayers(completed);
        }

        // A client that joins while the map is already up.
        private void OnSynchronizeComplete(ulong clientId)
        {
            if (InMap()) SpawnPlayer(clientId);
        }

        private void SpawnGameAndPlayers(IEnumerable<ulong> clients)
        {
            if (NetGame.Instance == null) Instantiate(netGamePrefab).GetComponent<NetworkObject>().Spawn(true);
            foreach (var id in clients.ToList()) SpawnPlayer(id);
        }

        private void SpawnPlayer(ulong clientId)
        {
            if (!nm.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject != null) return;
            Instantiate(netPlayerPrefab).GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
        }

        private static bool InMap() =>
            FindObjectsByType<GameplayMarker>(FindObjectsSortMode.None).Any(m => m.Type == MarkerType.PlayerSpawn);
    }
}
