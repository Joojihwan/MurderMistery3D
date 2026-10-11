using System.Linq;
using UnityEngine;

namespace MurderMystery.Gameplay
{
    /// <summary>
    /// Turns a GameObject into the locally controlled first-person player (shared by the offline prototype
    /// and the networked owner player).
    /// </summary>
    public static class LocalPlayerRig
    {
        public static void Attach(GameObject player)
        {
            var cc = player.GetComponent<CharacterController>() ?? player.AddComponent<CharacterController>();
            ConfigureController(cc);

            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(player.transform, false);
            pivot.localPosition = new Vector3(0f, 1.55f, 0f);
            var cam = pivot.gameObject.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.tag = "MainCamera";
            pivot.gameObject.AddComponent<AudioListener>();

            // The generated scene's overview camera would fight for MainCamera / AudioListener.
            foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c != cam))
                other.gameObject.SetActive(false);

            player.AddComponent<FirstPersonController>().Init(pivot);
            var interactor = player.AddComponent<PlayerInteractor>();
            interactor.Init(cam);
            player.AddComponent<PrototypeHud>().Init(interactor);
        }

        public static void ConfigureController(CharacterController cc)
        {
            cc.height = 1.7f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.85f, 0f);
            // Every rise in the blockout is ≤ 0.3 m (stone → gidan). The controller lifts by stepOffset
            // before moving sideways, so door lintels must clear height + stepOffset + skin.
            cc.stepOffset = 0.35f;
        }
    }
}
