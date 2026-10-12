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
        // Pitch corrections (degrees, + = forward) for rigs whose neck/head bones came out tilted
        // (set by CharacterRigBuilder; e.g. a long braid pulled 분이's neck bones back).
        [SerializeField] private float neckPitchFix;
        [SerializeField] private float headPitchFix;

        private Transform neck;
        private Transform head;

        public void SetPitchFix(float neckDegrees, float headDegrees)
        {
            neckPitchFix = neckDegrees;
            headPitchFix = headDegrees;
        }

        private Animator animator;
        private Vector3 lastPosition;
        private float speed;

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            lastPosition = transform.position;
            if (animator != null && animator.isHuman)
            {
                neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                head = animator.GetBoneTransform(HumanBodyBones.Head);
            }
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

        private void LateUpdate()
        {
            var right = transform.right;
            if (neck != null && neckPitchFix != 0f) neck.rotation = Quaternion.AngleAxis(neckPitchFix, right) * neck.rotation;
            if (head != null && headPitchFix != 0f) head.rotation = Quaternion.AngleAxis(headPitchFix, right) * head.rotation;
        }
    }
}
