using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MurderMystery.Gameplay;
using MurderMystery.Investigation;
using UnityEngine;
using UnityEngine.AI;

namespace MurderMystery.Level
{
    /// <summary>
    /// Play-mode test: walks a CharacterController shaped like the local player from the discussion area to
    /// every investigation spot and NPC point along a NavMesh path (baked on the fly), the way a player
    /// would, and reports which ones it could not reach. Add it to any object in a running scene and read
    /// <see cref="Report"/> once <see cref="Done"/> is set. With <see cref="DoorMode"/> it instead walks
    /// straight through every door leaf of the scanned house, front to back and back to front (no NavMesh:
    /// this is what a player pressing W at the door gets).
    /// </summary>
    public class WalkabilityProbe : MonoBehaviour
    {
        public static bool Done { get; private set; }
        public static string Report { get; private set; } = string.Empty;
        public static bool DoorMode { get; set; }

        private const float Speed = 3.5f;
        private const float StuckSeconds = 2f;
        private static float AssistStep => PlayerBodyProfile.Current != null ? PlayerBodyProfile.Current.assistStep : 0f;

        private IEnumerator Start()
        {
            Done = false;
            Report = string.Empty;
            var navData = Bake();
            var body = new GameObject("WalkProbeBody");
            var cc = body.AddComponent<CharacterController>();
            LocalPlayerRig.ConfigureController(cc);
            var standHeight = cc.height;

            var start = FindObjectsByType<GameplayMarker>(FindObjectsSortMode.None).First(m => m.Type == MarkerType.DiscussionArea).transform.position;
            var targets = DoorMode
                ? DoorPassages()
                : FindObjectsByType<InvestigationSpot>(FindObjectsSortMode.None).Select(s => (s.name, start, s.transform.position))
                    .Concat(FindObjectsByType<GameplayMarker>(FindObjectsSortMode.None).Where(m => m.Type == MarkerType.NpcPoint).Select(m => (m.name, start, m.transform.position)))
                    .OrderBy(t => t.Item1).ToList();

            var sb = new StringBuilder();
            var failures = 0;
            foreach (var (name, from, target) in targets)
            {
                var result = new List<string>();
                yield return Walk(cc, from, target, standHeight, DoorMode, result);
                if (DoorMode)
                {
                    var back = new List<string>();
                    yield return Walk(cc, target, from, standHeight, true, back);
                    if (result[0] == "OK" && back[0] != "OK") { result[0] = "ONEWAY"; result[1] = "back " + back[1]; }
                    else if (result[0] != "OK" && back[0] == "OK") { result[0] = "ONEWAY"; result[1] = "in " + result[1]; }
                }
                if (result[0] != "OK") failures++;
                if (!DoorMode || result[0] != "OK") sb.AppendLine($"{result[0],-5} {name} {result[1]}");
            }
            sb.Insert(0, $"walked {targets.Count} {(DoorMode ? "doors" : "targets")}, {failures} failed\n");

            Destroy(body);
            NavMesh.RemoveNavMeshData(navData);
            // Callers may speed the walk up with Time.timeScale; never leave it changed.
            Time.timeScale = 1f;
            Report = sb.ToString();
            Done = true;
            Debug.Log($"[WalkabilityProbe]\n{Report}");
        }

        /// <summary>For every door leaf of the scanned house: a point 0.9 m in front of it and one behind it.</summary>
        private static List<(string, Vector3, Vector3)> DoorPassages()
        {
            var list = new List<(string, Vector3, Vector3)>();
            var house = GameObject.Find("KHS_KimMyeonggwanHouse");
            if (house == null) return list;
            var seen = new HashSet<Transform>();
            foreach (var mf in house.GetComponentsInChildren<MeshFilter>())
            {
                Transform root = null;
                for (var t = mf.transform; t != null; t = t.parent)
                    if (t.name.StartsWith("SM_Door") && !t.name.StartsWith("SM_DoorFrame")) root = t;
                if (root == null || !seen.Add(root)) continue;
                var rs = root.GetComponentsInChildren<Renderer>(true);
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                var normal = b.size.x < b.size.z ? Vector3.right : Vector3.forward;
                var a = FloorAt(b.center + normal * 0.9f, b.min.y + 1f);
                var c = FloorAt(b.center - normal * 0.9f, b.min.y + 1f);
                if (a == null || c == null) continue;
                list.Add(($"{root.name}@({b.center.x:F1},{b.center.z:F1})", a.Value, c.Value));
            }
            return list;
        }

        private static Vector3? FloorAt(Vector3 p, float fromY)
        {
            var hits = Physics.RaycastAll(new Vector3(p.x, fromY, p.z), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => h.normal.y > 0.6f).OrderBy(h => h.distance).ToList();
            return hits.Count > 0 ? hits[0].point : (Vector3?)null;
        }

        private static IEnumerator Walk(CharacterController cc, Vector3 start, Vector3 target, float standHeight, bool straight, List<string> result)
        {
            cc.enabled = false;
            cc.height = standHeight;
            cc.center = new Vector3(0f, standHeight / 2f, 0f);
            cc.transform.position = start + Vector3.up * 0.1f;
            cc.enabled = true;
            yield return null;

            var corners = new List<Vector3> { target };
            var goal = target;
            if (!straight)
            {
                if (!NavMesh.SamplePosition(target, out var onNav, 1.2f, NavMesh.AllAreas) ||
                    !NavMesh.SamplePosition(cc.transform.position, out var from, 1.2f, NavMesh.AllAreas))
                {
                    result.Add("NONAV");
                    result.Add(string.Empty);
                    yield break;
                }
                var path = new NavMeshPath();
                NavMesh.CalculatePath(from.position, onNav.position, NavMesh.AllAreas, path);
                if (path.status != NavMeshPathStatus.PathComplete)
                {
                    result.Add("NOPATH");
                    result.Add(string.Empty);
                    yield break;
                }
                goal = onNav.position;
                corners = path.corners.Skip(1).ToList();
            }

            var vy = 0f;
            var best = float.MaxValue;
            var sinceProgress = 0f;
            var sidesteps = 0;
            foreach (var corner in corners)
            {
                while (true)
                {
                    var pos = cc.transform.position;
                    var flat = new Vector3(corner.x - pos.x, 0f, corner.z - pos.z);
                    if (flat.magnitude < 0.25f) break;

                    var dt = Time.deltaTime;
                    Duck.Update(cc, flat, standHeight);
                    StepAssist.TryStepUp(cc, flat, AssistStep);
                    vy = cc.isGrounded ? -1f : vy - 9.81f * dt;
                    cc.Move(flat.normalized * Speed * dt + Vector3.up * vy * dt);

                    var remaining = Vector3.Distance(cc.transform.position, goal);
                    if (remaining < best - 0.05f)
                    {
                        best = remaining;
                        sinceProgress = 0f;
                    }
                    else if ((sinceProgress += dt) > StuckSeconds)
                    {
                        if (sidesteps++ >= 2)
                        {
                            result.Add("STUCK");
                            result.Add($"at {cc.transform.position:F1} heading to {corner:F1}");
                            yield break;
                        }
                        // Like a player edging round a pillar: step sideways for a moment, then carry on.
                        var side = Vector3.Cross(Vector3.up, flat.normalized) * (sidesteps == 1 ? 1f : -1f);
                        for (var s = 0f; s < 0.35f; s += Time.deltaTime)
                        {
                            cc.Move(side * Speed * 0.5f * Time.deltaTime + Vector3.down * 0.02f);
                            yield return null;
                        }
                        sinceProgress = 0f;
                    }
                    yield return null;
                }
            }
            var end = Vector3.Distance(cc.transform.position, goal);
            result.Add(end < 0.9f ? "OK" : "SHORT");
            result.Add($"end {end:F2} m from target");
        }

        private static NavMeshDataInstance Bake()
        {
            var probe = new GameObject("tmp").AddComponent<CharacterController>();
            LocalPlayerRig.ConfigureController(probe);
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentHeight = Duck.DuckHeight;   // players duck under low lintels
            settings.agentRadius = probe.radius;
            settings.agentClimb = Mathf.Max(probe.stepOffset, AssistStep);
            settings.agentSlope = 40f;
            Destroy(probe.gameObject);

            var bounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(160f, 14f, 160f));
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            return NavMesh.AddNavMeshData(data);
        }
    }
}
