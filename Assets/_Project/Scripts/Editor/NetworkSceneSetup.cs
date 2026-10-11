using MurderMystery.Gameplay;
using MurderMystery.Network;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MurderMystery.EditorTools
{
    /// <summary>
    /// Creates the networked player prefab and adds NetworkManager, the in-scene NetGame and the lobby to the
    /// open scene. ManorBlockoutBuilder calls this after generating the level.
    /// </summary>
    public static class NetworkSceneSetup
    {
        private const string PrefabDir = "Assets/_Project/Prefabs";
        private const string PlayerPrefabPath = PrefabDir + "/NetPlayer.prefab";
        private const string GamePrefabPath = PrefabDir + "/NetGame.prefab";

        [MenuItem("Tools/Murder Mystery/Setup Network In Scene")]
        public static void SetupMenu()
        {
            Apply();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        public static void Apply()
        {
            var playerPrefab = EnsurePlayerPrefab();
            var gamePrefab = EnsureGamePrefab();

            foreach (var name in new[] { PrototypeBootstrap.NetworkManagerName, "NetGame", "Lobby" })
            {
                var old = GameObject.Find(name);
                if (old != null) Object.DestroyImmediate(old);
            }

            var nmGo = new GameObject(PrototypeBootstrap.NetworkManagerName);
            var transport = nmGo.AddComponent<UnityTransport>();
            var nm = nmGo.AddComponent<NetworkManager>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = playerPrefab,
                EnableSceneManagement = true,
                TickRate = 30,
                ConnectionApproval = false
            };

            nmGo.AddComponent<NetGameSpawner>().Init(gamePrefab);

            new GameObject("Lobby").AddComponent<LobbyMenu>();
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
