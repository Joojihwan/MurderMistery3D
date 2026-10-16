using MurderMystery.Gameplay;
using UnityEngine;

namespace MurderMystery.Level
{
    /// <summary>
    /// The way into the 밀담 house's hall: open as usual, shut while the council sits (everyone is gathered in
    /// the hall and may not leave until it ends). Shut means the leaves stay closed and a wall-high collider
    /// stands in the doorway.
    /// </summary>
    public class CouncilGate : MonoBehaviour
    {
        [SerializeField] private Collider[] barriers = new Collider[0];
        [SerializeField] private ProximityDoor[] doors = new ProximityDoor[0];

        private bool shut;

        public void Configure(Collider[] barrierColliders, ProximityDoor[] doorLeaves)
        {
            barriers = barrierColliders;
            doors = doorLeaves;
            foreach (var b in barriers) b.enabled = false;
        }

        private void Update()
        {
            var phase = GameClient.Current?.Snapshot?.phase;
            var wanted = phase == "Council1" || phase == "Council2";
            if (wanted == shut) return;
            shut = wanted;
            foreach (var b in barriers)
                if (b != null) b.enabled = shut;
            foreach (var d in doors)
            {
                if (d == null) continue;
                if (shut) d.CloseNow();
                d.enabled = !shut;
            }
        }
    }
}
