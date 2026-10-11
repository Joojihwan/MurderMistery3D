using UnityEngine;

namespace MurderMystery.Level
{
    public enum MarkerType
    {
        PlayerSpawn,
        NpcPoint,
        DiscussionArea
    }

    /// <summary>
    /// Named point the game flow looks up (spawns, NPC stations, gathering area).
    /// </summary>
    public class GameplayMarker : MonoBehaviour
    {
        [SerializeField] private MarkerType type;
        [SerializeField] private string markerId;
        [SerializeField] private float radius = 0.5f;

        public MarkerType Type => type;
        public string MarkerId => markerId;
        public float Radius => radius;

        public void Configure(MarkerType markerType, string id, float markerRadius)
        {
            type = markerType;
            markerId = id;
            radius = markerRadius;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = type switch
            {
                MarkerType.PlayerSpawn => Color.green,
                MarkerType.NpcPoint => new Color(0.1f, 0.4f, 0.9f),
                _ => Color.cyan
            };
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
