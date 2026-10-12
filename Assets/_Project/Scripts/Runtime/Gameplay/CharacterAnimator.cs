using UnityEngine;

namespace MurderMystery.Gameplay
{
    /// <summary>
    /// Drives the idle/walk blend of a rigged character from how fast its transform actually moves, so it
    /// works the same for networked bodies (moved by NetworkTransform) and the standing NPC.
    /// </summary>
    public class CharacterAnimator : MonoBehaviour
    {
        public static readonly int SpeedParam = Animator.StringToHash("Speed");

        [SerializeField] private float smoothing = 8f;

        private Animator animator;
        private Vector3 lastPosition;
        private float speed;

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            lastPosition = transform.position;
        }

        private void Update()
        {
            if (animator == null || Time.deltaTime <= 0f) return;
            var delta = transform.position - lastPosition;
            lastPosition = transform.position;
            delta.y = 0f;
            var target = delta.magnitude / Time.deltaTime;
            speed = Mathf.Lerp(speed, target, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
            animator.SetFloat(SpeedParam, speed);
        }
    }
}
