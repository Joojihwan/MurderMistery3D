using UnityEngine;

namespace MurderMystery.Level
{
    /// <summary>
    /// Per-scene first-person body. The grey-box uses the defaults; the scanned manor has 0.6 m stone
    /// platforms and low hanok door lintels, so its scene carries a profile with a shorter body, a small
    /// stepOffset (door sills) and an assisted step for the platforms.
    /// </summary>
    public class PlayerBodyProfile : MonoBehaviour
    {
        public float height = 1.7f;
        public float radius = 0.3f;
        public float stepOffset = 0.35f;
        /// <summary>Ledges up to this height are climbed by <c>StepAssist</c>; 0 disables it.</summary>
        public float assistStep;
        public float eyeHeight = 1.55f;

        public static PlayerBodyProfile Current => FindFirstObjectByType<PlayerBodyProfile>();
    }
}
