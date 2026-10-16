using System.Collections.Generic;
using System.Linq;
using MurderMystery.Gameplay;
using UnityEngine;

namespace MurderMystery.Level
{
    /// <summary>
    /// A shut gate or a closed-off passage (lock data in clues.json "locks"). Looking at it shows why it
    /// does not open, and E tries the player's keys. Once the host reports the lock open (snapshot
    /// unlockedLocks), its blocking colliders go away and its leaves swing like any room door.
    /// </summary>
    public class LockedDoor : MonoBehaviour
    {
        [SerializeField] private string lockId;
        [SerializeField] private Transform[] leaves = new Transform[0];

        private static readonly Dictionary<Collider, LockedDoor> ByCollider = new Dictionary<Collider, LockedDoor>();
        private readonly List<Collider> blocking = new List<Collider>();
        private bool open;
        private float nextCheck;

        public string LockId => lockId;
        public IReadOnlyList<Transform> Leaves => leaves;

        public void Configure(string id, Transform[] leafRoots)
        {
            lockId = id;
            leaves = leafRoots ?? new Transform[0];
        }

        /// <summary>The lock a collider the player is looking at belongs to, if any.</summary>
        public static LockedDoor Find(Collider c) => c != null && ByCollider.TryGetValue(c, out var door) ? door : null;

        private void Awake()
        {
            blocking.AddRange(GetComponentsInChildren<Collider>(true));
            foreach (var leaf in leaves.Where(l => l != null)) blocking.AddRange(leaf.GetComponentsInChildren<Collider>(true));
            foreach (var c in blocking) ByCollider[c] = this;
        }

        private void OnDestroy()
        {
            foreach (var c in blocking) ByCollider.Remove(c);
        }

        private void Update()
        {
            if (open || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.25f;
            var unlocked = GameClient.Current?.Snapshot?.unlockedLocks;
            if (unlocked != null && System.Array.IndexOf(unlocked, lockId) >= 0) Open();
        }

        private void Open()
        {
            open = true;
            foreach (var c in blocking) c.enabled = false;
            foreach (var leaf in leaves.Where(l => l != null))
                if (!leaf.TryGetComponent<ProximityDoor>(out _)) leaf.gameObject.AddComponent<ProximityDoor>();
        }
    }
}
