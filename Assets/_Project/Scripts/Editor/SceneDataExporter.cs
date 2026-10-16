using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MurderMystery.Core;
using MurderMystery.Investigation;
using MurderMystery.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MurderMystery.EditorTools
{
    /// <summary>
    /// Writes the built manor scene out as plain JSON under docs/data/ so it can be reviewed and diffed without
    /// opening Unity: furniture, clue objects, investigation spots, locks, private rooms, doors, building pieces,
    /// a ground height grid and the results of every scene check. One record per line, positions in metres
    /// rounded to centimetres. See docs/data/README.md.
    /// </summary>
    public static class SceneDataExporter
    {
        private const string ScenePath = "Assets/_Project/Scenes/Manor_KHS.unity";
        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../docs/data"));

        // Area the ground grid and the floating survey cover (the walled compound and its surroundings).
        private const float MinX = -44f, MaxX = 40f, MinZ = -58f, MaxZ = 44f;

        [MenuItem("Tools/Murder Mystery/Export Scene Data")]
        public static string ExportAll()
        {
            OpenManor();
            Physics.SyncTransforms();
            Directory.CreateDirectory(OutDir);
            var counts = new Dictionary<string, int>
            {
                ["props"] = Write("props.json", Props()),
                ["clue_objects"] = Write("clue_objects.json", ClueObjects()),
                ["spots"] = Write("spots.json", Spots()),
                ["locks"] = Write("locks.json", Locks()),
                ["rooms"] = Write("rooms.json", Rooms()),
                ["doors"] = Write("doors.json", Doors()),
                ["buildings"] = Write("buildings.json", Buildings()),
            };
            File.WriteAllText(Path.Combine(OutDir, "ground.json"), Ground());
            var checks = RunAllChecks();
            var summary = $"exported to {OutDir}\n" + string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}")) + "\n" + checks;
            Debug.Log(summary);
            return summary;
        }

        /// <summary>Runs every scene and data check, writes docs/data/checks.json and returns a short summary.</summary>
        [MenuItem("Tools/Murder Mystery/Run All Checks")]
        public static string RunAllChecks()
        {
            OpenManor();
            Physics.SyncTransforms();
            Directory.CreateDirectory(OutDir);

            var data = GameData.LoadFromResources();
            var issues = DataValidator.Validate(data);
            var walk = Lines(KhsManorBuilder.VerifyWalkability());
            var pockets = Lines(KhsManorBuilder.VerifyPockets());
            var doors = Lines(KhsManorBuilder.VerifyDoors());
            var duck = Lines(KhsManorBuilder.VerifyDuckOnly());
            var (floating, buried) = SurveyGrounding();
            var furniture = Props().Where(p => p.Contains("\"flag\"")).ToList();
            var hidden = HiddenClues();

            var walkBad = walk.Where(l => !l.StartsWith("OK") && !l.StartsWith("ok")).ToList();
            var doorBad = doors.Where(l => !l.TrimStart().StartsWith("OK")).ToList();
            var sb = new StringBuilder("{\n");
            sb.Append($"  \"summary\": {{\"dataIssues\": {issues.Count}, \"walkProblems\": {walkBad.Count}, \"pockets\": {Q(pockets.FirstOrDefault() ?? "")}, " +
                      $"\"doorsNotOk\": {doorBad.Count}, \"floating\": {floating.Count}, \"buried\": {buried.Count}, \"furnitureFlags\": {furniture.Count}, \"hiddenClues\": {hidden.Count}}},\n");
            sb.Append("  \"dataIssues\": " + Arr(issues.Select(Q)) + ",\n");
            sb.Append("  \"walkability\": " + Arr(walk.Select(Q)) + ",\n");
            sb.Append("  \"pockets\": " + Arr(pockets.Select(Q)) + ",\n");
            sb.Append("  \"doors\": " + Arr(doors.Select(Q)) + ",\n");
            sb.Append("  \"duckOnly\": " + Arr(duck.Select(Q)) + ",\n");
            sb.Append("  \"floating\": " + Arr(floating) + ",\n");
            sb.Append("  \"buried\": " + Arr(buried) + ",\n");
            sb.Append("  \"furnitureFlags\": " + Arr(furniture) + ",\n");
            sb.Append("  \"hiddenClues\": " + Arr(hidden) + "\n}\n");
            File.WriteAllText(Path.Combine(OutDir, "checks.json"), sb.ToString());

            return $"data issues {issues.Count}; walk problems {walkBad.Count}; {pockets.FirstOrDefault()}; doors not OK {doorBad.Count} " +
                   $"(gates and locks are expected); floating {floating.Count}; buried {buried.Count}; furniture flags {furniture.Count}; hidden clues {hidden.Count}";
        }

        // ------------------------------------------------------------------ records

        /// <summary>Furniture with its support and anything solid through it (the furniture audit).</summary>
        private static List<string> Props()
        {
            var list = new List<string>();
            var root = GameObject.Find("Props");
            if (root == null) return list;
            var all = new List<(Transform t, Bounds b)>();
            foreach (Transform h in root.transform)
                if (TryBounds(h, out var bb)) all.Add((h, bb));
            foreach (var (h, b) in all)
            {
                var support = -99f;
                var supportName = "";
                foreach (var hit in Physics.RaycastAll(new Vector3(b.center.x, b.min.y + 0.05f, b.center.z), Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore))
                    if (!hit.collider.transform.IsChildOf(h) && hit.point.y > support) { support = hit.point.y; supportName = hit.collider.name; }
                var gap = b.min.y - support;
                var half = new Vector3(b.extents.x * 0.85f, Mathf.Max(0.02f, b.extents.y * 0.8f - 0.03f), b.extents.z * 0.85f);
                var centre = new Vector3(b.center.x, b.min.y + 0.05f + half.y, b.center.z);
                var hits = Physics.OverlapBox(centre, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)
                    .Where(c => !c.transform.IsChildOf(h) && !c.name.StartsWith("Sill cap") && c.name != "Yard ground")
                    .Select(c => c.name).Distinct().ToList();
                // Other furniture sharing space with this piece (a candlestick standing on a desk only touches it).
                var inner = new Bounds(b.center, b.size - Vector3.one * 0.06f);
                var shared = all.Where(o => o.t != h && o.b.Intersects(inner) && new Bounds(o.b.center, o.b.size - Vector3.one * 0.06f).Intersects(b))
                    .Select(o => o.t.name).ToList();
                var parts = h.Cast<Transform>().Select(c => Regex.Replace(c.name, @" \(\d+\)$", "")).ToList();
                // On a table or a shelf (a small thing whose support is not found) is expected; flag the rest.
                var small = b.size.x < 0.3f && b.size.z < 0.3f;
                string flag = null;
                // Room floors are below 2 m everywhere in the manor; higher means a roof, wall top or loft.
                if (b.min.y > 2.0f && !small) flag = "too high";
                else if (shared.Count > 0) flag = "overlaps furniture";
                else if (hits.Count > 0) flag = "overlaps";
                else if (gap > 0.05f && !small && support > -90f) flag = "floats";
                else if (gap < -0.05f) flag = "sunk";
                list.Add("{" + $"\"name\": {Q(h.name)}, \"parts\": {Arr(parts.Select(Q))}, \"pos\": {V(h.position)}, \"yaw\": {F(h.eulerAngles.y, 0)}, " +
                         $"\"size\": {V(b.size)}, \"bottom\": {F(b.min.y)}, \"top\": {F(b.max.y)}, \"support\": {Q(supportName)}, \"gap\": {F(support < -90f ? 99f : gap)}, " +
                         $"\"through\": {Arr(hits.Select(Q))}, \"sharesSpaceWith\": {Arr(shared.Select(Q))}" + (flag != null ? $", \"flag\": {Q(flag)}" : "") + "}");
            }
            return list;
        }

        private static List<string> ClueObjects()
        {
            var data = GameData.LoadFromResources();
            var spotOf = data.Clues.clues.ToDictionary(c => c.id, c => c.spotId);
            return Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Where(t => t.parent != null && t.parent.name == "Clue objects")
                .OrderBy(t => spotOf.TryGetValue(t.name, out var s) ? s : "").ThenBy(t => t.name)
                .Select(t =>
                {
                    var has = TryBounds(t, out var b);
                    spotOf.TryGetValue(t.name, out var spot);
                    return "{" + $"\"clue\": {Q(t.name)}, \"spot\": {Q(spot ?? "")}, \"pos\": {V(t.position)}, \"size\": {(has ? V(b.size) : "null")}, " +
                           $"\"parts\": {Arr(t.Cast<Transform>().Select(c => Q(c.name)).Distinct())}" + "}";
                }).ToList();
        }

        private static List<string> Spots()
        {
            var data = GameData.LoadFromResources();
            return Object.FindObjectsByType<InvestigationSpot>(FindObjectsSortMode.None).OrderBy(s => s.SpotId).Select(s =>
            {
                var lockData = data.LockOfSpot(s.SpotId);
                return "{" + $"\"id\": {Q(s.SpotId)}, \"name\": {Q(s.DisplayName)}, \"pos\": {V(s.transform.position)}, \"layer\": {Q(s.Layer.ToString())}, " +
                       $"\"radius\": {F(s.InteractRadius)}, \"clues\": {Arr(s.ClueIds.Select(Q))}, \"lock\": {Q(lockData?.id ?? "")}" + "}";
            }).ToList();
        }

        private static List<string> Locks()
        {
            var data = GameData.LoadFromResources();
            return Object.FindObjectsByType<LockedDoor>(FindObjectsSortMode.None).OrderBy(d => d.LockId).ThenBy(d => d.name).Select(d =>
            {
                data.LockById.TryGetValue(d.LockId ?? "", out var l);
                var at = TryBounds(d.transform, out var b) ? b.center : d.transform.position;
                if (d.Leaves.Count > 0 && d.Leaves[0] != null) at = d.Leaves[0].position;
                return "{" + $"\"lock\": {Q(d.LockId)}, \"object\": {Q(d.name)}, \"pos\": {V(at)}, \"leaves\": {d.Leaves.Count}, " +
                       $"\"title\": {Q(l?.title ?? "")}, \"key\": {Q(l?.keyId ?? "")}, \"opensAtPhase\": {Q(l?.opensAtPhase ?? "")}" + "}";
            }).ToList();
        }

        private static List<string> Rooms() =>
            Object.FindObjectsByType<TalkRoomPlace>(FindObjectsSortMode.None).OrderBy(r => r.RoomId).Select(r =>
                "{" + $"\"room\": {Q(r.RoomId)}, \"pos\": {V(r.transform.position)}, \"outside\": {(r.Outside != null ? V(r.Outside.position) : "null")}, " +
                $"\"seats\": {Arr(Enumerable.Range(0, r.SeatCount).Select(i => V(r.Seat(i).position)))}" + "}").ToList();

        /// <summary>Door leaves that swing (the leaf origin is the hinge).</summary>
        private static List<string> Doors() =>
            Object.FindObjectsByType<ProximityDoor>(FindObjectsSortMode.None).OrderBy(d => d.transform.position.x).ThenBy(d => d.transform.position.z).Select(d =>
            {
                TryBounds(d.transform, out var b);
                return "{" + $"\"name\": {Q(d.name)}, \"hinge\": {V(d.transform.position)}, \"yaw\": {F(d.transform.eulerAngles.y, 0)}, \"size\": {V(b.size)}" + "}";
            }).ToList();

        /// <summary>Every piece of the scanned house plus the added steps, as bounds.</summary>
        private static List<string> Buildings()
        {
            var list = new List<string>();
            var house = GameObject.Find("KHS_KimMyeonggwanHouse");
            if (house == null) return list;
            foreach (Transform piece in house.transform)
                if (TryBounds(piece, out var b))
                    list.Add("{" + $"\"name\": {Q(piece.name)}, \"center\": {V(b.center)}, \"size\": {V(b.size)}, \"bottom\": {F(b.min.y)}" + "}");
            var root = GameObject.Find("Manor_KHS");
            foreach (var group in new[] { "StepStones", "DoorSteps", "Barricades", "SealedRooms", "TalkRooms" })
            {
                var g = root != null ? root.transform.Find(group) : null;
                if (g == null) continue;
                foreach (Transform piece in g)
                    if (TryBounds(piece, out var b))
                        list.Add("{" + $"\"group\": {Q(group)}, \"name\": {Q(piece.name)}, \"center\": {V(b.center)}, \"size\": {V(b.size)}, \"bottom\": {F(b.min.y)}" + "}");
            }
            return list;
        }

        /// <summary>Height of the yard ground every metre (null where there is none), rows from MinZ northwards.</summary>
        private static string Ground()
        {
            var ground = GameObject.Find("Yard ground");
            var sb = new StringBuilder("{\n");
            sb.Append($"  \"originX\": {F(MinX)}, \"originZ\": {F(MinZ)}, \"step\": 1, \"columns\": {(int)(MaxX - MinX) + 1}, \"rows\": {(int)(MaxZ - MinZ) + 1},\n");
            sb.Append("  \"note\": \"heights[row][col] is the ground at x = originX + col, z = originZ + row; null = no ground there\",\n");
            sb.Append("  \"heights\": [\n");
            for (var z = MinZ; z <= MaxZ; z += 1f)
            {
                var row = new List<string>();
                for (var x = MinX; x <= MaxX; x += 1f)
                {
                    var y = (float?)null;
                    foreach (var h in Physics.RaycastAll(new Vector3(x, 20f, z), Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore))
                        if (h.collider.gameObject == ground) y = h.point.y;
                    row.Add(y.HasValue ? F(y.Value) : "null");
                }
                sb.Append("    [" + string.Join(",", row) + "]" + (z + 1f <= MaxZ ? "," : "") + "\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        /// <summary>Clue objects lying under or inside a piece of furniture, where a player cannot see them.</summary>
        private static List<string> HiddenClues()
        {
            var furniture = new List<(string name, Bounds b)>();
            var root = GameObject.Find("Props");
            if (root != null)
                foreach (Transform h in root.transform)
                    if (TryBounds(h, out var b)) furniture.Add((h.name, b));
            var list = new List<string>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.parent != null && t.parent.name == "Clue objects"))
            {
                var p = t.position;
                foreach (var (name, b) in furniture)
                    if (p.x > b.min.x + 0.03f && p.x < b.max.x - 0.03f && p.z > b.min.z + 0.03f && p.z < b.max.z - 0.03f &&
                        b.max.y > p.y + 0.15f && b.min.y < p.y + 0.3f)
                        list.Add("{" + $"\"clue\": {Q(t.name)}, \"pos\": {V(p)}, \"under\": {Q(name)}" + "}");
            }
            return list;
        }

        // ------------------------------------------------------------------ grounding survey

        /// <summary>
        /// Building pieces near the ground whose underside touches nothing (floating) and small pieces mostly
        /// under the ground (buried). Door leaves and anything 1.6 m up are skipped.
        /// </summary>
        private static (List<string> floating, List<string> buried) SurveyGrounding()
        {
            var floating = new List<string>();
            var buried = new List<string>();
            var house = GameObject.Find("KHS_KimMyeonggwanHouse");
            var ground = GameObject.Find("Yard ground");
            if (house == null || ground == null) return (floating, buried);
            var pieces = house.transform.Cast<Transform>().ToList();
            var root = GameObject.Find("Manor_KHS");
            foreach (var n in new[] { "StepStones", "DoorSteps" })
            {
                var g = root != null ? root.transform.Find(n) : null;
                if (g != null) pieces.AddRange(g.Cast<Transform>().Where(c => c.GetComponent<Renderer>() != null));
            }

            float GroundAt(float x, float z)
            {
                foreach (var h in Physics.RaycastAll(new Vector3(x, 10f, z), Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore))
                    if (h.collider.gameObject == ground) return h.point.y;
                return -99f;
            }

            foreach (var piece in pieces)
            {
                if (!TryBounds(piece, out var b) || b.min.y > 1.6f) continue;
                if (piece.name.StartsWith("SM_Door") && !piece.name.StartsWith("SM_DoorFrame")) continue;
                var verts = new List<Vector3>();
                foreach (var mf in piece.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue;
                    var m = mf.transform.localToWorldMatrix;
                    verts.AddRange(mf.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v)));
                }
                if (verts.Count == 0) continue;
                var minY = verts.Min(v => v.y);
                var bottom = verts.Where(v => v.y < minY + 0.03f).ToList();
                var step = Mathf.Max(1, bottom.Count / 30);
                var contact = false;
                var minGap = 99f;
                for (var i = 0; i < bottom.Count && !contact; i += step)
                {
                    var v = bottom[i];
                    if (Physics.OverlapSphere(v, 0.035f, ~0, QueryTriggerInteraction.Ignore).Any(c => !c.transform.IsChildOf(piece) && !(c is CharacterController))) { contact = true; break; }
                    if (GroundAt(v.x, v.z) > v.y - 0.01f) { contact = true; break; }
                    // Inside another solid (a stone set into a platform): rays from above and below hit the same collider.
                    var above = new HashSet<Collider>(Physics.RaycastAll(v + Vector3.up * 2f, Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore)
                        .Where(h => !h.collider.transform.IsChildOf(piece)).Select(h => h.collider));
                    if (Physics.RaycastAll(v - Vector3.up * 2f, Vector3.up, 2f, ~0, QueryTriggerInteraction.Ignore).Any(h => above.Contains(h.collider))) { contact = true; break; }
                    foreach (var h in Physics.RaycastAll(v, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                        if (!h.collider.transform.IsChildOf(piece) && v.y - h.point.y < minGap) minGap = v.y - h.point.y;
                }
                if (!contact && minGap > 0.03f)
                    floating.Add("{" + $"\"name\": {Q(piece.name)}, \"center\": {V(b.center)}, \"bottom\": {F(b.min.y)}, \"gap\": {(minGap > 50f ? "null" : F(minGap))}" + "}");
                if (b.size.x < 3f && b.size.z < 3f)
                {
                    var gy = GroundAt(b.center.x, b.center.z);
                    var visible = (b.max.y - gy) / Mathf.Max(0.01f, b.size.y);
                    // Small stones set into the ground along walls (낙수받이, edging) are meant to show only their tops.
                    var setStone = piece.name.StartsWith("SM_Stone01") && b.size.y < 0.4f && visible >= 0.15f;
                    if (gy > -5f && visible < 0.45f && !setStone)
                        buried.Add("{" + $"\"name\": {Q(piece.name)}, \"center\": {V(b.center)}, \"visible\": {F(visible)}, \"ground\": {F(gy)}" + "}");
                }
            }
            return (floating, buried);
        }

        // ------------------------------------------------------------------ helpers

        private static void OpenManor()
        {
            var active = EditorSceneManager.GetActiveScene();
            if (active.path == ScenePath || Application.isPlaying) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }

        private static int Write(string file, List<string> records)
        {
            File.WriteAllText(Path.Combine(OutDir, file), "[\n" + string.Join(",\n", records.Select(r => "  " + r)) + "\n]\n");
            return records.Count;
        }

        private static bool TryBounds(Transform t, out Bounds b)
        {
            b = default;
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return false;
            b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return true;
        }

        private static List<string> Lines(string text) =>
            (text ?? "").Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();

        private static string F(float v, int digits = 2) => v.ToString("F" + digits, CultureInfo.InvariantCulture);
        private static string V(Vector3 v) => $"[{F(v.x)}, {F(v.y)}, {F(v.z)}]";
        private static string Arr(IEnumerable<string> items) => "[" + string.Join(", ", items) + "]";

        private static string Q(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append($"\\u{(int)c:x4}");
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
