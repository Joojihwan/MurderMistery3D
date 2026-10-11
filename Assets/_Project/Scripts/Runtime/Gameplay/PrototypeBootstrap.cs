using System.Linq;
using MurderMystery.Level;
using UnityEngine;

namespace MurderMystery.Gameplay
{
    /// <summary>
    /// On Play, if the scene has gameplay markers but no player, spawns the walk-around prototype:
    /// session host, first-person player at PlayerSpawn_0, interactor and HUD. Keeps the generated
    /// blockout scene free of hand-placed gameplay objects.
    /// </summary>
    public static class PrototypeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Spawn()
        {
            if (Object.FindFirstObjectByType<FirstPersonController>() != null) return;
            var markers = Object.FindObjectsByType<GameplayMarker>(FindObjectsSortMode.None);
            var spawn = markers.FirstOrDefault(m => m.Type == MarkerType.PlayerSpawn);
            if (spawn == null) return;

            if (GameSessionHost.Instance == null) new GameObject("GameSessionHost").AddComponent<GameSessionHost>();

            var player = new GameObject("Player (prototype)");
            player.transform.SetPositionAndRotation(spawn.transform.position + Vector3.up * 0.05f, Quaternion.Euler(0f, 0f, 0f));
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.7f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.85f, 0f);
            cc.stepOffset = 0.5f;    // stepping stones (0.25–0.3 m) then the 0.5–0.6 m gidan

            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(player.transform, false);
            pivot.localPosition = new Vector3(0f, 1.55f, 0f);
            var cam = pivot.gameObject.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.tag = "MainCamera";
            pivot.gameObject.AddComponent<AudioListener>();

            // The generated scene's overview camera would fight for MainCamera / AudioListener.
            foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c != cam))
            {
                other.gameObject.SetActive(false);
            }

            var controller = player.AddComponent<FirstPersonController>();
            controller.Init(pivot);
            var interactor = player.AddComponent<PlayerInteractor>();
            interactor.Init(cam);
            player.AddComponent<PrototypeHud>().Init(interactor);

            // Face the sarangchae from the discussion area.
            player.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
        }
    }
}
