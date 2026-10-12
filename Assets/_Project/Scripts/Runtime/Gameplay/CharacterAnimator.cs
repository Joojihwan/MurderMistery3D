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
        private Quaternion neckApplied;
        private Quaternion headApplied;

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
            Fix(neck, neckPitchFix, right, ref neckApplied);
            Fix(head, headPitchFix, right, ref headApplied);
        }

        /// <summary>
        /// Adds the correction on top of this frame's pose. When the Animator skipped the frame (culled while
        /// off-screen) the bone still holds last frame's corrected rotation, so nothing is added again.
        /// </summary>
        private static void Fix(Transform bone, float degrees, Vector3 axis, ref Quaternion applied)
        {
            if (bone == null || degrees == 0f || bone.localRotation == applied) return;
            bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
            applied = bone.localRotation;
        }
    }
}
