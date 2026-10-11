using System.Linq;
using MurderMystery.Investigation;
using MurderMystery.Level;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MurderMystery.Gameplay
{
    /// <summary>
    /// Finds the investigation spot or NPC the player is looking at and handles keys:
    /// E investigate / talk, R retrieve own hidden item, Tab notebook.
    /// Offline only (debug): N next phase, 1–5 switch character.
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private Camera view;
        [SerializeField] private float maxDistance = 2.5f;
        [SerializeField] private float npcDistance = 3.0f;

        private InvestigationSpot[] spots;
        private Transform npc;

        public InvestigationSpot Focused { get; private set; }
        public bool NpcFocused { get; private set; }
        public bool NotebookOpen { get; private set; }
        public bool NpcMenuOpen { get; set; }

        private static readonly string[] CharacterOrder = { "jeong", "jaeha", "buni", "heo", "byeon" };

        public void Init(Camera cam) => view = cam;

        private void Start()
        {
            spots = FindObjectsByType<InvestigationSpot>(FindObjectsSortMode.None);
            npc = FindObjectsByType<GameplayMarker>(FindObjectsSortMode.None)
                .FirstOrDefault(m => m.MarkerId == "NPC_DoGeomnyul_Daecheong")?.transform;
        }

        public void SetNotebook(bool open)
        {
            NotebookOpen = open;
            FirstPersonController.SetCursorLocked(!NotebookOpen && !NpcMenuOpen);
        }

        private void Update()
        {
            var client = GameClient.Current;
            var keyboard = Keyboard.current;
            if (client == null || keyboard == null || view == null) return;

            UpdateFocus();

            if (keyboard.tabKey.wasPressedThisFrame) SetNotebook(!NotebookOpen);

            if (client.IsOffline && GameSessionHost.Instance != null)
            {
                if (keyboard.nKey.wasPressedThisFrame) GameSessionHost.Instance.AdvancePhase();
                for (var i = 0; i < CharacterOrder.Length; i++)
                    if (keyboard[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame)
                        GameSessionHost.Instance.SetLocalCharacter(CharacterOrder[i]);
            }

            if (NpcMenuOpen) return;

            if (keyboard.eKey.wasPressedThisFrame)
            {
                if (NpcFocused)
                {
                    NpcMenuOpen = true;
                    FirstPersonController.SetCursorLocked(false);
                }
                else if (Focused != null)
                {
                    client.Investigate(Focused.SpotId);
                }
            }

            if (keyboard.rKey.wasPressedThisFrame && Focused != null) client.Retrieve(Focused.SpotId);
        }

        private void UpdateFocus()
        {
            var origin = view.transform.position;
            var forward = view.transform.forward;

            NpcFocused = npc != null && Vector3.Distance(origin, npc.position) < npcDistance &&
                         Vector3.Angle(forward, npc.position + Vector3.up * 1.2f - origin) < 25f;

            // Pick the spot closest to the view ray within reach.
            Focused = null;
            var best = float.MaxValue;
            foreach (var s in spots)
            {
                var to = s.transform.position - origin;
                if (to.magnitude > maxDistance) continue;
                var angle = Vector3.Angle(forward, to);
                if (angle > 20f || angle > best) continue;
                best = angle;
                Focused = s;
            }
        }
    }
}
