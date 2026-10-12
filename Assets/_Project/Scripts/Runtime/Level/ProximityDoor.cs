using System.Collections.Generic;
using UnityEngine;

namespace MurderMystery.Level
{
    /// <summary>
    /// A room door leaf that opens (is hidden) while any character stands near it and closes again when
    /// they leave. Door leaves have no collider (KhsManorBuilder), so this is purely visual: without it
    /// players would walk through closed paper doors. One shared updater polls every few frames.
    /// </summary>
    public class ProximityDoor : MonoBehaviour
    {
        private const float Radius = 1.3f;
        private const float Interval = 0.15f;

        private static readonly List<ProximityDoor> Doors = new List<ProximityDoor>();
        private static readonly List<Vector3> Bodies = new List<Vector3>();
        private static float nextPoll;

        private Renderer[] renderers;
        private Vector3 center;
        private bool open;

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(transform.position, Vector3.zero);
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            center = bounds.center;
        }

        private void OnEnable() => Doors.Add(this);
        private void OnDisable() => Doors.Remove(this);

        private void Update()
        {
            // The first door to update each interval polls for everyone.
            if (Time.unscaledTime < nextPoll || Doors.Count == 0 || Doors[0] != this) return;
            nextPoll = Time.unscaledTime + Interval;

            Bodies.Clear();
            foreach (var cc in FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
                Bodies.Add(cc.transform.position + Vector3.up * (cc.height / 2f));

            foreach (var door in Doors) door.SetOpen(door.AnyoneNear());
        }

        private bool AnyoneNear()
        {
            foreach (var b in Bodies)
            {
                var d = b - center;
                if (Mathf.Abs(d.y) < 1.6f && d.x * d.x + d.z * d.z < Radius * Radius) return true;
            }
            return false;
        }

        private void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            foreach (var r in renderers) r.enabled = !value;
        }
    }
}
