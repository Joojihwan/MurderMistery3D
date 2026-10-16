using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MurderMystery.Core;
using MurderMystery.Gameplay;
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
        //  별당 (the guests' rooms; the scenario keeps the name): the two wood-floored rooms at the east end
        //  of the 행랑채 long wing, facing 사랑마당 — 허 의원 x 8.7~11.2, 변 행수 x 11.2~13.6
        //  (z -26.8~-29.9). The east arm beyond is an earth-floored barn. 뒷간 = walled hut at (20.3, -29.2).
        private static readonly SpotDef[] Spots =
        {
            S("01", "서재 서안", 11.2f, -8.9f, 0.45f, SpotLayer.Surface, "N1"),
            S("02", "서재 문갑", 9.9f, -8.7f, 0.6f, SpotLayer.Surface, "C4", "D1", "D2"),
            S("03", "서재 문지방", 10.8f, -10.2f, 0.1f, SpotLayer.Surface, "C3b"),
            S("04", "사랑방 화로", 7.2f, -10.6f, 0.45f, SpotLayer.Surface, "C1"),
            S("05", "사랑방 소반", 6.2f, -10.0f, 0.45f, SpotLayer.Surface, "E1"),
            S("06", "사랑방 서랍", 5.0f, -9.0f, 0.7f, SpotLayer.Surface, "C5"),
            S("07", "대청·툇마루 바닥", 11.0f, -13.4f, 0.1f, SpotLayer.Surface, "C3a"),
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
            S("18", "별당 툇마루 진신", 11.2f, -26.2f, 0.1f, SpotLayer.Surface, "C8"),
            // v2 (docs/design/story_v2.md): keys, locked places, misleading and meaningless finds.
            // A negative lift measures from the lowest surface (under a raised floor) instead of the top one.
            S("19", "사랑방 의걸이장 (대감 도포)", 8.7f, -9.0f, 1.0f, SpotLayer.Surface, "K1", "T19"),
            S("20", "사당 앞 제상", 12.6f, 19.8f, 0.5f, SpotLayer.Surface, "S1"),
            S("21", "사당 향로", 11.6f, 19.8f, 0.4f, SpotLayer.Surface, "S2"),
            S("22", "툇마루 끝 마루 밑", 5.0f, -13.7f, -0.15f, SpotLayer.Surface, "X1"),
            S("23", "부엌 뒤 수채", -20.5f, 4.5f, -0.1f, SpotLayer.Surface, "X2"),
            S("24", "부엌 횃대", -16.6f, 5.6f, 1.2f, SpotLayer.Surface, "X3"),
            S("25", "대청 앞 마루 밑", 9.8f, -13.9f, -0.15f, SpotLayer.Deep, "X4"),
            S("26", "건넌방 경대", -0.2f, 12.5f, 0.6f, SpotLayer.Deep, "X5"),
            S("27", "우물가", -17.4f, 20.4f, 0.2f, SpotLayer.Surface, "M1"),
            S("28", "장독대", -14.3f, 21.8f, 0.6f, SpotLayer.Surface, "M2"),
            S("29", "뒷간 앞", 19.0f, -28.4f, 0.1f, SpotLayer.Surface, "M3"),
            S("30", "대청 구석", 14.0f, -10.8f, 0.2f, SpotLayer.Surface, "M4"),
            S("31", "안마당 디딤돌", -6.0f, 4.0f, 0.1f, SpotLayer.Surface, "M5"),
            S("32", "서재 뒤 창", 10.9f, -7.6f, 1.1f, SpotLayer.Surface, "M6"),
            S("33", "곳간 궤짝", -13.0f, -2.2f, 0.5f, SpotLayer.Surface, "L1", "L2"),
            S("34", "부엌 찬장", -18.4f, 1.4f, 1.0f, SpotLayer.Surface, "K2", "M7"),
            S("35", "작은사랑 의걸이장 (재하 도포)", 15.6f, -13.0f, 1.0f, SpotLayer.Surface, "M8"),
        };

        private static readonly Vector2 Discussion = new Vector2(8f, -18.5f);
        // 도 검률 keeps to the 툇마루 in front of the 대청 so he is seen from the yard (the hall doors are shut).
        private static readonly Vector2 NpcDaecheong = new Vector2(12.6f, -13.3f);
        private static readonly Vector2 NpcStairs = new Vector2(12.6f, -14.6f);

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

            var ceilings = new List<GroundSample>();
            var samples = FindGroundSamples(house, ceilings);
            var root = new GameObject("Manor_KHS").transform;
            BuildGround(Group(root, "Ground"), GroundHeights(samples, ceilings, out var outliers));
            Physics.SyncTransforms();
            Debug.Log($"[KhsManorBuilder] ground rebuilt from {samples.Count - outliers} samples ({outliers} stones off the ground ignored)");
            Debug.Log($"[KhsManorBuilder] set {SettleLoosePieces(house)} loose stones/jars on the ground");
            Debug.Log($"[KhsManorBuilder] {AddMissingPostStones(Group(root, "PostStones"), house)} foundation stones under posts that stopped short");
            Physics.SyncTransforms();
            Debug.Log($"[KhsManorBuilder] {BuildDoorSteps(Group(root, "DoorSteps"), house)} stepping stones in front of high doors");
            BuildBlockers(Group(root, "Blockers"));
            BuildLockedGates(Group(root, "Locks"), house);
            Physics.SyncTransforms();
            BuildTalkRooms(Group(root, "TalkRooms"), house);
            BuildSealedRooms(Group(root, "SealedRooms"), house);
            BuildPartitions(Group(root, "Partitions"));
            Physics.SyncTransforms();
            BuildSpots(Group(root, "InvestigationSpots"));
            BuildMarkers(Group(root, "GameplayMarkers"));
            BuildProps(Group(root, "Props"));

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
        /// rooms and inner gates can be passed (a ProximityDoor hides them while someone is near); the gates in
        /// <see cref="LockedGates"/> stay shut.
        /// </summary>
        private static void FixColliders(Transform house)
        {
            foreach (var broken in house.GetComponentsInChildren<MeshCollider>(true).Where(c => c.sharedMesh == null).ToList())
                Object.DestroyImmediate(broken);
            foreach (var mf in house.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                if (IsDoorLeaf(mf.transform) && !IsLockedGate(mf.GetComponent<Renderer>().bounds.center))
                {
                    // Opens (hides) while someone is near, so nobody is seen walking through a shut door.
                    var leaf = DoorLeafRoot(mf.transform);
                    if (!leaf.TryGetComponent<ProximityDoor>(out _)) leaf.gameObject.AddComponent<ProximityDoor>();
                    continue;
                }
                if (!mf.TryGetComponent<MeshCollider>(out var mc)) mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
        }

        // Gates that stay shut (centre x, z, lock in clues.json "locks"): the compound's outer gates and the
        // shrine, which its key opens. Every other door leaf, room door or inner gate, opens for whoever comes near.
        private static readonly (Vector2 at, string lockId)[] LockedGates =
        {
            (new Vector2(-38.0f, 0.7f), "westgate"),
            (new Vector2(11.8f, 14.7f), "shrine"),     // 사당: 사당 열쇠
            (new Vector2(22.6f, 30.1f), "barn"),       // north-east gates to the barn yard
            (new Vector2(23.4f, 34.0f), "barn"),
            (new Vector2(-2.05f, -17.4f), "outer"),    // 중문 to the outer yard (also behind a blocker)
        };

        private static bool IsLockedGate(Vector3 leafCenter) => LockedGateAt(leafCenter) != null;

        private static string LockedGateAt(Vector3 leafCenter) =>
            LockedGates.FirstOrDefault(g => Vector2.Distance(g.at, new Vector2(leafCenter.x, leafCenter.z)) < 1.2f).lockId;

        /// <summary>One <see cref="LockedDoor"/> per lock, holding the leaves of its gates (they keep their colliders).</summary>
        private static void BuildLockedGates(Transform p, Transform house)
        {
            var leaves = new Dictionary<string, List<Transform>>();
            foreach (var mf in house.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!IsDoorLeaf(mf.transform)) continue;
                var id = LockedGateAt(mf.GetComponent<Renderer>().bounds.center);
                if (id == null) continue;
                var leaf = DoorLeafRoot(mf.transform);
                if (!leaves.TryGetValue(id, out var list)) leaves[id] = list = new List<Transform>();
                if (!list.Contains(leaf)) list.Add(leaf);
            }
            foreach (var pair in leaves)
            {
                var go = new GameObject($"Lock ({pair.Key})");
                go.transform.SetParent(p, false);
                go.AddComponent<LockedDoor>().Configure(pair.Key, pair.Value.ToArray());
            }
        }

        private static Transform DoorLeafRoot(Transform t)
        {
            Transform root = null;
            for (; t != null; t = t.parent)
                if (t.name.StartsWith("SM_Door") && !t.name.StartsWith("SM_DoorFrame")) root = t;
            return root;
        }

        private static bool IsDoorLeaf(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.name.StartsWith("SM_Door") && !t.name.StartsWith("SM_DoorFrame")) return true;
            return false;
        }

        // Ground mesh: 140 x 150 m around the compound, 0.5 m cells.
        private static readonly Rect GroundArea = new Rect(-70f, -81f, 140f, 150f);
        private const float GroundCell = 0.5f;
        // How far a ground sample shapes the land around it; beyond that the yard eases back to y = 0.
        private const float GroundReach = 5f;
        // Ground is laid this far above the base of whatever stands on it, so no seam shows.
        private const float GroundEmbed = 0.02f;
        // A loose stone this far off the ground the built parts around it imply was not lying on the ground
        // (it sat on a wall, a platform or a pile) and does not shape it.
        private const float LooseOutlier = 0.3f;
        // Relaxation passes over the ground grid (each spreads a lift by about one cell).
        private const int GroundSmoothing = 12;

        // Built parts that stand on the ground: the land must reach up to them (they never move).
        private static readonly string[] Structures =
            { "SM_Pillar", "SM_FoundationStone", "SM_StoneWall", "SM_Stonewall", "SM_Wall", "SM_PinkWall", "SM_Gidan",
              "SM_Stone_Tile", "SM_chimney", "SM_Chimney", "SM_Fireplace", "SM_DoorFrame", "SM_GateDoorFrame",
              "SM_Stone01C_Splines", "SM_Stone01D_Splines" };

        // Loose things lying on the ground: they show where the scanned ground was, and are set back down on
        // the rebuilt ground afterwards (stacked pieces move together).
        private static readonly string[] LoosePieces =
            { "SM_Stone", "SM_Jangdokdae", "SM_Gamasot", "SM_Seokkalae", "SM_SteppingWood" };

        private static bool IsStructure(string name) => Structures.Any(name.StartsWith);

        // Timber and plaster parts usually stand on something (a sill, a platform) rather than the soil; where
        // the scan leaves a gap under them they only mark the ground if no stonework nearby sits lower.
        private static readonly string[] Superstructure = { "SM_Wall", "SM_PinkWall", "SM_DoorFrame", "SM_GateDoorFrame" };

        private static bool IsLoose(string name) => !IsStructure(name) && LoosePieces.Any(name.StartsWith);

        /// <summary>A loose stone with a post standing on it is that post's foundation stone: it stays put.</summary>
        private static bool CarriesStructure(Transform piece)
        {
            var renderers = piece.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return false;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            var half = new Vector3(b.extents.x * 0.6f, 0.12f, b.extents.z * 0.6f);
            return Physics.OverlapBox(new Vector3(b.center.x, b.max.y, b.center.z), half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)
                .Any(c => !c.transform.IsChildOf(piece) && IsStructure(TopPiece(c.transform).name));
        }

        private static Transform TopPiece(Transform t)
        {
            while (t.parent != null && t.parent.name != "KHS_KimMyeonggwanHouse") t = t.parent;
            return t;
        }

        private struct GroundSample
        {
            public Vector2 P;
            public float Y;
            public bool Firm;
            public bool Upper;
        }

        /// <summary>
        /// The scan was built on rolling ground (up to ~0.8 m above and ~0.4 m below the yards), and that ground
        /// is not in the asset: on a flat yard, footings float in some places and stones are buried in others.
        /// The ground is recovered from what stood on it: every part with nothing of the house below it marks the
        /// ground at its base, in 0.5 m cells. Floors give ceilings instead: the ground must stay under them.
        /// Called before the yard ground exists.
        /// </summary>
        private static List<GroundSample> FindGroundSamples(Transform house, List<GroundSample> ceilings)
        {
            var samples = new List<GroundSample>();
            var boxes = new List<(Transform t, Bounds b)>();
            foreach (Transform piece in house)
            {
                var renderers = piece.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                var bb = renderers[0].bounds;
                foreach (var r in renderers) bb.Encapsulate(r.bounds);
                boxes.Add((piece, bb));
            }
            foreach (var (piece, bounds) in boxes)
            {
                var floor = piece.name.StartsWith("SM_Floor");
                var carries = IsLoose(piece.name) && CarriesStructure(piece);
                var firm = IsStructure(piece.name) || carries;
                if (!firm && !floor && !IsLoose(piece.name)) continue;
                // A foundation stone shows above the ground: it may not be buried past the top few centimetres.
                if (carries) ceilings.Add(new GroundSample { P = new Vector2(bounds.center.x, bounds.center.z), Y = bounds.max.y - 0.04f });
                if (!floor && StackedOnAnother(piece, bounds, boxes)) continue;
                foreach (var low in LowestPerCell(piece))
                {
                    if (floor)
                    {
                        ceilings.Add(new GroundSample { P = new Vector2(low.x, low.z), Y = low.y });
                        continue;
                    }
                    if (low.y < -0.6f || low.y > 1.0f) continue;
                    // Resting on another part of the house (a post on its stone, a beam on posts)?
                    var resting = Physics.RaycastAll(low + Vector3.up * 0.03f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore)
                        .Any(h => !h.collider.transform.IsChildOf(piece) && !h.collider.name.StartsWith("SM_Ground"));
                    if (resting) continue;
                    samples.Add(new GroundSample
                    {
                        P = new Vector2(low.x, low.z), Y = low.y, Firm = firm,
                        Upper = Superstructure.Any(piece.name.StartsWith),
                    });
                }
            }

            // Drop timber/plaster samples that sit well above stonework or posts close by: they rest on beams.
            var footing = Bucket(samples.Where(s => s.Firm && !s.Upper));
            return samples.Where(s => !s.Upper || !LowerNearby(footing, s, 1.2f, 0.2f)).ToList();
        }

        /// <summary>
        /// Laid on top of another piece (a ring of a stone well on the ring below, a stone on a pile)? Then its
        /// underside does not mark the ground anywhere. Only pieces of a size to carry it count as supports:
        /// the bounds of a long compound wall cover half the yard.
        /// </summary>
        private static bool StackedOnAnother(Transform piece, Bounds b, List<(Transform t, Bounds b)> boxes)
        {
            var area = b.size.x * b.size.z;
            foreach (var (other, ob) in boxes)
            {
                if (other == piece || Mathf.Max(ob.size.x, ob.size.z) > 6f) continue;
                if (ob.min.y > b.min.y - 0.05f || ob.max.y < b.min.y - 0.1f) continue;
                var ox = Mathf.Min(b.max.x, ob.max.x) - Mathf.Max(b.min.x, ob.min.x);
                var oz = Mathf.Min(b.max.z, ob.max.z) - Mathf.Max(b.min.z, ob.min.z);
                if (ox > 0f && oz > 0f && ox * oz > area * 0.3f) return true;
            }
            return false;
        }

        private static bool LowerNearby(Dictionary<Vector2Int, List<GroundSample>> buckets, GroundSample s, float radius, float drop)
        {
            var r = Mathf.CeilToInt(radius);
            for (var bz = Mathf.FloorToInt(s.P.y) - r; bz <= Mathf.FloorToInt(s.P.y) + r; bz++)
            for (var bx = Mathf.FloorToInt(s.P.x) - r; bx <= Mathf.FloorToInt(s.P.x) + r; bx++)
            {
                if (!buckets.TryGetValue(new Vector2Int(bx, bz), out var list)) continue;
                if (list.Any(o => (o.P - s.P).sqrMagnitude < radius * radius && o.Y < s.Y - drop)) return true;
            }
            return false;
        }

        /// <summary>
        /// The lowest vertex of a piece's underside in each 0.5 m ground cell it covers. Vertices of upward
        /// faces are skipped: the top of a hollow terrace wall (화계) is not where it meets the ground.
        /// </summary>
        private static IEnumerable<Vector3> LowestPerCell(Transform piece)
        {
            var cells = new Dictionary<Vector2Int, Vector3>();
            var flat = piece.name.StartsWith("SM_Stone_Tile");
            foreach (var mf in piece.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var m = mf.transform.localToWorldMatrix;
                var vertices = mf.sharedMesh.vertices;
                var normals = mf.sharedMesh.normals;
                for (var k = 0; k < vertices.Length; k++)
                {
                    if (!flat && k < normals.Length && m.MultiplyVector(normals[k]).normalized.y > 0.5f) continue;
                    var w = m.MultiplyPoint3x4(vertices[k]);
                    var key = new Vector2Int(Mathf.FloorToInt(w.x / GroundCell), Mathf.FloorToInt(w.z / GroundCell));
                    if (!cells.TryGetValue(key, out var low) || w.y < low.y) cells[key] = w;
                }
            }
            return cells.Values;
        }

        private static Dictionary<Vector2Int, List<GroundSample>> Bucket(IEnumerable<GroundSample> samples)
        {
            var buckets = new Dictionary<Vector2Int, List<GroundSample>>();
            foreach (var s in samples)
            {
                var key = new Vector2Int(Mathf.FloorToInt(s.P.x), Mathf.FloorToInt(s.P.y));
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<GroundSample>();
                list.Add(s);
            }
            return buckets;
        }

        /// <summary>Distance-weighted blend of the samples around a point, fading to y = 0 away from them.</summary>
        private static float Blend(Dictionary<Vector2Int, List<GroundSample>> buckets, float x, float z, out bool near)
        {
            var reach = Mathf.CeilToInt(GroundReach);
            double sum = 0, weight = 1.0 / (GroundReach * GroundReach);
            near = false;
            for (var bz = Mathf.FloorToInt(z) - reach; bz <= Mathf.FloorToInt(z) + reach; bz++)
            for (var bx = Mathf.FloorToInt(x) - reach; bx <= Mathf.FloorToInt(x) + reach; bx++)
            {
                if (!buckets.TryGetValue(new Vector2Int(bx, bz), out var list)) continue;
                foreach (var s in list)
                {
                    var d2 = (s.P.x - x) * (s.P.x - x) + (s.P.y - z) * (s.P.y - z);
                    if (d2 > GroundReach * GroundReach) continue;
                    var w = 1.0 / (d2 + 0.1);
                    sum += w * (s.Y + GroundEmbed);
                    weight += w;
                    near = true;
                }
            }
            return (float)(sum / weight);
        }

        /// <summary>
        /// Heights for the ground grid: a distance-weighted blend of the samples (loose stones only where they
        /// agree with the built parts around them), then lifted wherever a built part would otherwise float and
        /// kept under every floor. Lifting is one-way: a wall foot or a post a little sunk into the ground looks
        /// planted, a floating one does not.
        /// </summary>
        private static float[,] GroundHeights(List<GroundSample> samples, List<GroundSample> ceilings, out int outliers)
        {
            var nx = Mathf.RoundToInt(GroundArea.width / GroundCell) + 1;
            var nz = Mathf.RoundToInt(GroundArea.height / GroundCell) + 1;

            var firmBuckets = Bucket(samples.Where(s => s.Firm));
            var used = new List<GroundSample>();
            outliers = 0;
            foreach (var s in samples)
            {
                if (!s.Firm)
                {
                    var built = Blend(firmBuckets, s.P.x, s.P.y, out var near);
                    if (near && Mathf.Abs(s.Y + GroundEmbed - built) > LooseOutlier)
                    {
                        outliers++;
                        continue;
                    }
                }
                used.Add(s);
            }
            var buckets = Bucket(used);

            var heights = new float[nx, nz];
            for (var j = 0; j < nz; j++)
            for (var i = 0; i < nx; i++)
                heights[i, j] = Blend(buckets, GroundArea.xMin + i * GroundCell, GroundArea.yMin + j * GroundCell, out _);

            // No built part may float: the grid points around each of its ground cells reach its base.
            var lower = new float[nx, nz];
            var upper = new float[nx, nz];
            for (var j = 0; j < nz; j++)
            for (var i = 0; i < nx; i++)
            {
                lower[i, j] = float.MinValue;
                upper[i, j] = float.MaxValue;
            }
            foreach (var s in samples.Where(s => s.Firm))
                ForCellCorners(s, nx, nz, (i, j, inner) =>
                {
                    // Inner ring: right under the part. Outer ring: a short apron so its edge shows no gap.
                    lower[i, j] = Mathf.Max(lower[i, j], s.Y + GroundEmbed - (inner ? 0f : 0.05f));
                });
            // And none may come up through a floor (the boards over a raised room's sills and joists) or over
            // the top of a foundation stone.
            foreach (var c in ceilings)
                ForCellCorners(c, nx, nz, (i, j, inner) =>
                {
                    if (inner) upper[i, j] = Mathf.Min(upper[i, j], c.Y - 0.03f);
                });

            // Relax the land between those limits, so lifts under footings become slopes rather than steps.
            var next = new float[nx, nz];
            for (var pass = 0; pass < GroundSmoothing; pass++)
            {
                for (var j = 0; j < nz; j++)
                for (var i = 0; i < nx; i++)
                {
                    var h = heights[i, j];
                    if (pass > 0 && i > 0 && j > 0 && i < nx - 1 && j < nz - 1)
                        h = (heights[i - 1, j] + heights[i + 1, j] + heights[i, j - 1] + heights[i, j + 1] + 2f * h) / 6f;
                    next[i, j] = Mathf.Min(Mathf.Max(h, lower[i, j]), upper[i, j]);
                }
                (heights, next) = (next, heights);
            }
            return heights;
        }

        private static void ForCellCorners(GroundSample s, int nx, int nz, System.Action<int, int, bool> visit)
        {
            var i0 = Mathf.FloorToInt((s.P.x - GroundArea.xMin) / GroundCell);
            var j0 = Mathf.FloorToInt((s.P.y - GroundArea.yMin) / GroundCell);
            for (var dj = -1; dj <= 2; dj++)
            for (var di = -1; di <= 2; di++)
            {
                int i = i0 + di, j = j0 + dj;
                if (i < 0 || j < 0 || i >= nx || j >= nz) continue;
                visit(i, j, di >= 0 && di <= 1 && dj >= 0 && dj <= 1);
            }
        }

        /// <summary>
        /// Sets every loose piece (stones, jars, lids) down on what is under it now that the ground is in:
        /// lowered when floating, raised when the ground buried it, slightly bedded in. Pieces stacked or
        /// nested together (a lid on its jar) move as one; a stone carrying a post stays. Returns how many
        /// pieces moved.
        /// </summary>
        public static int SettleLoosePieces(Transform house)
        {
            var loose = new List<(Transform t, Bounds b)>();
            foreach (Transform piece in house)
            {
                if (!IsLoose(piece.name) || CarriesStructure(piece)) continue;
                var renderers = piece.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                var bb = renderers[0].bounds;
                foreach (var rr in renderers) bb.Encapsulate(rr.bounds);
                loose.Add((piece, bb));
            }

            bool Over(Bounds a, Bounds c) =>
                Mathf.Abs(a.center.x - c.center.x) < Mathf.Max(a.extents.x, c.extents.x) * 0.7f &&
                Mathf.Abs(a.center.z - c.center.z) < Mathf.Max(a.extents.z, c.extents.z) * 0.7f;
            bool IsLid(Transform t) => t.name.Contains("Cap") || t.name.Contains("_Top");
            var parent = Enumerable.Range(0, loose.Count).ToArray();
            int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
            for (var i = 0; i < loose.Count; i++)
            for (var j = i + 1; j < loose.Count; j++)
            {
                var a = loose[i].b;
                // A lid may hover a little over its jar or pot in the scan: it still belongs with it.
                a.Expand(IsLid(loose[i].t) || IsLid(loose[j].t) ? new Vector3(0.02f, 0.4f, 0.02f) : Vector3.one * 0.02f);
                if (a.Intersects(loose[j].b) && Over(a, loose[j].b)) parent[Find(i)] = Find(j);
            }

            var moved = 0;
            foreach (var group in Enumerable.Range(0, loose.Count).GroupBy(Find))
            {
                var members = group.Select(i => loose[i]).OrderBy(m => m.b.min.y).ToList();
                // Lids first come down onto what they cover, then the whole stack onto the ground.
                for (var k = members.Count - 1; k > 0; k--)
                {
                    if (!IsLid(members[k].t)) continue;
                    var lift = SettleShift(new List<Transform> { members[k].t });
                    if (lift < -0.02f) members[k].t.position += new Vector3(0f, lift, 0f);
                }
                Physics.SyncTransforms();
                var transforms = members.Select(m => m.t).ToList();
                var shift = SettleShift(transforms);
                if (Mathf.Abs(shift) <= 0.02f) continue;
                foreach (var m in transforms) m.position += new Vector3(0f, shift, 0f);
                Physics.SyncTransforms();
                moved += members.Count;
            }
            return moved;
        }

        /// <summary>
        /// How far to move a group up (+) or down (-) so it rests on the surfaces under its base, measured at
        /// points of its underside: the ground, or a part of the house it lies on.
        /// </summary>
        private static float SettleShift(List<Transform> group)
        {
            var points = new List<Vector3>();
            foreach (var t in group)
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var m = mf.transform.localToWorldMatrix;
                points.AddRange(mf.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v)));
            }
            if (points.Count == 0) return 0f;
            var bottom = points.Min(p => p.y);
            var top = points.Max(p => p.y);
            var height = top - bottom;
            var underside = points.Where(p => p.y < bottom + Mathf.Min(0.05f, height * 0.3f)).ToList();
            var step = Mathf.Max(1, underside.Count / 24);

            var needs = new List<float>();
            for (var k = 0; k < underside.Count; k += step)
            {
                var p = underside[k];
                // Highest surface under this point the piece could rest on or be buried by: not something above
                // the piece (a floor over a stone), so the ray starts inside the piece.
                var from = Mathf.Max(Mathf.Min(p.y + 0.6f, top - 0.01f), p.y + 0.01f);
                var surface = float.MinValue;
                foreach (var h in Physics.RaycastAll(new Vector3(p.x, from, p.z), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                    if (!group.Any(g => h.collider.transform.IsChildOf(g)) && h.point.y > surface) surface = h.point.y;
                // Buried deeper than itself: the ground is above its top.
                foreach (var h in Physics.RaycastAll(new Vector3(p.x, top + 2f, p.z), Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore))
                    if (h.collider.name == "Yard ground" && h.point.y > surface) surface = h.point.y;
                if (surface > float.MinValue) needs.Add(surface - p.y);
            }
            if (needs.Count == 0) return 0f;
            needs.Sort();
            var embed = Mathf.Clamp(height * 0.15f, 0.015f, 0.05f);
            // Rest on the higher contacts (it cannot sink into stone), bedded in a little.
            return needs[(int)(needs.Count * 0.7f)] - embed;
        }

        /// <summary>
        /// Some posts of the scan stop a few centimetres above the platform under them, with no foundation
        /// stone. Each gets one: a copy of the house's own foundation stone, sized to fill the gap.
        /// Returns how many were placed.
        /// </summary>
        private static int AddMissingPostStones(Transform p, Transform house)
        {
            var templateFilter = StoneTemplate(house);
            if (templateFilter == null) return 0;
            var count = 0;
            foreach (Transform post in house)
            {
                if (!post.name.StartsWith("SM_Pillar")) continue;
                var renderers = post.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                // Upright posts only (horizontal beams are named SM_Pillar too).
                if (b.size.y < 1.5f || Mathf.Max(b.size.x, b.size.z) > 0.5f) continue;
                var foot = new Vector3(b.center.x, b.min.y, b.center.z);
                if (InsideAnother(foot + Vector3.up * 0.01f, post)) continue;
                if (!Physics.Raycast(foot + Vector3.up * 0.01f, Vector3.down, out var hit, 0.5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                var gap = foot.y - hit.point.y;
                if (gap < 0.03f || gap > 0.35f) continue;

                var width = Mathf.Max(b.size.x, b.size.z) * 1.8f;
                var height = gap + 0.06f;
                // Bottom 3 cm into the platform, top 3 cm into the post.
                PlaceStone(p, $"Post stone ({post.name})", templateFilter,
                    new Vector3(foot.x, hit.point.y - 0.03f + height / 2f, foot.z), new Vector3(width, height, width),
                    Quaternion.Euler(0f, post.eulerAngles.y, 0f));
                count++;
            }
            Physics.SyncTransforms();
            return count;
        }

        /// <summary>The house's own dressed foundation stone, copied for added post stones and door steps.</summary>
        private static MeshFilter StoneTemplate(Transform house)
        {
            var template = house.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("SM_FoundationStone"));
            return template != null ? template.GetComponentInChildren<MeshFilter>() : null;
        }

        /// <summary>
        /// A copy of the template stone, stretched to the given size (x across, y up, z along the facing) and
        /// centred on the point. The template's own (import) rotation keeps it upright inside a scaled holder.
        /// </summary>
        private static GameObject PlaceStone(Transform parent, string name, MeshFilter template, Vector3 center, Vector3 size, Quaternion facing)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var body = new GameObject("Stone");
            body.transform.SetParent(holder.transform, false);
            body.transform.rotation = template.transform.rotation;
            body.AddComponent<MeshFilter>().sharedMesh = template.sharedMesh;
            var renderer = body.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = template.GetComponent<MeshRenderer>().sharedMaterials;
            var natural = renderer.bounds;
            var scale = new Vector3(size.x / natural.size.x, size.y / natural.size.y, size.z / natural.size.z);
            holder.transform.localScale = scale;
            holder.transform.rotation = facing;
            holder.transform.position = center - facing * Vector3.Scale(natural.center, scale);
            body.AddComponent<MeshCollider>().sharedMesh = template.sharedMesh;
            return holder;
        }

        /// <summary>Is the point inside a closed collider other than the given piece (rays from above and below both meet it)?</summary>
        private static bool InsideAnother(Vector3 point, Transform piece)
        {
            var above = new HashSet<Collider>(Physics.RaycastAll(point + Vector3.up * 2f, Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => !h.collider.transform.IsChildOf(piece)).Select(h => h.collider));
            return Physics.RaycastAll(point - Vector3.up * 2f, Vector3.up, 2f, ~0, QueryTriggerInteraction.Ignore)
                .Any(h => above.Contains(h.collider));
        }

        private static void BuildGround(Transform p, float[,] heights)
        {
            var nx = Mathf.RoundToInt(GroundArea.width / GroundCell) + 1;
            var nz = Mathf.RoundToInt(GroundArea.height / GroundCell) + 1;
            var vertices = new Vector3[nx * nz];
            var uvs = new Vector2[nx * nz];
            for (var j = 0; j < nz; j++)
            for (var i = 0; i < nx; i++)
            {
                var x = GroundArea.xMin + i * GroundCell;
                var z = GroundArea.yMin + j * GroundCell;
                vertices[j * nx + i] = new Vector3(x, heights[i, j], z);
                uvs[j * nx + i] = new Vector2(x / 4f, z / 4f);
            }
            var triangles = new int[(nx - 1) * (nz - 1) * 6];
            var t = 0;
            for (var j = 0; j < nz - 1; j++)
            for (var i = 0; i < nx - 1; i++)
            {
                var a = j * nx + i;
                triangles[t++] = a; triangles[t++] = a + nx; triangles[t++] = a + 1;
                triangles[t++] = a + 1; triangles[t++] = a + nx; triangles[t++] = a + nx + 1;
            }
            var mesh = new Mesh { name = "Yard ground", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            // Saved as an asset so the scene does not carry 85k vertices inline.
            const string meshPath = "Assets/_Project/Art/Meshes/KhsYardGround.asset";
            ManorBlockoutBuilder.EnsureFolder("Assets/_Project/Art/Meshes");
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var go = new GameObject("Yard ground");
            go.transform.SetParent(p, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            go.AddComponent<MeshCollider>().sharedMesh = mesh;

            var source = AssetDatabase.LoadAssetAtPath<Material>(GroundMatPath);
            if (source == null) return;
            // Kept (not recreated) so its GUID, and the scene that references it, stay stable between builds.
            const string path = "Assets/_Project/Art/Materials/KhsYardGround.mat";
            var tiled = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (tiled == null)
            {
                ManorBlockoutBuilder.EnsureFolder("Assets/_Project/Art/Materials");
                tiled = new Material(source);
                AssetDatabase.CreateAsset(tiled, path);
            }
            else
            {
                tiled.CopyPropertiesFromMaterial(source);
            }
            tiled.mainTextureScale = Vector2.one;
            tiled.SetFloat("_Smoothness", 0.08f);
            tiled.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(tiled);
            renderer.sharedMaterial = tiled;
        }

        // A door whose sill is more than this above the ground in front of it gets a stepping stone.
        private const float DoorStepMin = 0.25f;
        // Higher than two steps: not meant to be entered from that side (a window over a drop).
        private const float DoorStepMax = 1.2f;
        // A sill this far above the floor on its higher side marks a window, not a door.
        private const float WindowSill = 0.25f;

        /// <summary>
        /// Raised rooms and 툇마루 have their doors 0.5-1 m above the yard, and the scan seldom puts a 섬돌 in
        /// front of them. Every openable door gets one on each side that is too far below its sill, halfway up,
        /// unless something to step on is already there (a second leaf of the same doorway finds the first one).
        /// Each leaf also gets a sill cap (below).
        /// </summary>
        private static int BuildDoorSteps(Transform p, Transform house)
        {
            var template = StoneTemplate(house);
            var leaves = new List<(Transform leaf, Bounds b, Vector3 through, float width)>();
            foreach (var leaf in house.GetComponentsInChildren<ProximityDoor>().Select(d => d.transform))
            {
                var mf = leaf.GetComponentInChildren<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var rs = leaf.GetComponentsInChildren<Renderer>();
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);

                // The leaf's thin local axis, flattened, is the way through.
                var size = mf.sharedMesh.bounds.size;
                var axis = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
                var through = mf.transform.TransformDirection(axis);
                through.y = 0f;
                if (through.sqrMagnitude < 0.01f) continue;
                through.Normalize();
                var width = Mathf.Max(Mathf.Abs(Vector3.Dot(b.size, Vector3.Cross(Vector3.up, through))), 0.5f);

                // The wall under many leaves is an open shell with no top face, so nothing can stand on the sill:
                // cap it with an invisible slab level with the bottom of the leaf.
                var cap = new GameObject($"Sill cap ({leaf.name})");
                cap.transform.SetParent(p, false);
                cap.transform.SetPositionAndRotation(new Vector3(b.center.x, b.min.y - 0.155f, b.center.z), Quaternion.LookRotation(through));
                cap.AddComponent<BoxCollider>().size = new Vector3(width, 0.3f, 0.2f);
                leaves.Add((leaf, b, through, width));
            }
            Physics.SyncTransforms();
            if (template == null) return 0;

            // The leaves of one doorway (a pair, or a run of three or four) share one flight of stones.
            var doorways = new List<(Bounds b, Vector3 through, float width, string name)>();
            var used = new bool[leaves.Count];
            for (var i = 0; i < leaves.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                var (leaf, b, through, _) = leaves[i];
                var across = Vector3.Cross(Vector3.up, through);
                for (var grew = true; grew;)
                {
                    grew = false;
                    for (var j = 0; j < leaves.Count; j++)
                    {
                        if (used[j]) continue;
                        var d = leaves[j].b.center - b.center;
                        if (Mathf.Abs(Vector3.Dot(d, through)) > 0.2f || Mathf.Abs(d.y) > 0.3f) continue;
                        var reach = Mathf.Abs(Vector3.Dot(b.size, across)) / 2f + leaves[j].width / 2f + 0.15f;
                        if (Mathf.Abs(Vector3.Dot(d, across)) > reach) continue;
                        used[j] = true;
                        b.Encapsulate(leaves[j].b);
                        grew = true;
                    }
                }
                doorways.Add((b, through, Mathf.Max(Mathf.Abs(Vector3.Dot(b.size, across)), 0.7f), leaf.name));
            }

            var count = 0;
            foreach (var (b, through, width, name) in doorways)
            {
                if (!Physics.Raycast(new Vector3(b.center.x, b.min.y + 0.3f, b.center.z), Vector3.down, out var sillHit, 1.5f, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                var sill = sillHit.point.y;

                // A leaf whose sill stands well above the floor on both sides is a window over a 머름 panel:
                // nobody steps through it, so it gets no stone (and looks it).
                var sideFloors = new[] { 1f, -1f }.Select(sg => WalkableBelow(b.center + through * sg * 0.8f, sill + 0.3f))
                    .Where(f => f != null).Select(f => f.Value).ToList();
                if (sideFloors.Count == 0 || sill - sideFloors.Max() > WindowSill) continue;

                foreach (var sign in new[] { 1f, -1f })
                {
                    var outward = through * sign;
                    // What is just outside, where the stones go: the ground, a platform, or a 섬돌 of the scan
                    // (high enough already, then nothing is added).
                    var floor = WalkableBelow(b.center + outward * 0.45f, sill + 0.3f);
                    if (floor == null || sill - floor.Value <= DoorStepMin || sill - floor.Value > DoorStepMax) continue;

                    // One stone per 0.3 m of rise, like a short flight of 섬돌: the highest against the sill.
                    const float depth = 0.4f;
                    var rise = sill - floor.Value;
                    var steps = Mathf.Max(1, Mathf.CeilToInt(rise / 0.3f) - 1);
                    var bottom = floor.Value - 0.3f;
                    for (var k = 1; k <= steps; k++)
                    {
                        var top = sill - rise * k / (steps + 1);
                        PlaceStone(p, $"Door step stone ({name})", template,
                            new Vector3(b.center.x, (top + bottom) / 2f, b.center.z) + outward * (0.14f + depth * (k - 0.5f)),
                            new Vector3(width + 0.1f, top - bottom, depth), Quaternion.LookRotation(outward));
                    }
                    Physics.SyncTransforms();
                    count++;
                }
            }
            return count;
        }

        private static float? WalkableBelow(Vector3 p, float fromY)
        {
            float? best = null;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, fromY, p.z), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                if (h.normal.y > 0.6f && (best == null || h.point.y > best.Value)) best = h.point.y;
            return best;
        }

        // ---------------------------------------------------------------- partitions

        private struct Partition
        {
            public string Name;
            public Vector2 From, To;
            public float DoorAt, DoorWidth;   // distance along the wall to the door centre; 0 width = no door
        }

        // The scan leaves the 사랑채 interior mostly open: the room behind the hall (서재) has no north wall and
        // 사랑방 / 대청 / the east end run into one long floor. The scenario needs closed rooms (sounds through
        // paper walls, a door gap to peek through), so paper partitions are added on the scan's floor lines.
        private static readonly Partition[] Partitions =
        {
            new Partition { Name = "서재 북벽 (스캔에 없음)", From = new Vector2(9.45f, -8.47f), To = new Vector2(12.16f, -8.47f) },
            new Partition { Name = "사랑방 | 대청", From = new Vector2(9.43f, -10.17f), To = new Vector2(9.43f, -12.62f), DoorAt = 1.25f, DoorWidth = 0.9f },
            new Partition { Name = "대청 | 작은사랑", From = new Vector2(14.86f, -10.17f), To = new Vector2(14.86f, -12.62f), DoorAt = 1.25f, DoorWidth = 0.9f },
        };

        private const float PartitionHeight = 2.3f;
        private const float PartitionDoorHeight = 1.75f;
        private const float PartitionThickness = 0.1f;

        private static void BuildPartitions(Transform p)
        {
            var paper = AssetDatabase.LoadAssetAtPath<Material>(KhsAssetTools.Root + "/KimMyeonggwanHouse/Material/MI_WhiteWall01A.mat");
            var wood = AssetDatabase.LoadAssetAtPath<Material>(KhsAssetTools.Root + "/KimMyeonggwanHouse/Material/MI_Wood01A.mat");
            foreach (var w in Partitions)
            {
                var holder = new GameObject(w.Name).transform;
                holder.SetParent(p, false);
                var mid = (w.From + w.To) / 2f;
                var floor = FloorY(mid.x, mid.y);
                var length = Vector2.Distance(w.From, w.To);
                var dir = (w.To - w.From).normalized;
                holder.SetPositionAndRotation(new Vector3(w.From.x, floor, w.From.y),
                    Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y)));

                // Pieces along the local z axis: wall | door gap | wall, a lintel over the gap, a wood sill.
                var d0 = w.DoorWidth > 0f ? w.DoorAt - w.DoorWidth / 2f : length;
                var d1 = w.DoorWidth > 0f ? w.DoorAt + w.DoorWidth / 2f : length;
                Slab(holder, "Wall", 0f, d0, 0f, PartitionHeight, paper);
                if (d1 < length) Slab(holder, "Wall", d1, length, 0f, PartitionHeight, paper);
                if (w.DoorWidth > 0f) Slab(holder, "Lintel", d0, d1, PartitionDoorHeight, PartitionHeight, wood);
                Slab(holder, "Base", 0f, d0, 0f, 0.25f, wood, 0.02f);
                if (d1 < length) Slab(holder, "Base", d1, length, 0f, 0.25f, wood, 0.02f);
            }
        }

        private static void Slab(Transform holder, string name, float a, float b, float y0, float y1, Material mat, float extra = 0f)
        {
            if (b - a < 0.01f) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(holder, false);
            go.transform.localPosition = new Vector3(0f, (y0 + y1) / 2f, (a + b) / 2f);
            go.transform.localScale = new Vector3(PartitionThickness + extra, y1 - y0, b - a);
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static void BuildBlockers(Transform p)
        {
            foreach (var (b, lockId) in Blockers)
            {
                var go = new GameObject($"Blocker ({lockId})");
                go.transform.SetParent(p, false);
                go.transform.position = new Vector3(b.x, 1.5f, b.y);
                go.AddComponent<BoxCollider>().size = new Vector3(b.z, 3f, b.w);
                // Looking at the invisible wall tells the player why they cannot go on.
                go.AddComponent<LockedDoor>().Configure(lockId, null);
            }
        }



        // ---------------------------------------------------------------- rooms sealed until the second search

        // 도 검률 sealed each suspect's quarters at dawn and opens them at 9 (lock opensAtPhase): door leaves in
        // the area turn solid with a paper seal on them, open sides get a straw rope (금줄) with paper strips.
        private static readonly (string lockId, Rect leafArea, Vector2[][] ropes)[] SealedRooms =
        {
            // 작은사랑: its doors, and the leafless doorway from the 대청.
            ("sealed_jaeha", new Rect(14.6f, -15.0f, 3.0f, 5.6f), new[] { new[] { new Vector2(14.85f, -10.85f), new Vector2(14.85f, -11.95f) } }),
            // 건넌방: its doors (also the two from the 안대청) and the open bay from the 안대청 at its north end.
            ("sealed_jeong", new Rect(-4.05f, 7.8f, 5.25f, 9.4f), new[] { new[] { new Vector2(-3.65f, 13.55f), new Vector2(-3.65f, 16.75f) } }),
            ("sealed_guests", new Rect(0f, 0f, 0f, 0f), new[]
            {
                // The guests' open-fronted room (rotated with the 행랑채): front, west and east sides.
                new[] { new Vector2(8.80f, -26.73f), new Vector2(13.68f, -27.76f) },
                new[] { new Vector2(8.80f, -26.73f), new Vector2(8.29f, -29.30f) },
                new[] { new Vector2(13.68f, -27.76f), new Vector2(13.17f, -30.33f) },
            }),
        };

        private static void BuildSealedRooms(Transform p, Transform house)
        {
            var paper = SimpleMaterial("Assets/_Project/Art/Materials/SealPaper.mat", new Color(0.93f, 0.91f, 0.84f));
            var rope = SimpleMaterial("Assets/_Project/Art/Materials/SealRope.mat", new Color(0.62f, 0.52f, 0.33f));
            foreach (var (lockId, area, ropes) in SealedRooms)
            {
                var holder = new GameObject($"Sealed ({lockId})").transform;
                holder.SetParent(p, false);
                var leaves = new List<Transform>();
                var seals = new List<GameObject>();
                foreach (var door in house.GetComponentsInChildren<ProximityDoor>(true).ToList())
                {
                    var rs = door.GetComponentsInChildren<Renderer>();
                    if (rs.Length == 0) continue;
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    if (!area.Contains(new Vector2(b.center.x, b.center.z))) continue;
                    foreach (var mf in door.GetComponentsInChildren<MeshFilter>())
                    {
                        if (mf.sharedMesh == null) continue;
                        if (!mf.TryGetComponent<MeshCollider>(out var mc)) mc = mf.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                    }
                    leaves.Add(door.transform);
                    Object.DestroyImmediate(door);   // LockedDoor adds it back when the seal comes off
                    seals.Add(PaperSeal(holder, b, paper));
                }
                foreach (var r in ropes) seals.Add(StrawRope(holder, r[0], r[1], rope, paper));
                holder.gameObject.AddComponent<LockedDoor>().Configure(lockId, leaves.ToArray(), seals.ToArray());
            }
            Physics.SyncTransforms();
        }

        private static Material SimpleMaterial(string path, Color color)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            mat.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>A strip of paper across the middle of a door leaf, on both faces.</summary>
        private static GameObject PaperSeal(Transform parent, Bounds leaf, Material paper)
        {
            var thin = leaf.size.x < leaf.size.z;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Paper seal";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(leaf.center.x, leaf.min.y + leaf.size.y * 0.55f, leaf.center.z);
            go.transform.localScale = thin
                ? new Vector3(leaf.size.x + 0.02f, 0.5f, Mathf.Min(0.14f, leaf.size.z))
                : new Vector3(Mathf.Min(0.14f, leaf.size.x), 0.5f, leaf.size.z + 0.02f);
            go.GetComponent<MeshRenderer>().sharedMaterial = paper;
            return go;
        }

        /// <summary>A 금줄 between two points at waist height with paper strips, and a wall-high collider under it.</summary>
        private static GameObject StrawRope(Transform parent, Vector2 a, Vector2 b, Material rope, Material paper)
        {
            var go = new GameObject("Straw rope (금줄)");
            go.transform.SetParent(parent, false);
            var mid = (a + b) / 2f;
            var floor = Mathf.Max(FloorY(a.x, a.y), FloorY(b.x, b.y), FloorY(mid.x, mid.y));
            var ground = Mathf.Min(LowestFloorY(a.x, a.y), LowestFloorY(b.x, b.y), LowestFloorY(mid.x, mid.y));
            var length = Vector2.Distance(a, b);
            var dir = new Vector3(b.x - a.x, 0f, b.y - a.y).normalized;
            var facing = Quaternion.LookRotation(Vector3.Cross(Vector3.up, dir));
            go.transform.SetPositionAndRotation(new Vector3(mid.x, 0f, mid.y), facing);

            var line = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            line.name = "Rope";
            Object.DestroyImmediate(line.GetComponent<Collider>());
            line.transform.SetParent(go.transform, false);
            line.transform.SetPositionAndRotation(new Vector3(mid.x, floor + 0.95f, mid.y), Quaternion.FromToRotation(Vector3.up, dir));
            line.transform.localScale = new Vector3(0.04f, length / 2f, 0.04f);
            line.GetComponent<MeshRenderer>().sharedMaterial = rope;

            var strips = Mathf.Max(2, Mathf.RoundToInt(length / 0.6f));
            for (var i = 1; i < strips; i++)
            {
                var at = Vector2.Lerp(a, b, i / (float)strips);
                var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strip.name = "Paper strip";
                Object.DestroyImmediate(strip.GetComponent<Collider>());
                strip.transform.SetParent(go.transform, false);
                strip.transform.SetPositionAndRotation(new Vector3(at.x, floor + 0.8f, at.y), facing);
                strip.transform.localScale = new Vector3(0.07f, 0.26f, 0.005f);
                strip.GetComponent<MeshRenderer>().sharedMaterial = paper;
            }

            var block = go.AddComponent<BoxCollider>();
            block.center = new Vector3(0f, (ground + floor + 2.2f) / 2f, 0f);
            block.size = new Vector3(length, floor + 2.2f - ground, 0.1f);
            return go;
        }

        // ---------------------------------------------------------------- private rooms (밀담 방)

        // The 사당 building (its platform, walls, roof and four door leaves), copied for the private rooms.
        private static readonly Rect ShrineArea = new Rect(9.6f, 18.5f, 5.4f, 5.9f);
        // Template points (x, z in the 사당 as scanned): the room behind the doors and the porch outside them.
        private static readonly Vector2 ShrineOrigin = new Vector2(12.25f, 21.35f);
        private const float ShrineInteriorZ = 22.05f;
        private const float ShrinePorchZ = 20.55f;

        // The rooms, side by side in the empty back yard north of the 안채 (centre x, z; scale).
        private static readonly (string id, Vector2 at, float scale, Vector2[] seats)[] TalkRooms =
        {
            ("small2", new Vector2(-25.0f, 33.0f), 1.0f, new[] { new Vector2(-0.85f, 0f), new Vector2(0f, 0.35f), new Vector2(0.85f, 0f) }),
            ("small1", new Vector2(-18.0f, 33.0f), 1.0f, new[] { new Vector2(-0.85f, 0f), new Vector2(0f, 0.35f), new Vector2(0.85f, 0f) }),
            ("council", new Vector2(-9.5f, 33.5f), 1.5f, new[] { new Vector2(-0.95f, -0.15f), new Vector2(-0.5f, 0.35f), new Vector2(0.5f, 0.35f),
                                                                  new Vector2(0.95f, -0.15f), new Vector2(0f, -0.4f) }),
        };

        /// <summary>
        /// Builds the private rooms (RoomManager): copies of the 사당 building, the council room half again as
        /// large. Their doors are solid (no ProximityDoor): people go in and out only by the room rules and are
        /// moved by RoomPresence to a seat inside or to the porch. The scan leaves the room behind the doors
        /// without boards, so the porch floor is copied in there too.
        /// </summary>
        private static void BuildTalkRooms(Transform p, Transform house)
        {
            var parts = new List<Transform>();
            Transform porch = null;
            foreach (Transform piece in house)
            {
                var renderers = piece.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                if (!ShrineArea.Contains(new Vector2(b.center.x, b.center.z)) || Mathf.Max(b.size.x, b.size.z) > 7f) continue;
                if (piece.name.StartsWith("SM_StoneWall") || piece.name.StartsWith("SM_Stonewall")) continue;
                parts.Add(piece);
                if (piece.name.StartsWith("SM_Floor") && b.center.z < ShrineOrigin.y) porch = piece;
            }
            if (parts.Count == 0) return;
            var baseY = parts.Min(t => t.GetComponentsInChildren<Renderer>().Min(r => r.bounds.min.y));
            var origin = new Vector3(ShrineOrigin.x, baseY, ShrineOrigin.y);

            foreach (var (id, at, scale, seatOffsets) in TalkRooms)
            {
                var holder = new GameObject($"Talk room ({id})").transform;
                holder.SetParent(p, false);
                holder.SetPositionAndRotation(origin, Quaternion.identity);
                foreach (var part in parts)
                {
                    var copy = Object.Instantiate(part.gameObject, part.position, part.rotation);
                    copy.name = part.name;
                    copy.transform.SetParent(holder, true);
                }
                if (porch != null)
                {
                    var inside = Object.Instantiate(porch.gameObject, porch.position + new Vector3(0f, 0f, 1.42f), porch.rotation);
                    inside.name = "Room floor";
                    inside.transform.SetParent(holder, true);
                }

                // Solid doors: no opening when someone comes near.
                var doors = new List<Collider>();
                foreach (var door in holder.GetComponentsInChildren<ProximityDoor>(true).ToList())
                {
                    foreach (var mf in door.GetComponentsInChildren<MeshFilter>())
                    {
                        if (mf.sharedMesh == null) continue;
                        if (!mf.TryGetComponent<MeshCollider>(out var mc)) mc = mf.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        doors.Add(mc);
                    }
                    Object.DestroyImmediate(door);
                }

                var ground = FloorY(at.x, at.y);
                holder.localScale = Vector3.one * scale;
                holder.position = new Vector3(at.x, ground - 0.03f, at.y);
                Physics.SyncTransforms();

                Vector3 Local(float x, float z) => holder.position + new Vector3(x - ShrineOrigin.x, 0f, z - ShrineOrigin.y) * scale;
                float FloorAt(Vector3 w) => Physics.RaycastAll(new Vector3(w.x, holder.position.y + 1.6f * scale, w.z), Vector3.down, 3f * scale,
                        ~0, QueryTriggerInteraction.Ignore).Where(h => h.normal.y > 0.7f).OrderBy(h => h.distance)
                    .Select(h => h.point.y).DefaultIfEmpty(ground).First();

                var seats = new List<Transform>();
                var middle = Local(ShrineOrigin.x, ShrineInteriorZ);
                for (var i = 0; i < seatOffsets.Length; i++)
                {
                    var w = Local(ShrineOrigin.x + seatOffsets[i].x, ShrineInteriorZ + seatOffsets[i].y);
                    var seat = new GameObject($"Seat {i + 1}").transform;
                    seat.SetParent(holder, true);
                    // Turned towards the middle of the room, so people face each other.
                    var look = middle - w;
                    look.y = 0f;
                    var facing = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.Euler(0f, 180f, 0f);
                    seat.SetPositionAndRotation(new Vector3(w.x, FloorAt(w) + 0.05f, w.z), facing);
                    seats.Add(seat);
                }
                var porchPoint = Local(ShrineOrigin.x, ShrinePorchZ);
                var outside = new GameObject("Outside").transform;
                outside.SetParent(holder, true);
                outside.SetPositionAndRotation(new Vector3(porchPoint.x, FloorAt(porchPoint) + 0.05f, porchPoint.z), Quaternion.Euler(0f, 180f, 0f));

                holder.gameObject.AddComponent<TalkRoomPlace>().Configure(id, seats.ToArray(), outside, doors.ToArray());
            }
        }

        // ---------------------------------------------------------------- gameplay

        private static void BuildSpots(Transform p)
        {
            foreach (var s in Spots)
            {
                var go = new GameObject($"Spot_{s.Id} ({s.Name})");
                go.transform.SetParent(p, false);
                var floor = s.Lift >= 0f ? FloorY(s.X, s.Z) : LowestFloorY(s.X, s.Z);
                go.transform.position = new Vector3(s.X, floor + Mathf.Abs(s.Lift), s.Z);
                go.AddComponent<InvestigationSpot>().Configure(s.Id, s.Name, s.Layer, s.Clues);
                var col = go.AddComponent<SphereCollider>();
                col.isTrigger = true;
                col.radius = 1.2f;
            }
        }

        // ---------------------------------------------------------------- props (KHS UnhyeongungCollect)

        private const string PropDir = KhsAssetTools.Root + "/UnhyeongungCollect/Prefabs/";

        private struct PropDef
        {
            public string Label;
            public float X, Z, Yaw, Above;   // Above: stand on top of another prop of this height
            public float Scale;              // 0 = the asset's own size
            public bool Under;               // on the ground under a raised floor, not on the floor
            public bool Straddle;            // across a platform edge: stands on the highest floor under it
            public string[] Parts;           // multi-part furniture shares one origin
        }

        private static PropDef Prop(string label, float x, float z, float yaw, params string[] parts) =>
            new PropDef { Label = label, X = x, Z = z, Yaw = yaw, Parts = parts };

        private static PropDef PropOn(string label, float x, float z, float above, params string[] parts) =>
            new PropDef { Label = label, X = x, Z = z, Above = above, Parts = parts };

        // Household relics of Unhyeongung at the investigation spots (the clue holders) plus a little dressing.
        // Yaw 180 turns a piece's front to -z (south), for furniture backed against a north wall.
        private static readonly PropDef[] Props =
        {
            Prop("01 서재 서안", 11.2f, -8.9f, 180f, "SM_Table252"),
            PropOn("01 서안 위 촛대", 11.0f, -8.9f, 0.64f, "SM_Candlestick122"),
            Prop("02 서재 문갑", 9.9f, -8.82f, 180f, "SM_Secretaries_body", "SM_Secretaries_doorLB", "SM_Secretaries_doorRB",
                "SM_Secretaries_drawerLB", "SM_Secretaries_drawerLM", "SM_Secretaries_drawerLT", "SM_Secretaries_drawerRB",
                "SM_Secretaries_drawerRM", "SM_Secretaries_drawerRT"),
            Prop("04 사랑방 화로", 7.2f, -10.6f, 0f, "SM_Brazier"),
            Prop("05 사랑방 소반", 6.2f, -10.0f, 0f, "SM_TongyeongTable"),
            PropOn("05 소반 위 탕약 그릇", 6.2f, -10.0f, 0.27f, "SM_WhitePorcelainBowl"),
            Prop("06 사랑방 사방탁자", 5.0f, -8.85f, 180f, "SM_Shelves241"),
            Prop("사랑방 보료", 5.7f, -11.4f, 0f, "SM_FloorCusion"),
            Prop("사랑방 안석", 5.7f, -11.75f, 0f, "SM_BackrestCushion"),
            Prop("09 툇마루 등경", 14.4f, -13.2f, 0f, "SM_FloorLamp"),
            Prop("09 빈 기름병", 14.7f, -13.5f, 0f, "SM_DarkBrownBottle"),
            Prop("10 작은사랑 서가", 16.3f, -10.3f, 180f, "SM_Board229_body", "SM_Board229_doorL", "SM_Board229_doorR",
                "SM_Board229_drawerB01", "SM_Board229_drawerB02", "SM_Board229_drawerB03", "SM_Board229_drawerB04",
                "SM_Board229_drawerS01", "SM_Board229_drawerS02", "SM_Board229_drawerS03"),
            Prop("11 곳간 쥐약 단지", -11.9f, -1.3f, 0f, "SM_BlackGlazedJar"),
            Prop("12 부엌 약탕관", -17.5f, 6.2f, 90f, "SM_PotForMedicines"),
            Prop("13 부엌 뒤주(쌀독)", -17.9f, 3.0f, 90f, "SM_GrainChest_body", "SM_GrainChest_cover01", "SM_GrainChest_cover02"),
            Prop("14 건넌방 반닫이", -1.5f, 9.8f, 270f, "SM_Box286"),
            Prop("15 허 의원 약상자", 9.6f, -28.0f, 0f, "SM_Chest_body", "SM_Chest_door", "SM_Chest_lock"),
            Prop("별당 방석 (허 의원)", 10.4f, -28.3f, 0f, "SM_FloorCusion"),
            Prop("별당 방석 (변 행수)", 12.4f, -28.3f, 0f, "SM_FloorCusion"),
            Prop("19 사랑방 의걸이장", 8.7f, -8.85f, 180f, "SM_Cabinet238"),
            new PropDef { Label = "20 사당 앞 제상", X = 12.6f, Z = 19.8f, Straddle = true, Parts = new[] { "SM_SquareTable" } },
            new PropDef { Label = "21 사당 향로", X = 11.6f, Z = 19.8f, Scale = 4f, Straddle = true, Parts = new[] { "SM_CeladonglazedSmall" } },
            Prop("23 수채의 사발", -20.5f, 4.5f, 30f, "SM_WhitePorcelainDish"),
            Prop("26 건넌방 경대", -0.25f, 12.6f, 270f, "SM_Cabinet237"),
            Prop("27 우물가 바가지", -17.6f, 20.5f, 0f, "SM_ScoopedWoodenVessel"),
            Prop("33 곳간 궤짝", -13.0f, -2.3f, 0f, "SM_Chest_body", "SM_Chest_door", "SM_Chest_lock"),
            // Markers for finds lying about, so each spot shows something to look at.
            new PropDef { Label = "22 마루 밑 침통", X = 5.0f, Z = -13.7f, Yaw = 20f, Under = true, Scale = 0.5f, Parts = new[] { "SM_WoodenSmoothingRollerw" } },
            Prop("24 부엌 체와 앞치마", -16.6f, 5.6f, 0f, "SM_Sieve397"),
            new PropDef { Label = "25 마루 밑 버선 자리", X = 9.8f, Z = -13.9f, Under = true, Parts = new[] { "SM_WoodenBowl_B" } },
            Prop("28 장독대 뚜껑", -14.3f, 21.8f, 25f, "SM_WhitePorcelainLid"),
            Prop("30 대청 구석 부채 자리", 14.0f, -10.8f, 0f, "SM_WoodenTray322"),
            Prop("31 안마당 노리개 자리", -6.0f, 4.0f, 0f, "SM_WhitePorcelainDish194"),
            Prop("34 부엌 찬장", -18.5f, 1.4f, 90f, "SM_CupBoard205_Body", "SM_CupBoard205_DoorL", "SM_CupBoard205_DoorR"),
            Prop("35 작은사랑 의걸이장", 15.6f, -13.25f, 0f, "SM_Cabinet238"),
        };

        private static void BuildProps(Transform p)
        {
            if (!AssetDatabase.IsValidFolder(PropDir.TrimEnd('/'))) return;
            foreach (var def in Props)
            {
                var holder = new GameObject(def.Label).transform;
                holder.SetParent(p, false);
                foreach (var part in def.Parts)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PropDir + part + ".prefab");
                    if (prefab == null)
                    {
                        Debug.LogWarning($"[KhsManorBuilder] missing prop {part}");
                        continue;
                    }
                    PrefabUtility.InstantiatePrefab(prefab, holder);
                }
                holder.rotation = Quaternion.Euler(0f, def.Yaw, 0f);
                if (def.Scale > 0f) holder.localScale = Vector3.one * def.Scale;

                // Sit the lowest point of the piece on the floor (or on the prop it stands on). Measured on the
                // first part (the body): some door and drawer meshes have their pivot well below the body.
                var renderers = holder.childCount > 0 ? holder.GetChild(0).GetComponentsInChildren<Renderer>() : new Renderer[0];
                if (renderers.Length == 0) continue;
                var bounds = renderers[0].bounds;
                foreach (var rr in renderers) bounds.Encapsulate(rr.bounds);
                // The highest floor under any part of its footprint: a piece straddling a platform edge stands on it.
                var floor = def.Under ? LowestFloorY(def.X, def.Z) + def.Above : !def.Straddle ? FloorY(def.X, def.Z) + def.Above : new[] { new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f) }
                    .Select(o => FloorY(def.X + o.x * bounds.extents.x * 0.8f, def.Z + o.y * bounds.extents.z * 0.8f)).Max() + def.Above;
                holder.position = new Vector3(def.X, floor - bounds.min.y, def.Z);
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

            // 도 검률 in 흑단령 (Tools/Murder Mystery/KHS/Build Character Outfits), facing the yard.
            var floor = new Vector3(NpcDaecheong.x, FloorY(NpcDaecheong.x, NpcDaecheong.y), NpcDaecheong.y);
            var outfit = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/Characters/dogeomnyul.prefab");
            GameObject npc;
            if (outfit != null)
            {
                npc = (GameObject)PrefabUtility.InstantiatePrefab(outfit, p);
                npc.transform.SetPositionAndRotation(floor, Quaternion.Euler(0f, 180f, 0f));
            }
            else
            {
                npc = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                npc.transform.SetParent(p, false);
                Object.DestroyImmediate(npc.GetComponent<Collider>());
                npc.transform.localScale = new Vector3(0.5f, 0.8f, 0.5f);
                npc.transform.position = floor + Vector3.up * 0.8f;
            }
            npc.name = "NPC (도 검률)";
        }

        private static void Marker(Transform p, string id, MarkerType type, Vector2 xz, float radius)
        {
            var go = new GameObject(id);
            go.transform.SetParent(p, false);
            go.transform.position = new Vector3(xz.x, FloorY(xz.x, xz.y), xz.y);
            go.AddComponent<GameplayMarker>().Configure(type, id, radius);
        }

        /// <summary>Highest walkable surface under a point, searched below ceiling height.</summary>
        /// <summary>The lowest walkable surface at a point: the ground under a raised floor.</summary>
        private static float LowestFloorY(float x, float z)
        {
            var hits = Physics.RaycastAll(new Vector3(x, 2.6f, z), Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore);
            var floor = hits.Where(h => h.normal.y > 0.7f).OrderByDescending(h => h.distance).FirstOrDefault();
            return floor.collider != null ? floor.point.y : 0f;
        }

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
            // Checked with every lock that has a key opened (the shrine): what is behind it must be reachable
            // then, and nothing beyond it may leak out.
            var openable = new HashSet<string>(GameData.LoadFromResources().Clues.locks.Where(l => l.Openable || l.OpensLater).Select(l => l.id));
            var opened = Object.FindObjectsByType<LockedDoor>(FindObjectsSortMode.None)
                .Where(d => openable.Contains(d.LockId))
                .SelectMany(d => d.Leaves.Where(l => l != null).SelectMany(l => l.GetComponentsInChildren<Collider>())
                    .Concat(d.GetComponentsInChildren<Collider>()))
                .Where(c => c.enabled).Distinct().ToList();
            foreach (var c in opened) c.enabled = false;
            try
            {
                return VerifyWalkabilityNow(mapX, mapZ, mapHalf);
            }
            finally
            {
                foreach (var c in opened) c.enabled = true;
                Physics.SyncTransforms();
            }
        }

        private static string VerifyWalkabilityNow(float mapX, float mapZ, float mapHalf)
        {
            Physics.SyncTransforms();
            var bounds = new Bounds(new Vector3(0f, 2f, -6f), new Vector3(120f, 12f, 140f));
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            sources.RemoveAll(s => s.component != null && s.component.GetComponent<Collider>() is Collider c && c.isTrigger);
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentHeight = Duck.DuckHeight;   // players duck under low lintels
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
            ("헛간 마당", new Vector2(27f, 31.5f)),
        };

        // Invisible walls (centre x, z, size x, size z): the outer yard (바깥마당), which 도 검률 has closed
        // (the 중문 and the gap beside the thatched shed), and a side gate with no door leaf.
        private static readonly (Vector4 box, string lockId)[] Blockers =
        {
            (new Vector4(-2.1f, -17.2f, 0.6f, 2.4f), "outer"),
            // Gap between the thatched shed and the 행랑채 west arm. Reaches up to the shed's south edge: the yard
            // ground now ramps up to the stone footings there, so the old ledge no longer stops anyone.
            (new Vector4(-13.5f, -16.2f, 6.2f, 3.2f), "outer"),
            (new Vector4(-11.0f, -12.8f, 0.5f, 4.4f), "outer"),     // the shed's open east side, from the yard wall's end down
            (new Vector4(18.8f, 27.9f, 1.4f, 2.6f), "eastgate"),    // east side gate by the shrine, out to the thatched barn
        };

        /// <summary>
        /// Every door leaf (room doors and gates): can a player get from one side to the other through it?
        /// Bakes a temporary NavMesh with the player's body and asks for a short path between points 0.9 m in
        /// front of and behind the leaf. Returns one line per door: OK, BLOCKED (no path) or AROUND (only a
        /// long detour, i.e. the doorway itself is shut), with the gates marked.
        /// </summary>
        public static string VerifyDoors()
        {
            Physics.SyncTransforms();
            var instance = BakeTempNavMesh();
            try
            {
                var leaves = new List<Transform>();
                foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                {
                    if (!IsDoorLeaf(mf.transform)) continue;
                    var root = DoorLeafRoot(mf.transform);
                    if (root != null && !leaves.Contains(root)) leaves.Add(root);
                }
                var sb = new StringBuilder();
                int ok = 0, bad = 0;
                foreach (var leaf in leaves.OrderBy(l => l.position.x))
                {
                    var rs = leaf.GetComponentsInChildren<Renderer>(true);
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    var normal = b.size.x < b.size.z ? Vector3.right : Vector3.forward;
                    var gate = !leaf.TryGetComponent<ProximityDoor>(out _);
                    var a = Side(b, normal);
                    var c = Side(b, -normal);
                    string result;
                    if (a == null || c == null) result = "NOFLOOR";
                    else
                    {
                        var path = new NavMeshPath();
                        var found = NavMesh.CalculatePath(a.Value, c.Value, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                        var len = found ? PathLength(path) : -1f;
                        result = !found ? "BLOCKED" : len > 4f ? "AROUND" : "OK";
                    }
                    if (result == "OK") ok++; else bad++;
                    if (result != "OK") sb.AppendLine($"{result,-8} {(gate ? "gate" : "room")} {leaf.name} @({b.center.x:F1},{b.min.y:F2},{b.center.z:F1})");
                }
                return $"doors {leaves.Count}: {ok} ok, {bad} not\n" + sb;
            }
            finally
            {
                NavMesh.RemoveNavMeshData(instance);
            }
        }

        private static Vector3? Side(Bounds door, Vector3 normal)
        {
            var p = door.center + normal * 0.9f;
            var floor = Physics.RaycastAll(new Vector3(p.x, door.min.y + 1.0f, p.z), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => h.normal.y > 0.6f).OrderBy(h => h.distance).Select(h => (float?)h.point.y).FirstOrDefault();
            if (floor == null) return null;
            return NavMesh.SamplePosition(new Vector3(p.x, floor.Value, p.z), out var hit, 0.6f, NavMesh.AllAreas) ? hit.position : (Vector3?)null;
        }

        /// <summary>
        /// Places a player can reach only ducked. Doorways are expected (a cell or two); a large patch means a
        /// crawl space (under a raised floor or 툇마루) that players would slip into by walking at its edge.
        /// </summary>
        public static string VerifyDuckOnly()
        {
            Physics.SyncTransforms();
            var instance = BakeTempNavMesh();
            try
            {
                var tri = NavMesh.CalculateTriangulation();
                var start = SampleOrNull(new Vector3(Discussion.x, 0.5f, Discussion.y));
                var cells = new Dictionary<Vector2Int, Vector3>();
                for (var i = 0; i < tri.indices.Length; i += 3)
                {
                    var a = tri.vertices[tri.indices[i]];
                    var b = tri.vertices[tri.indices[i + 1]];
                    var c = tri.vertices[tri.indices[i + 2]];
                    for (var u = 0.1f; u < 1f; u += 0.2f)
                    for (var v = 0.1f; u + v < 1f; v += 0.2f)
                    {
                        var p = a + (b - a) * u + (c - a) * v;
                        var key = new Vector2Int(Mathf.FloorToInt(p.x / 0.5f), Mathf.FloorToInt(p.z / 0.5f));
                        if (cells.ContainsKey(key)) continue;
                        var bottom = p + Vector3.up * (BodyRadius + 0.2f);
                        var top = p + Vector3.up * (BodyHeight - BodyRadius);
                        if (Physics.CheckCapsule(bottom, top, BodyRadius * 0.95f, ~0, QueryTriggerInteraction.Ignore)) cells[key] = p;
                    }
                }

                // Flood-fill neighbouring cells into patches.
                var seen = new HashSet<Vector2Int>();
                var report = new List<string>();
                foreach (var cell in cells.Keys)
                {
                    if (!seen.Add(cell)) continue;
                    var patch = new List<Vector2Int> { cell };
                    for (var k = 0; k < patch.Count; k++)
                        foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                        {
                            var n = patch[k] + d;
                            if (cells.ContainsKey(n) && seen.Add(n)) patch.Add(n);
                        }
                    if (patch.Count < 6) continue;
                    var at = cells[patch[patch.Count / 2]];
                    var reachable = false;
                    if (start != null)
                    {
                        var path = new NavMeshPath();
                        reachable = NavMesh.CalculatePath(start.Value, at, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                    }
                    report.Add($"{(reachable ? "REACH" : "sealed")} {patch.Count * 0.25f:F1} m² around ({at.x:F1}, {at.y:F2}, {at.z:F1})");
                }
                return $"duck-only patches ≥ 1.5 m²: {report.Count}\n" + string.Join("\n", report);
            }
            finally
            {
                NavMesh.RemoveNavMeshData(instance);
            }
        }

        private static NavMeshDataInstance BakeTempNavMesh()
        {
            var bounds = new Bounds(new Vector3(0f, 2f, -6f), new Vector3(140f, 12f, 150f));
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentHeight = Duck.DuckHeight;   // players duck under low lintels
            settings.agentRadius = BodyRadius;
            settings.agentClimb = AssistStep;
            settings.agentSlope = 40f;
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            return NavMesh.AddNavMeshData(data);
        }

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
