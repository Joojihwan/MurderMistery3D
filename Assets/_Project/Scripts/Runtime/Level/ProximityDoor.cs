using System.Collections.Generic;
using UnityEngine;

namespace MurderMystery.Level
{
    /// <summary>
    /// A room door leaf that swings open on its hinge while any character stands near it (away from that
    /// person) and closes again when they leave. Door leaves have no collider (KhsManorBuilder), so this is
    /// purely visual: without it players would walk through closed paper doors. One shared updater polls
    /// every few frames; each leaf animates itself.
    /// </summary>
    public class ProximityDoor : MonoBehaviour
    {
        private const float Radius = 1.3f;
        private const float Interval = 0.15f;
        private const float OpenAngle = 95f;
        private const float Speed = 240f;   // degrees per second

        private static readonly List<ProximityDoor> Doors = new List<ProximityDoor>();
        private static readonly List<Vector3> Bodies = new List<Vector3>();
        private static float nextPoll;

        private Vector3 center;
        private Vector3 through;
        private Vector3 hinge;
        private Vector3 closedPosition;
        private Quaternion closedRotation;
        private float angle;
        private float target;

        private void Awake()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(transform.position, Vector3.zero);
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            center = bounds.center;
            closedPosition = transform.position;
            closedRotation = transform.rotation;

            // The leaf's thin axis is the way through.
            var filter = GetComponentInChildren<MeshFilter>();
            through = Vector3.forward;
            if (filter != null && filter.sharedMesh != null)
            {
                var size = filter.sharedMesh.bounds.size;
                var axis = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
                var t = filter.transform.TransformDirection(axis);
                t.y = 0f;
                if (t.sqrMagnitude > 0.01f) through = t.normalized;
            }
            // The scanned leaves have their origin on the hinge edge.
            hinge = transform.position;
            hinge.y = center.y;
        }

        private void OnEnable() => Doors.Add(this);
        private void OnDisable() => Doors.Remove(this);

        private void Update()
        {
            // The first door to update each interval polls for everyone.
            if (Time.unscaledTime >= nextPoll && Doors.Count > 0 && Doors[0] == this)
            {
                nextPoll = Time.unscaledTime + Interval;
                Bodies.Clear();
                foreach (var cc in FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
                    Bodies.Add(cc.transform.position + Vector3.up * (cc.height / 2f));
                foreach (var door in Doors) door.Poll();
            }

            if (Mathf.Approximately(angle, target)) return;
            angle = Mathf.MoveTowards(angle, target, Speed * Time.deltaTime);
            var turn = Quaternion.AngleAxis(angle, Vector3.up);
            transform.SetPositionAndRotation(hinge + turn * (closedPosition - hinge), turn * closedRotation);
        }

        private void Poll()
        {
            Vector3? nearest = null;
            var best = Radius * Radius;
            foreach (var b in Bodies)
            {
                var d = b - center;
                if (Mathf.Abs(d.y) >= 1.6f) continue;
                var flat = d.x * d.x + d.z * d.z;
                if (flat < best)
                {
                    best = flat;
                    nearest = b;
                }
            }
            if (nearest == null)
            {
                target = 0f;
                return;
            }
            if (target != 0f) return;   // already open: stay put until everyone has gone
            // Swing away from the person: the way that moves the leaf's middle to their far side.
            var away = -Mathf.Sign(Vector3.Dot(nearest.Value - center, through));
            var swung = Quaternion.AngleAxis(OpenAngle, Vector3.up) * (center - hinge) + hinge;
            target = Vector3.Dot(swung - center, through) * away > 0f ? OpenAngle : -OpenAngle;
        }
    }
}
