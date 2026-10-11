using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MurderMystery.Investigation;
using MurderMystery.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace MurderMystery.EditorTools
{
    /// <summary>
    /// Builds the playable manor from the Korea Heritage Service "Kim Myeong-gwan's House" demo scene
    /// (docs/design/map_layout.md §8). The house keeps its KHS x/z; it is lifted so the yards sit at y = 0.
    /// Rooms of our scenario are mapped onto the real house's buildings in the tables below.
    /// Re-running rebuilds the scene from scratch.
    /// </summary>
    public static class KhsManorBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Manor_KHS.unity";
        private const string DemoPath = KhsAssetTools.Root + "/KimMyeonggwanHouse/Scene/Demo.unity";
        private const string GroundMatPath = KhsAssetTools.Root + "/KimMyeonggwanHouse/Material/MI_Ground01A.mat";

        // KHS yard level. Floors of the halls are ~1.2 m above it, stone platforms (gidan) 0.6 m.
        private const float KhsGroundY = -2.0f;
        // Door leaves whose bottom is below this (yard level, after lifting) are gates and stay solid.
        private const float GateBottomMaxY = 0.25f;

        // Shorter body than the grey-box: front doors are 1.74 m above a 0.12 m sill, and Unity's stepping
        // needs height + stepOffset + skin under the lintel, so stepOffset only covers sills. The 0.6~0.9 m
        // stone platforms are climbed by StepAssist (assisted step), with extra stones where it is higher.
        private const float BodyHeight = 1.45f;
        private const float BodyRadius = 0.25f;
        private const float BodyStep = 0.15f;
        // Not higher: a few garden walls are 0.63 m and must stay unclimbable.
        private const float AssistStep = 0.62f;
        private const float EyeHeight = 1.33f;

        private struct SpotDef
        {
            public string Id, Name;
            public float X, Z, Lift;
            public SpotLayer Layer;
            public string[] Clues;
        }

        private static SpotDef S(string id, string name, float x, float z, float lift, SpotLayer layer, params string[] clues) =>
            new SpotDef { Id = id, Name = name, X = x, Z = z, Lift = lift, Layer = layer, Clues = clues };

        // Investigation spots (ids and clues as in the grey-box; positions mapped onto the KHS buildings).
        // Room mapping (docs/design/map_layout.md §8):
        //  사랑채 x 4.6~17, z -8.3~-14.2, faces south. 사랑방 = big west room (x 4.6~9.4), 서재 = room behind the
        //  hall (x 9.5~12.2, z -8.3~-10.1, door to the hall and a door to 사랑방), 대청 = hall (x 9.5~15,
        //  z -10.1~-12.7), 작은사랑 = east end room (x 15~17), 툇마루 = z -12.8~-14.1, 섬돌 at z -14.6.
        //  안채: 부엌 = west wing (x -19~-16, z 0~7.6), 곳간 = south-row bay with the jars (x -13.8~-11),
        //  중문간 = south-row bay x -8~-2.5, 건넌방 = east arm of the north wing (x -3.7~1, z 6.6~15).
        //  손님방 (grey-box 별당): the two wood-floored rooms at the east end of the 행랑채 long wing, facing
        //  사랑마당 — 허 의원 x 8.7~11.2, 변 행수 x 11.2~13.6 (z -26.8~-29.9). The east arm beyond is an
        //  earth-floored barn. 뒷간 = walled hut at (20.3, -29.2).
        private static readonly SpotDef[] Spots =
        {
            S("01", "서재 서안", 11.2f, -8.9f, 0.45f, SpotLayer.Surface, "N1"),
            S("02", "서재 문갑", 9.9f, -8.7f, 0.6f, SpotLayer.Surface, "C4", "D1", "D2"),
            S("03", "서재 문지방", 10.8f, -10.2f, 0.1f, SpotLayer.Surface, "C3b"),
            S("04", "사랑방 화로", 7.2f, -10.6f, 0.45f, SpotLayer.Surface, "C1"),
            S("05", "사랑방 소반", 6.2f, -10.0f, 0.45f, SpotLayer.Surface, "E1"),
            S("06", "사랑방 서랍", 5.0f, -9.0f, 0.7f, SpotLayer.Surface, "C5"),
            S("07", "대청·툇마루 바닥", 12.2f, -13.4f, 0.1f, SpotLayer.Surface, "C3a"),
            S("08a", "작은사랑 앞 섬돌", 13.5f, -14.6f, 0.1f, SpotLayer.Surface, "C3c"),
            S("08b", "사랑방 앞 섬돌", 8.0f, -14.6f, 0.1f, SpotLayer.Surface, "C3c"),
            S("09", "등잔·빈 기름병", 14.4f, -13.4f, 0.3f, SpotLayer.Surface, "N3"),
            S("10", "작은사랑 서가", 16.3f, -10.6f, 0.8f, SpotLayer.Deep, "H4", "C7"),
            S("11", "곳간 쥐약 단지", -11.9f, -1.1f, 0.4f, SpotLayer.Surface, "C2"),
            S("12", "부엌 약탕관", -17.5f, 6.2f, 0.5f, SpotLayer.Surface, "N2"),
            S("13", "부엌 쌀독", -17.5f, 3.0f, 0.4f, SpotLayer.Deep, "H1"),
            S("14", "건넌방 반닫이", -1.5f, 9.8f, 0.6f, SpotLayer.Deep, "H3"),
            S("15", "허 의원 약상자", 9.6f, -28.0f, 0.4f, SpotLayer.Surface, "C6"),
            S("16", "허 의원 봇짐", 10.3f, -28.9f, 0.4f, SpotLayer.Deep, "H6"),
            S("17", "변 행수 짐 보따리", 12.6f, -28.6f, 0.4f, SpotLayer.Deep, "H5a", "H5b"),
            S("18", "손님방 툇마루 진신", 11.2f, -26.2f, 0.1f, SpotLayer.Surface, "C8"),
        };

        // Extra stepping stones (centre x, z, width along x, depth along z, top height) where the scan has a
        // platform edge higher than the body can step and no stone in front of it.
        private static readonly Vector4[] StepStones =
        {
            new Vector4(10.0f, -25.6f, 1.4f, 0.7f), // 허 의원 방 (행랑채), gidan 0.85 m
            new Vector4(12.4f, -25.6f, 1.4f, 0.7f), // 변 행수 방
        };
        private const float StepStoneTop = 0.43f;

        private static readonly Vector2 Discussion = new Vector2(8f, -18.5f);
        private static readonly Vector2 NpcDaecheong = new Vector2(12.3f, -11.4f);
        private static readonly Vector2 NpcStairs = new Vector2(10.7f, -13.6f);

        [MenuItem("Tools/Manor/Build KHS Manor Scene")]
        public static void BuildScene()
        {
            if (!File.Exists(DemoPath))
            {
                Debug.LogError($"[KhsManorBuilder] {DemoPath} not found. Import the KHS package first (docs/design/asset_plan.md).");
                return;
            }
            KhsAssetTools.ConvertMaterialsCount();
            ManorBlockoutBuilder.EnsureFolder("Assets/_Project/Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var house = ImportHouse(scene);
            house.position = new Vector3(0f, -KhsGroundY, 0f);
            FixColliders(house);
            Physics.SyncTransforms();

            var root = new GameObject("Manor_KHS").transform;
            BuildGround(Group(root, "Ground"));
            BuildStepStones(Group(root, "StepStones"));
            BuildBlockers(Group(root, "Blockers"));
            Physics.SyncTransforms();
            BuildSpots(Group(root, "InvestigationSpots"));
            BuildMarkers(Group(root, "GameplayMarkers"));

            var profile = new GameObject("PlayerBodyProfile").AddComponent<PlayerBodyProfile>();
            profile.height = BodyHeight;
            profile.radius = BodyRadius;
            profile.stepOffset = BodyStep;
            profile.assistStep = AssistStep;
            profile.eyeHeight = EyeHeight;

            ManorBlockoutBuilder.BuildLightAndCamera();
            NetworkSceneSetup.Apply();

            EditorSceneManager.SaveScene(scene, ScenePath);
            ManorBlockoutBuilder.AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[KhsManorBuilder] Saved {ScenePath}");
        }

        // ---------------------------------------------------------------- house

        private static Transform ImportHouse(Scene target)
        {
            var demo = EditorSceneManager.OpenScene(DemoPath, OpenSceneMode.Additive);
            var house = new GameObject("KHS_KimMyeonggwanHouse").transform;
            foreach (var go in demo.GetRootGameObjects())
            {
                // The demo's camera, light and its 2 m-thick ground slab (which sits 1.5 m below the yards).
                if (go.GetComponent<Camera>() != null || go.GetComponent<Light>() != null || go.name == "Cube") continue;
                SceneManager.MoveGameObjectToScene(go, target);
                go.transform.SetParent(house, true);
            }
            EditorSceneManager.CloseScene(demo, true);
            return house;
        }

        /// <summary>
        /// The KHS mesh colliders sit on the prefab roots with no mesh (the reference does not survive import),
        /// so they are rebuilt on the child that renders. Room door leaves (raised on a floor) get no collider so
        /// rooms can be entered; leaves standing on the yard are gates (outer gates, shrine) and stay shut.
        /// </summary>
        private static void FixColliders(Transform house)
        {
            foreach (var broken in house.GetComponentsInChildren<MeshCollider>(true).Where(c => c.sharedMesh == null).ToList())
                Object.DestroyImmediate(broken);
            foreach (var mf in house.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                if (IsDoorLeaf(mf.transform) && mf.GetComponent<Renderer>().bounds.min.y > GateBottomMaxY) continue;
                if (!mf.TryGetComponent<MeshCollider>(out var mc)) mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
        }

        private static bool IsDoorLeaf(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.name.StartsWith("SM_Door") && !t.name.StartsWith("SM_DoorFrame")) return true;
            return false;
        }

        private static void BuildGround(Transform p)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Yard ground";
            go.transform.SetParent(p, false);
            go.transform.localScale = new Vector3(140f, 0.2f, 150f);
            go.transform.position = new Vector3(0f, -0.1f, -6f);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(GroundMatPath);
            if (mat != null)
            {
                var tiled = new Material(mat) { name = "Yard ground (tiled)" };
                tiled.mainTextureScale = new Vector2(35f, 37f);
                tiled.SetFloat("_Smoothness", 0.08f);
                tiled.SetFloat("_Metallic", 0f);
                const string path = "Assets/_Project/Art/Materials/KhsYardGround.mat";
                ManorBlockoutBuilder.EnsureFolder("Assets/_Project/Art/Materials");
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(tiled, path);
                go.GetComponent<MeshRenderer>().sharedMaterial = tiled;
            }
        }

        private static void BuildStepStones(Transform p)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(KhsAssetTools.Root + "/KimMyeonggwanHouse/Material/MI_GidanStone01A.mat");
            foreach (var s in StepStones)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Step stone (섬돌)";
                go.transform.SetParent(p, false);
                go.transform.localScale = new Vector3(s.z, StepStoneTop, s.w);
                go.transform.position = new Vector3(s.x, StepStoneTop / 2f, s.y);
                if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        private static void BuildBlockers(Transform p)
        {
            foreach (var b in Blockers)
            {
                var go = new GameObject("Blocker");
                go.transform.SetParent(p, false);
                go.transform.position = new Vector3(b.x, 1.5f, b.y);
                go.AddComponent<BoxCollider>().size = new Vector3(b.z, 3f, b.w);
            }
        }

        // ---------------------------------------------------------------- gameplay

        private static void BuildSpots(Transform p)
        {
            foreach (var s in Spots)
            {
                var go = new GameObject($"Spot_{s.Id} ({s.Name})");
                go.transform.SetParent(p, false);
                go.transform.position = new Vector3(s.X, FloorY(s.X, s.Z) + s.Lift, s.Z);
                go.AddComponent<InvestigationSpot>().Configure(s.Id, s.Name, s.Layer, s.Clues);
                var col = go.AddComponent<SphereCollider>();
                col.isTrigger = true;
                col.radius = 1.2f;
            }
        }

        private static void BuildMarkers(Transform p)
        {
            Marker(p, "NPC_DoGeomnyul_Daecheong", MarkerType.NpcPoint, NpcDaecheong, 0.5f);
            Marker(p, "NPC_DoGeomnyul_Stairs", MarkerType.NpcPoint, NpcStairs, 0.5f);
            Marker(p, "DiscussionArea", MarkerType.DiscussionArea, Discussion, 4f);
            for (var i = 0; i < 5; i++)
            {
                var a = Mathf.PI / 2f + i * Mathf.PI * 2f / 5f;
                Marker(p, $"PlayerSpawn_{i}", MarkerType.PlayerSpawn,
                    Discussion + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 3f, 0.4f);
            }

            var npc = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            npc.name = "NPC_Placeholder (도 검률)";
            npc.transform.SetParent(p, false);
            Object.DestroyImmediate(npc.GetComponent<Collider>());
            npc.transform.localScale = new Vector3(0.5f, 0.8f, 0.5f);
            npc.transform.position = new Vector3(NpcDaecheong.x, FloorY(NpcDaecheong.x, NpcDaecheong.y) + 0.8f, NpcDaecheong.y);
        }

        private static void Marker(Transform p, string id, MarkerType type, Vector2 xz, float radius)
        {
            var go = new GameObject(id);
            go.transform.SetParent(p, false);
            go.transform.position = new Vector3(xz.x, FloorY(xz.x, xz.y), xz.y);
            go.AddComponent<GameplayMarker>().Configure(type, id, radius);
        }

        /// <summary>Highest walkable surface under a point, searched below ceiling height.</summary>
        private static float FloorY(float x, float z)
        {
            var hits = Physics.RaycastAll(new Vector3(x, 2.6f, z), Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore);
            var floor = hits.Where(h => h.normal.y > 0.7f).OrderBy(h => h.distance).FirstOrDefault();
            return floor.collider != null ? floor.point.y : 0f;
        }

        private static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        // ---------------------------------------------------------------- verification

        /// <summary>
        /// Bakes a temporary NavMesh with the player's body and checks that every spot, NPC point and spawn
        /// can be walked to from the discussion area. Writes Library/khs_walk.png (green = walkable,
        /// red = unreachable spot, blue = reachable spot) and returns a text report.
        /// </summary>
        public static string VerifyWalkability() => VerifyWalkability(0f, -4f, 42f);

        public static string VerifyWalkability(float mapX, float mapZ, float mapHalf)
        {
            Physics.SyncTransforms();
            var bounds = new Bounds(new Vector3(0f, 2f, -6f), new Vector3(120f, 12f, 140f));
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            sources.RemoveAll(s => s.component != null && s.component.GetComponent<Collider>() is Collider c && c.isTrigger);
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentHeight = BodyHeight;
            settings.agentRadius = BodyRadius;
            settings.agentClimb = AssistStep;
            settings.agentSlope = 40f;
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            var instance = NavMesh.AddNavMeshData(data);
            try
            {
                var sb = new StringBuilder();
                var start = SampleOrNull(GameObject.Find("DiscussionArea").transform.position);
                if (start == null) return "discussion area is not on the NavMesh";
                var targets = Object.FindObjectsByType<InvestigationSpot>(FindObjectsSortMode.None).Select(s => (s.name, s.transform.position))
                    .Concat(Object.FindObjectsByType<GameplayMarker>(FindObjectsSortMode.None).Select(m => (m.name, m.transform.position)))
                    .OrderBy(t => t.name).ToList();
                var results = new List<(Vector3 pos, bool ok)>();
                foreach (var (name, pos) in targets)
                {
                    var on = SampleOrNull(pos);
                    var path = new NavMeshPath();
                    var ok = on != null && NavMesh.CalculatePath(start.Value, on.Value, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                    results.Add((pos, ok));
                    sb.AppendLine($"{(ok ? "OK  " : "FAIL")} {name} pos={pos:F1} nav={(on?.ToString("F1") ?? "none")} len={(ok ? PathLength(path).ToString("F1") : "-")}");
                }
                // Must stay unreachable: outside the compound and the forbidden outer yard / shrine.
                foreach (var (name, xz) in Forbidden)
                {
                    var on = SampleOrNull(new Vector3(xz.x, FloorY(xz.x, xz.y), xz.y));
                    var path = new NavMeshPath();
                    var leak = on != null && NavMesh.CalculatePath(start.Value, on.Value, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                    sb.AppendLine($"{(leak ? "LEAK" : "ok  ")} forbidden {name} {xz}" +
                                  (leak ? " via " + string.Join(" ", path.corners.Select(c => $"({c.x:F1},{c.z:F1})")) : ""));
                    if (leak) results.Add((new Vector3(xz.x, 0f, xz.y), false));
                }
                RenderWalkMap(NavMesh.CalculateTriangulation(), results, new Vector2(mapX, mapZ), mapHalf);
                return sb.ToString();
            }
            finally
            {
                NavMesh.RemoveNavMeshData(instance);
            }
        }

        // Points players must not reach (checked by VerifyWalkability).
        private static readonly (string, Vector2)[] Forbidden =
        {
            ("outside south", new Vector2(0f, -45f)),
            ("outside west", new Vector2(-45f, 0f)),
            ("outside east", new Vector2(40f, 0f)),
            ("outside north", new Vector2(0f, 45f)),
            ("바깥마당 (outside the 중문)", new Vector2(-9f, -18f)),
            ("사당", new Vector2(12f, 22f)),
        };

        // Invisible walls (centre x, z, size x, size z): the outer yard (바깥마당), which 도 검률 has closed
        // (the 중문 and the gap beside the thatched shed), and a side gate with no door leaf.
        private static readonly Vector4[] Blockers =
        {
            new Vector4(-2.1f, -17.2f, 0.6f, 2.4f),
            new Vector4(-13.6f, -16.4f, 5.2f, 2.8f), // gap between the thatched shed and the 행랑채 west arm
            new Vector4(18.8f, 27.9f, 1.4f, 2.6f),   // east side gate by the shrine, out to the thatched barn
        };

        private static Vector3? SampleOrNull(Vector3 p) =>
            NavMesh.SamplePosition(p, out var hit, 1.2f, NavMesh.AllAreas) ? hit.position : (Vector3?)null;

        private static float PathLength(NavMeshPath path)
        {
            var len = 0f;
            for (var i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return len;
        }

        private static void RenderWalkMap(NavMeshTriangulation tri, List<(Vector3 pos, bool ok)> points, Vector2 center, float half)
        {
            const int size = 1600;

            var camGo = new GameObject("WalkMapCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = half;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.white;
            cam.farClipPlane = 200f;
            camGo.transform.SetPositionAndRotation(new Vector3(center.x, 60f, center.y), Quaternion.Euler(90f, 0f, 0f));
            // Hide roofs and anything above head height so rooms are visible.
            var hidden = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.bounds.min.y > 1.9f).ToList();
            hidden.ForEach(r => r.enabled = false);
            var rt = new RenderTexture(size, size, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            RenderTexture.active = null;
            cam.targetTexture = null;
            hidden.ForEach(r => r.enabled = true);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(rt);

            Vector2 ToPx(Vector3 w) => new Vector2((w.x - center.x + half) / (2f * half) * size, (w.z - center.y + half) / (2f * half) * size);
            var px = tex.GetPixels32();
            for (var i = 0; i + 2 < tri.indices.Length; i += 3)
            {
                Vector2 a = ToPx(tri.vertices[tri.indices[i]]), b = ToPx(tri.vertices[tri.indices[i + 1]]), c = ToPx(tri.vertices[tri.indices[i + 2]]);
                var minX = Mathf.Max(0, (int)Mathf.Min(a.x, Mathf.Min(b.x, c.x)));
                var maxX = Mathf.Min(size - 1, (int)Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + 1);
                var minY = Mathf.Max(0, (int)Mathf.Min(a.y, Mathf.Min(b.y, c.y)));
                var maxY = Mathf.Min(size - 1, (int)Mathf.Max(a.y, Mathf.Max(b.y, c.y)) + 1);
                for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    if (!Inside(p, a, b, c)) continue;
                    var o = px[y * size + x];
                    px[y * size + x] = new Color32((byte)(o.r / 2), (byte)((o.g + 255) / 2), (byte)(o.b / 2), 255);
                }
            }
            foreach (var (pos, ok) in points)
            {
                var q = ToPx(pos);
                for (var dy = -6; dy <= 6; dy++)
                for (var dx = -6; dx <= 6; dx++)
                {
                    int x = (int)q.x + dx, y = (int)q.y + dy;
                    if (x < 0 || y < 0 || x >= size || y >= size || dx * dx + dy * dy > 36) continue;
                    px[y * size + x] = ok ? new Color32(30, 60, 230, 255) : new Color32(230, 30, 30, 255);
                }
            }
            // 5 m grid (darker every 10 m) and door leaves (orange) for reading coordinates off the image.
            for (var g = -60; g <= 60; g += 5)
            {
                var shade = g % 10 == 0 ? (byte)90 : (byte)170;
                var gx = (int)ToPx(new Vector3(g, 0f, 0f)).x;
                var gy = (int)ToPx(new Vector3(0f, 0f, g)).y;
                for (var i = 0; i < size; i++)
                {
                    if (gx >= 0 && gx < size) px[i * size + gx] = new Color32(shade, shade, shade, 255);
                    if (gy >= 0 && gy < size) px[gy * size + i] = new Color32(shade, shade, shade, 255);
                }
            }
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(m => IsDoorLeaf(m.transform)))
            {
                var q = ToPx(mf.GetComponent<Renderer>().bounds.center);
                for (var dy = -3; dy <= 3; dy++)
                for (var dx = -3; dx <= 3; dx++)
                {
                    int x = (int)q.x + dx, y = (int)q.y + dy;
                    if (x >= 0 && y >= 0 && x < size && y < size) px[y * size + x] = new Color32(255, 140, 0, 255);
                }
            }
            tex.SetPixels32(px);
            File.WriteAllBytes("Library/khs_walk.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        private static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float Cross(Vector2 u, Vector2 v, Vector2 w) => (v.x - u.x) * (w.y - u.y) - (v.y - u.y) * (w.x - u.x);
            var d1 = Cross(a, b, p);
            var d2 = Cross(b, c, p);
            var d3 = Cross(c, a, p);
            var neg = d1 < 0 || d2 < 0 || d3 < 0;
            var pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }
}
