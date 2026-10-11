using UnityEngine;

namespace MurderMystery.Investigation
{
    public enum SpotLayer
    {
        // Open from the first investigation round.
        Surface,
        // Opens in the second round; the hider may retrieve earlier at extra cost.
        Deep
    }

    /// <summary>
    /// An investigable point in the manor. Clue IDs refer to docs/design/clues.md.
    /// </summary>
    public class InvestigationSpot : MonoBehaviour
    {
        [SerializeField] private string spotId;
        [SerializeField] private string displayName;
        [SerializeField] private SpotLayer layer;
        [SerializeField] private string[] clueIds = new string[0];
        [SerializeField] private float interactRadius = 1.2f;

        public string SpotId => spotId;
        public string DisplayName => displayName;
        public SpotLayer Layer => layer;
        public string[] ClueIds => clueIds;
        public float InteractRadius => interactRadius;

        public void Configure(string id, string name, SpotLayer spotLayer, string[] clues)
        {
            spotId = id;
            displayName = name;
            layer = spotLayer;
            clueIds = clues;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = layer == SpotLayer.Surface ? new Color(0.85f, 0.15f, 0.1f) : new Color(1f, 0.55f, 0f);
            Gizmos.DrawWireSphere(transform.position, interactRadius);
        }
    }
}
