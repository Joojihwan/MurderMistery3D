using System;
using MurderMystery.Level;
using UnityEngine;

namespace MurderMystery.Gameplay
{
    /// <summary>
    /// Moves the local player in and out of the private rooms as the host decides (snapshot "room"):
    /// entering puts them on a seat inside; leaving a small room puts them on its step outside; when the
    /// council ends everyone returns to where they stood before it.
    /// </summary>
    public class RoomPresence : MonoBehaviour
    {
        /// <summary>Set by the network layer so a jump is not interpolated across the map for others.</summary>
        public static Action<GameObject, Vector3, Quaternion> Teleport;

        private string current = string.Empty;
        private Vector3 beforePosition;
        private Quaternion beforeRotation;
        private bool hasBefore;

        private void Update()
        {
            var snap = GameClient.Current?.Snapshot;
            if (snap == null) return;
            var wanted = snap.room ?? string.Empty;
            if (wanted == current) return;

            var leaving = TalkRoomPlace.Find(current);
            var entering = TalkRoomPlace.Find(wanted);
            if (!string.IsNullOrEmpty(wanted) && entering == null) return;   // scene without rooms
            current = wanted;

            if (entering != null)
            {
                if (leaving == null)
                {
                    beforePosition = transform.position;
                    beforeRotation = transform.rotation;
                    hasBefore = true;
                }
                var room = Array.Find(snap.rooms, r => r.id == wanted);
                var index = room != null ? Array.IndexOf(room.members, snap.characterId) : 0;
                var seat = entering.Seat(index);
                Move(seat.position, seat.rotation);
                return;
            }

            // Out of a room: small rooms let you out at their door, the council puts you back where you were.
            if (leaving != null && leaving.Outside != null && !string.IsNullOrEmpty(snap.phase) && snap.phase.StartsWith("Investigation"))
                Move(leaving.Outside.position, leaving.Outside.rotation);
            else if (hasBefore)
                Move(beforePosition, beforeRotation);
            hasBefore = false;
        }

        private void Move(Vector3 position, Quaternion rotation)
        {
            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            if (Teleport != null) Teleport(gameObject, position, rotation);
            else transform.SetPositionAndRotation(position, rotation);
            if (cc != null) cc.enabled = true;
        }
    }
}
