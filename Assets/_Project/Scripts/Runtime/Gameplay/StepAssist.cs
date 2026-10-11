using UnityEngine;

namespace MurderMystery.Gameplay
{
    /// <summary>
    /// Climbs ledges higher than the CharacterController's stepOffset (hanok stone platforms are ~0.6 m).
    /// Unity's own stepping lifts the body by stepOffset before moving, so a large stepOffset would snag on
    /// the low door lintels; instead stepOffset stays small and this lifts the body onto a ledge only when the
    /// last move was blocked sideways, the ledge top ahead is walkable and there is head room above it.
    /// (A shin-height ray is not used: platform bases slope outwards, so it would start inside them.)
    /// </summary>
    public static class StepAssist
    {
        private const float Reach = 0.3f;

        public static bool TryStepUp(CharacterController cc, Vector3 horizontalMove, float maxStep)
        {
            if (maxStep <= cc.stepOffset || !cc.isGrounded) return false;
            horizontalMove.y = 0f;
            if (horizontalMove.sqrMagnitude < 1e-6f) return false;

            var dir = horizontalMove.normalized;
            var t = cc.transform;
            var feet = t.position + cc.center - Vector3.up * (cc.height / 2f);
            const QueryTriggerInteraction ignore = QueryTriggerInteraction.Ignore;

            if ((cc.collisionFlags & CollisionFlags.Sides) == 0) return false;

            // Walkable top within reach of a step.
            var over = feet + dir * (cc.radius + Reach) + Vector3.up * (maxStep + 0.05f);
            if (!Physics.Raycast(over, Vector3.down, out var top, maxStep + 0.05f, ~0, ignore) || top.normal.y < 0.7f)
                return false;
            var rise = top.point.y - feet.y;
            if (rise <= cc.stepOffset || rise > maxStep) return false;

            // Head room for the whole body standing on it; right at the edge an eave beam may be in the way,
            // so a landing a little further in is tried too.
            foreach (var forward in LandingOffsets)
            {
                var dest = t.position + Vector3.up * (rise + 0.03f) + dir * (cc.radius * forward);
                if (!HasHeadRoom(cc, dest)) continue;
                cc.enabled = false;
                t.position = dest;
                cc.enabled = true;
                return true;
            }
            return false;
        }

        private static readonly float[] LandingOffsets = { 0.8f, 1.8f };

        private static bool HasHeadRoom(CharacterController cc, Vector3 at)
        {
            var bottom = at + cc.center - Vector3.up * (cc.height / 2f - cc.radius);
            var topSphere = at + cc.center + Vector3.up * (cc.height / 2f - cc.radius);
            foreach (var hit in Physics.OverlapCapsule(bottom + Vector3.up * 0.02f, topSphere, cc.radius * 0.95f, ~0, QueryTriggerInteraction.Ignore))
                if (hit != cc) return false;
            return true;
        }
    }
}
