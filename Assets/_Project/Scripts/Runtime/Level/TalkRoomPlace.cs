using System.Collections.Generic;
using UnityEngine;

namespace MurderMystery.Level
{
    /// <summary>
    /// The building of a private room (RoomManager). Its door leaves stay shut and solid: players get in
    /// and out only through the room rules, and are then moved (RoomPresence) to a seat inside or to the
    /// step outside. Looking at the door shows who is inside.
    /// </summary>
    public class TalkRoomPlace : MonoBehaviour
    {
        [SerializeField] private string roomId;
        [SerializeField] private Transform[] seats = new Transform[0];
        [SerializeField] private Transform outside;
        [SerializeField] private Collider[] doorColliders = new Collider[0];

        private static readonly Dictionary<string, TalkRoomPlace> ById = new Dictionary<string, TalkRoomPlace>();
        private static readonly Dictionary<Collider, TalkRoomPlace> ByCollider = new Dictionary<Collider, TalkRoomPlace>();

        public string RoomId => roomId;
        public Transform Outside => outside;

        public void Configure(string id, Transform[] seatPoints, Transform outsidePoint, Collider[] doors)
        {
            roomId = id;
            seats = seatPoints;
            outside = outsidePoint;
            doorColliders = doors;
        }

        public static TalkRoomPlace Find(string id) => id != null && ById.TryGetValue(id, out var place) ? place : null;
        public static TalkRoomPlace Find(Collider c) => c != null && ByCollider.TryGetValue(c, out var place) ? place : null;

        /// <summary>The seat for the n-th person inside (wraps around).</summary>
        public Transform Seat(int index) => seats.Length == 0 ? transform : seats[Mathf.Abs(index) % seats.Length];

        private void OnEnable()
        {
            ById[roomId] = this;
            foreach (var c in doorColliders)
                if (c != null) ByCollider[c] = this;
        }

        private void OnDisable()
        {
            if (ById.TryGetValue(roomId, out var p) && p == this) ById.Remove(roomId);
            foreach (var c in doorColliders)
                if (c != null) ByCollider.Remove(c);
        }
    }
}
