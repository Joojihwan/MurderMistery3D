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
        public static readonly int SittingParam = Animator.StringToHash("Sitting");
        public static readonly int AirborneParam = Animator.StringToHash("Airborne");

        // Above the ground by more than a CharacterController skin plus a little: jumping or falling.
        private const float AirborneClearance = 0.15f;

        /// <summary>Plays the sit pose (synced by NetPlayer). Needs a Sitting clip in the controller.</summary>
        public bool Sitting
        {
            get => sitting;
            set
            {
                sitting = value;
                if (animator != null && HasParam(SittingParam)) animator.SetBool(SittingParam, value);
            }
        }

        private bool sitting;

        /// <summary>Stoops under a low lintel (synced by NetPlayer): the body bends forward at the spine.</summary>
        public bool Ducking { get; set; }

        // Bend (degrees forward) at each of the spine and chest at full stoop; the head is turned back up so
        // the face still looks ahead. 2 x 32 degrees brings the head down to about the 1 m ducked body.
        private const float StoopDegrees = 32f;
        private float stoop;
        private Transform spine;
        private Transform chest;
        private Quaternion spineApplied;
        private Quaternion chestApplied;

        [SerializeField] private float smoothing = 8f;
        // Pitch corrections (degrees, + = forward) for rigs whose neck/head bones came out tilted
        // (set by CharacterRigBuilder; e.g. a long braid pulled 분이's neck bones back).
        [SerializeField] private float neckPitchFix;
        [SerializeField] private float headPitchFix;
        // Height of the foot bones above the shoe soles in the bind pose (set by CharacterRigBuilder). Each
        // frame the body is shifted so the lower foot's sole meets the ground: Mixamo clips retargeted onto
        // these rigs leave the feet hovering by a different amount per clip.
        [SerializeField] private float soleHeight;
        [SerializeField] private bool groundFeet = true;
        private Vector3 modelBase;
        private float groundOffset;
        private Transform leftFoot;
        private Transform rightFoot;
        // The skinned shoes do not follow the foot bones exactly, so every half second the real lowest
        // vertex is measured and folded into a bias (cheap: a few bakes per second for the whole cast).
        private const float CalibrateInterval = 0.5f;
        private float soleBias;
        private float nextCalibrate;
        private SkinnedMeshRenderer[] skins;
        private Mesh baked;

        private Transform neck;
        private Transform head;
        private Quaternion neckApplied;
        private Quaternion headApplied;

        public void SetSoleHeight(float height) => soleHeight = height;

        /// <summary>Off while airborne (jumping), when the feet should leave the ground.</summary>
        public bool GroundFeet
        {
            get => groundFeet;
            set => groundFeet = value;
        }

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
            if (animator != null) modelBase = animator.transform.localPosition;
            if (animator != null && animator.isHuman)
            {
                neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                head = animator.GetBoneTransform(HumanBodyBones.Head);
                spine = animator.GetBoneTransform(HumanBodyBones.Spine);
                chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                skins = GetComponentsInChildren<SkinnedMeshRenderer>(true);
                nextCalibrate = Time.time + Random.value * CalibrateInterval;
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

        private bool HasParam(int hash)
        {
            foreach (var p in animator.parameters)
                if (p.nameHash == hash) return true;
            return false;
        }

        private void LateUpdate()
        {
            Ground();
            var right = transform.right;
            stoop = Mathf.MoveTowards(stoop, Ducking && !sitting ? 1f : 0f, 4f * Time.deltaTime);
            if (stoop > 0f)
            {
                Fix(spine, StoopDegrees * stoop, right, ref spineApplied);
                Fix(chest, StoopDegrees * stoop, right, ref chestApplied);
            }
            Fix(neck, neckPitchFix, right, ref neckApplied);
            Fix(head, headPitchFix - 1.5f * StoopDegrees * stoop, right, ref headApplied);
        }

        private void Ground()
        {
            if (soleHeight <= 0f || leftFoot == null || rightFoot == null || animator == null) return;
            var target = 0f;
            if (groundFeet)
            {
                // Foot height as if no offset were applied, relative to this character's ground.
                var ground = GroundY();
                var airborne = transform.position.y - ground > AirborneClearance;
                if (HasParam(AirborneParam)) animator.SetBool(AirborneParam, airborne);
                // In the air the feet hang from the body as the clip has them, at a standing height.
                if (airborne) ground = transform.position.y - 0.08f;
                var lowest = Mathf.Min(leftFoot.position.y, rightFoot.position.y) - groundOffset - ground;
                target = soleHeight + soleBias - lowest;
                if (!airborne && Time.time >= nextCalibrate)
                {
                    nextCalibrate = Time.time + CalibrateInterval;
                    soleBias -= LowestVertex() - ground;
                }
            }
            groundOffset = Mathf.Lerp(groundOffset, target, 1f - Mathf.Exp(-20f * Time.deltaTime));
            animator.transform.localPosition = modelBase + Vector3.up * groundOffset;
        }

        /// <summary>
        /// The surface under the character. A CharacterController keeps its root a skin width above the
        /// ground, so the root height alone would leave networked bodies hovering.
        /// </summary>
        private float GroundY()
        {
            var origin = transform.position + Vector3.up * 0.4f;
            var best = transform.position.y;
            var bestDistance = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(origin, Vector3.down, 0.8f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.collider is CharacterController || hit.distance >= bestDistance) continue;
                bestDistance = hit.distance;
                best = hit.point.y;
            }
            return best;
        }

        private float LowestVertex()
        {
            if (baked == null) baked = new Mesh();
            var lowest = float.MaxValue;
            foreach (var smr in skins)
            {
                if (smr == null || !smr.enabled) continue;
                smr.BakeMesh(baked, true);
                var m = smr.transform.localToWorldMatrix;
                var vertices = baked.vertices;
                for (var i = 0; i < vertices.Length; i++) lowest = Mathf.Min(lowest, m.MultiplyPoint3x4(vertices[i]).y);
            }
            return lowest == float.MaxValue ? transform.position.y : lowest;
        }

        private void OnDestroy()
        {
            if (baked != null) Destroy(baked);
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
