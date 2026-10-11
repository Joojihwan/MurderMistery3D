using Unity.Netcode;
using UnityEngine;

namespace MurderMystery.Network
{
    /// <summary>
    /// Registers the NetGame prefab on every peer and spawns it on the server once hosting starts.
    /// (Script-created in-scene NetworkObjects get no scene id, so NetGame is spawned from a prefab instead.)
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class NetGameSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject netGamePrefab;

        public void Init(GameObject prefab) => netGamePrefab = prefab;

        private void Start()
        {
            var nm = GetComponent<NetworkManager>();
            nm.AddNetworkPrefab(netGamePrefab);
            nm.OnServerStarted += SpawnGame;
        }

        private void OnDestroy()
        {
            var nm = GetComponent<NetworkManager>();
            if (nm != null) nm.OnServerStarted -= SpawnGame;
        }

        private void SpawnGame()
        {
            if (NetGame.Instance != null) return;
            Instantiate(netGamePrefab).GetComponent<NetworkObject>().Spawn();
        }
    }
}
