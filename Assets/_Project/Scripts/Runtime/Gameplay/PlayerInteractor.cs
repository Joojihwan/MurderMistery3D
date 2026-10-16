using System.Linq;
using MurderMystery.Investigation;
using MurderMystery.Level;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MurderMystery.Gameplay
{
    /// <summary>
    /// Finds the investigation spot or NPC the player is looking at and handles keys:
    /// E investigate / talk / use a door, R retrieve own hidden item, Tab notebook, Y / N answer someone asking
    /// to enter the room the player is in.
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
        /// <summary>A shut gate or blocked passage in front of the player.</summary>
        public LockedDoor FocusedDoor { get; private set; }
        /// <summary>The door of a private room in front of the player (or the one they are in).</summary>
        public TalkRoomPlace FocusedRoom { get; private set; }
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

            // Someone at the door of the room I am in: Y lets them in, N refuses (they never learn who).
            var requests = client.Snapshot?.roomRequests;
            if (requests != null && requests.Length > 0)
            {
                if (keyboard.yKey.wasPressedThisFrame) client.RespondRoom(requests[0].id, true);
                else if (keyboard.nKey.wasPressedThisFrame && !client.IsOffline) client.RespondRoom(requests[0].id, false);
            }

            if (keyboard.eKey.wasPressedThisFrame)
            {
                if (NpcFocused)
                {
                    NpcMenuOpen = true;
                    FirstPersonController.SetCursorLocked(false);
                }
                else if (Focused != null)
                {
                    // A spot behind a lock (a locked chest): the first E tries the keys.
                    var lockData = client.Data.LockOfSpot(Focused.SpotId);
                    if (lockData != null && !IsUnlocked(client, lockData.id)) client.TryUnlock(lockData.id);
                    else client.Investigate(Focused.SpotId);
                }
                else if (FocusedRoom != null)
                {
                    var snap = client.Snapshot;
                    if (snap != null && (snap.room == FocusedRoom.RoomId || snap.pendingRoom == FocusedRoom.RoomId)) client.LeaveRoom();
                    else client.RequestRoom(FocusedRoom.RoomId);
                }
                else if (FocusedDoor != null)
                {
                    client.TryUnlock(FocusedDoor.LockId);
                }
            }

            if (keyboard.rKey.wasPressedThisFrame && Focused != null) client.Retrieve(Focused.SpotId);
        }

        public static bool IsUnlocked(IGameClient client, string lockId) =>
            client.Snapshot?.unlockedLocks != null && System.Array.IndexOf(client.Snapshot.unlockedLocks, lockId) >= 0;

        private void UpdateFocus()
        {
            var origin = view.transform.position;
            var forward = view.transform.forward;

            var looking = Physics.Raycast(origin, forward, out var hit, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            FocusedDoor = looking ? LockedDoor.Find(hit.collider) : null;
            FocusedRoom = looking ? TalkRoomPlace.Find(hit.collider) : null;

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
