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

            // Walkable top within reach of a step. Near first: a door sill is only a hand's width deep, and a
            // ray further out would miss it and look down past it.
            foreach (var reach in Reaches)
            {
                var over = feet + dir * (cc.radius + reach) + Vector3.up * (maxStep + 0.05f);
                if (!Physics.Raycast(over, Vector3.down, out var top, maxStep + 0.05f, ~0, ignore) || top.normal.y < 0.7f)
                    continue;
                var rise = top.point.y - feet.y;
                if (rise <= cc.stepOffset || rise > maxStep) continue;

                // Head room for the whole body standing on it; right at the edge an eave beam may be in the way,
                // so a landing a little further in is tried too. Under a low lintel, duck onto it.
                foreach (var forward in LandingOffsets)
                {
                    var dest = t.position + Vector3.up * (rise + 0.03f) + dir * (cc.radius * forward);
                    if (!HasHeadRoom(cc, dest, cc.height))
                    {
                        if (cc.height <= Duck.DuckHeight || !HasHeadRoom(cc, dest, Duck.DuckHeight)) continue;
                        Duck.SetHeight(cc, Duck.DuckHeight);
                    }
                    cc.enabled = false;
                    t.position = dest;
                    cc.enabled = true;
                    return true;
                }
            }
            return false;
        }

        private static readonly float[] Reaches = { 0.06f, 0.15f, 0.3f };
        private static readonly float[] LandingOffsets = { 0.8f, 1.8f };

        private static bool HasHeadRoom(CharacterController cc, Vector3 at, float height)
        {
            var bottom = at + Vector3.up * cc.radius;
            var topSphere = at + Vector3.up * (height - cc.radius);
            foreach (var hit in Physics.OverlapCapsule(bottom + Vector3.up * 0.02f, topSphere, cc.radius * 0.95f, ~0, QueryTriggerInteraction.Ignore))
                if (hit != cc) return false;
            return true;
        }
    }

    /// <summary>
    /// Ducks under low lintels: hanok doors are 1.2-1.4 m above the sill. When the head would hit something
    /// ahead but a lowered body would pass, the controller shrinks (feet stay put); it stands up again as soon
    /// as there is room. Shared by the player and the walking probe.
    /// </summary>
    public static class Duck
    {
        public const float DuckHeight = 1.1f;

        /// <summary>Returns the controller height to use this frame (call before Move).</summary>
        public static void Update(CharacterController cc, Vector3 horizontalMove, float standHeight)
        {
            if (standHeight <= DuckHeight) return;
            var feet = cc.transform.position + cc.center - Vector3.up * (cc.height / 2f);
            var ducked = cc.height < standHeight - 0.01f;
            horizontalMove.y = 0f;

            if (!ducked)
            {
                if (horizontalMove.sqrMagnitude < 1e-6f) return;
                var dir = horizontalMove.normalized;
                // Something at head height (between the ducked top and the standing top) right ahead?
                var head = feet + Vector3.up * (standHeight - cc.radius);
                var low = feet + Vector3.up * (DuckHeight + 0.02f + cc.radius * 0.5f);
                if (!Physics.CapsuleCast(low, head, cc.radius * 0.9f, dir, out var hit, 0.35f, ~0, QueryTriggerInteraction.Ignore))
                    return;
                if (hit.collider == cc) return;
                SetHeight(cc, DuckHeight);
            }
            else
            {
                // Stand up when the standing body fits here.
                var bottom = feet + Vector3.up * (cc.radius + 0.05f);
                var top = feet + Vector3.up * (standHeight - cc.radius);
                foreach (var c in Physics.OverlapCapsule(bottom, top, cc.radius * 0.95f, ~0, QueryTriggerInteraction.Ignore))
                    if (c != cc) return;
                SetHeight(cc, standHeight);
            }
        }

        internal static void SetHeight(CharacterController cc, float height)
        {
            cc.height = height;
            cc.center = new Vector3(0f, height / 2f, 0f);
        }
    }
}
