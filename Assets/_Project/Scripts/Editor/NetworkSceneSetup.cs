using MurderMystery.Gameplay;
using MurderMystery.Network;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;

namespace MurderMystery.EditorTools
{
    /// <summary>
    /// Network objects. The NetworkManager lives in the Title scene and persists into the map the host loads
    /// (<see cref="AddNetworkManager"/>); map scenes only need the prefabs (<see cref="Apply"/>, called by the
    /// map builders), and opened on their own they run the offline prototype.
    /// </summary>
    public static class NetworkSceneSetup
    {
        private const string PrefabDir = "Assets/_Project/Prefabs";
        private const string PlayerPrefabPath = PrefabDir + "/NetPlayer.prefab";
        private const string GamePrefabPath = PrefabDir + "/NetGame.prefab";

        /// <summary>For map scenes: make sure the prefabs exist and no NetworkManager or old lobby is left.</summary>
        public static void Apply()
        {
            EnsurePlayerPrefab();
            EnsureGamePrefab();
            foreach (var name in new[] { PrototypeBootstrap.NetworkManagerName, "NetGame", "Lobby" })
            {
                var old = GameObject.Find(name);
                if (old != null) Object.DestroyImmediate(old);
            }
        }

        /// <summary>For the Title scene: the persistent NetworkManager with transport and spawner.</summary>
        public static GameObject AddNetworkManager()
        {
            var playerPrefab = EnsurePlayerPrefab();
            var gamePrefab = EnsureGamePrefab();

            var nmGo = new GameObject(PrototypeBootstrap.NetworkManagerName);
            var transport = nmGo.AddComponent<UnityTransport>();
            var nm = nmGo.AddComponent<NetworkManager>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                // Spawned by NetGameSpawner once the map has loaded, not on connection.
                PlayerPrefab = null,
                EnableSceneManagement = true,
                TickRate = 30,
                ConnectionApproval = false
            };
            nmGo.AddComponent<NetGameSpawner>().Init(gamePrefab, playerPrefab);
            return nmGo;
        }

        private static GameObject EnsureGamePrefab()
        {
            if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder("Assets/_Project", "Prefabs");
            var go = new GameObject("NetGame");
            go.AddComponent<NetworkObject>();
            go.AddComponent<NetGame>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, GamePrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject EnsurePlayerPrefab()
        {
            if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder("Assets/_Project", "Prefabs");

            var go = new GameObject("NetPlayer");
            go.AddComponent<NetworkObject>();
            var nt = go.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            nt.SyncRotAngleX = false;
            nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;
            LocalPlayerRig.ConfigureController(go.AddComponent<CharacterController>());
            go.AddComponent<NetPlayer>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PlayerPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }
    }
}
