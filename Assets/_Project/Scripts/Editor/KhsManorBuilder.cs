using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

            var footings = FindFootings(house);
            var root = new GameObject("Manor_KHS").transform;
            BuildGround(Group(root, "Ground"), footings);
            Debug.Log($"[KhsManorBuilder] ground raised under {footings.Count} footings");
            BuildStepStones(Group(root, "StepStones"));
            Physics.SyncTransforms();
            Debug.Log($"[KhsManorBuilder] {BuildDoorSteps(Group(root, "DoorSteps"), house)} stepping stones in front of high doors");
            BuildBlockers(Group(root, "Blockers"));
            BuildPartitions(Group(root, "Partitions"));
            Physics.SyncTransforms();
            var settled = SettleFloatingPieces(house);
            Debug.Log($"[KhsManorBuilder] settled {settled} loose stones/jars onto the ground");
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

        // Loose ground pieces. The scan's ground undulates, the yard here is flat, so where it was lower they float.
        private static readonly string[] LoosePieces =
            { "SM_Stone", "SM_Jangdokdae", "SM_FoundationStone", "SM_Gamasot", "SM_Gidan", "SM_Seokkalae" };

        /// <summary>
        /// Drops loose pieces that touch nothing below them onto the nearest surface. Pieces stacked or nested
        /// together (a jar on its upturned-lid stand) move as one group, so they cannot hold each other up.
        /// Returns how many pieces moved.
        /// </summary>
        public static int SettleFloatingPieces(Transform house)
        {
            var loose = new List<(Transform t, Bounds b)>();
            foreach (Transform piece in house)
            {
                if (!LoosePieces.Any(k => piece.name.StartsWith(k)) || piece.name.StartsWith("SM_StoneWall") ||
                    piece.name.StartsWith("SM_Stonewall")) continue;
                var renderers = piece.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                var bb = renderers[0].bounds;
                foreach (var rr in renderers) bb.Encapsulate(rr.bounds);
                loose.Add((piece, bb));
            }

            // Union-find over stacked pieces: bounds overlap and one sits over the other's middle. Spline-built
            // borders and paths are long and only lie beside things, so they never join a stack.
            bool Stackable(Transform t) => !t.name.Contains("_Splines");
            bool Over(Bounds a, Bounds c) =>
                Mathf.Abs(a.center.x - c.center.x) < Mathf.Max(a.extents.x, c.extents.x) * 0.7f &&
                Mathf.Abs(a.center.z - c.center.z) < Mathf.Max(a.extents.z, c.extents.z) * 0.7f;
            var parent = Enumerable.Range(0, loose.Count).ToArray();
            int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
            for (var i = 0; i < loose.Count; i++)
            for (var j = i + 1; j < loose.Count; j++)
            {
                if (!Stackable(loose[i].t) || !Stackable(loose[j].t)) continue;
                var a = loose[i].b;
                var c = loose[j].b;
                a.Expand(0.02f);
                if (a.Intersects(c) && Over(a, c)) parent[Find(i)] = Find(j);
            }

            var moved = 0;
            foreach (var group in Enumerable.Range(0, loose.Count).GroupBy(Find))
            {
                var members = group.Select(i => loose[i]).ToList();
                var bounds = members[0].b;
                foreach (var m in members) bounds.Encapsulate(m.b);
                var gap = FloatGap(bounds, members.Select(m => m.t).ToList());
                if (gap <= 0.05f) continue;
                foreach (var m in members) m.t.position -= new Vector3(0f, gap, 0f);
                Physics.SyncTransforms();
                moved += members.Count;
            }
            return moved;
        }

        /// <summary>Height of the bounds above the nearest outside surface, 0 when resting on or sunk into one.</summary>
        private static float FloatGap(Bounds b, List<Transform> group)
        {
            var e = b.extents * 0.6f;
            var best = float.MaxValue;
            foreach (var off in new[] { Vector3.zero, new Vector3(e.x, 0f, e.z), new Vector3(-e.x, 0f, e.z), new Vector3(e.x, 0f, -e.z), new Vector3(-e.x, 0f, -e.z) })
            {
                var p = b.center + off;
                var support = float.MinValue;
                var touching = false;
                foreach (var h in Physics.RaycastAll(new Vector3(p.x, b.max.y + 6f, p.z), Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (group.Any(g => h.collider.transform.IsChildOf(g))) continue;
                    if (h.point.y >= b.min.y - 0.05f && h.point.y <= b.max.y) touching = true;
                    if (h.point.y < b.min.y && h.point.y > support) support = h.point.y;
                }
                best = Mathf.Min(best, touching ? 0f : support == float.MinValue ? 0f : b.min.y - support);
            }
            return best == float.MaxValue ? 0f : best;
        }

        // Gates that stay shut (centre x, z): the compound's outer gates and the shrine. Every other door leaf,
        // room door or inner gate, opens for whoever comes near.
        private static readonly Vector2[] LockedGates =
        {
            new Vector2(-38.0f, 0.7f),   // west outer gate
            new Vector2(11.8f, 14.7f),   // shrine (사당) gate
            new Vector2(22.6f, 30.1f),   // north-east gates to the barn yard
            new Vector2(23.4f, 34.0f),
            new Vector2(-2.05f, -17.4f), // 중문 to the outer yard (also behind a blocker)
        };

        private static bool IsLockedGate(Vector3 leafCenter) =>
            LockedGates.Any(g => Vector2.Distance(g, new Vector2(leafCenter.x, leafCenter.z)) < 1.2f);

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
        private const float MoundSlope = 0.35f;     // m of drop per m away from a raised footing (~19°)
        private const float MoundPlateau = 1.0f;    // keeps rows of footings on one level between them

        // Parts that stand directly on the ground and so tell where the scanned ground really was.
        private static readonly string[] Footings =
            { "SM_Pillar", "SM_FoundationStone", "SM_StoneWall", "SM_Stonewall", "SM_Wall", "SM_Gidan", "SM_Stone_Tile",
              "SM_chimney", "SM_DoorFrame", "SM_GateDoorFrame", "SM_Stone01C_Splines", "SM_Stone01D_Splines" };

        private struct Footing
        {
            public Rect Area;
            public float Height;
        }

        /// <summary>
        /// The scan's ground is not flat: under the 행랑채, the gates and parts of the walls it was up to ~0.8 m
        /// higher than the yards, so on a flat yard those footings float. Every footing that stands above the
        /// yard on nothing of the house becomes a raised patch, and the ground mesh rises to meet it with a
        /// gentle slope — recreating the original lie of the land instead of moving the buildings.
        /// Called before the ground exists, so a ray below a footing only meets house parts.
        /// </summary>
        private static List<Footing> FindFootings(Transform house)
        {
            var list = new List<Footing>();
            foreach (Transform piece in house)
            {
                if (!Footings.Any(k => piece.name.StartsWith(k))) continue;
                var renderers = piece.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                var b = renderers[0].bounds;
                foreach (var rr in renderers) b.Encapsulate(rr.bounds);
                if (b.min.y < 0.04f || b.min.y > 1.2f) continue;
                // Standing on another house part (a post on a platform)? Then it is not on the ground.
                var onHouse = Physics.RaycastAll(new Vector3(b.center.x, b.min.y + 0.02f, b.center.z), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore)
                    .Any(h => !h.collider.transform.IsChildOf(piece) && h.point.y > -0.05f);
                if (onHouse) continue;
                list.Add(new Footing { Area = new Rect(b.min.x, b.min.z, b.size.x, b.size.z), Height = b.min.y + 0.03f });
            }
            return list;
        }

        private static float GroundHeight(float x, float z, List<Footing> footings)
        {
            var h = 0f;
            foreach (var f in footings)
            {
                var dx = Mathf.Max(f.Area.xMin - x, 0f, x - f.Area.xMax);
                var dz = Mathf.Max(f.Area.yMin - z, 0f, z - f.Area.yMax);
                var d = Mathf.Sqrt(dx * dx + dz * dz);
                var v = f.Height - MoundSlope * Mathf.Max(0f, d - MoundPlateau);
                if (v > h) h = v;
            }
            return h;
        }

        private static void BuildGround(Transform p, List<Footing> footings)
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
                vertices[j * nx + i] = new Vector3(x, GroundHeight(x, z, footings), z);
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

        // A door whose sill is more than this above the ground in front of it gets a stepping stone.
        private const float DoorStepMin = 0.4f;
        // Higher than two steps: not meant to be entered from that side (a window over a drop).
        private const float DoorStepMax = 1.2f;

        /// <summary>
        /// Raised rooms and 툇마루 have their doors 0.5-1 m above the yard, and the scan seldom puts a 섬돌 in
        /// front of them. Every openable door gets one on each side that is too far below its sill, halfway up,
        /// unless something to step on is already there (a second leaf of the same doorway finds the first one).
        /// </summary>
        private static int BuildDoorSteps(Transform p, Transform house)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(KhsAssetTools.Root + "/KimMyeonggwanHouse/Material/MI_GidanStone01A.mat");
            var leaves = house.GetComponentsInChildren<ProximityDoor>().Select(d => d.transform).ToList();
            var count = 0;
            foreach (var leaf in leaves)
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
                var width = Mathf.Max(Mathf.Abs(Vector3.Dot(b.size, Vector3.Cross(Vector3.up, through))), 0.7f);

                if (!Physics.Raycast(new Vector3(b.center.x, b.min.y + 0.3f, b.center.z), Vector3.down, out var sillHit, 1.5f, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                var sill = sillHit.point.y;

                foreach (var sign in new[] { 1f, -1f })
                {
                    var outward = through * sign;
                    var floor = WalkableBelow(b.center + outward * 0.8f, sill + 0.3f);
                    if (floor == null || sill - floor.Value <= DoorStepMin || sill - floor.Value > DoorStepMax) continue;
                    // Already something to step on just outside?
                    var near = WalkableBelow(b.center + outward * 0.45f, sill + 0.05f);
                    if (near != null && near.Value > floor.Value + 0.2f) continue;

                    const float depth = 0.45f;
                    var top = floor.Value + (sill - floor.Value) / 2f;
                    var bottom = floor.Value - 0.3f;
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = $"Door step stone ({leaf.name})";
                    go.transform.SetParent(p, false);
                    go.transform.SetPositionAndRotation(
                        new Vector3(b.center.x, (top + bottom) / 2f, b.center.z) + outward * (0.14f + depth / 2f),
                        Quaternion.LookRotation(outward));
                    go.transform.localScale = new Vector3(width + 0.1f, top - bottom, depth);
                    if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
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

        // ---------------------------------------------------------------- props (KHS UnhyeongungCollect)

        private const string PropDir = KhsAssetTools.Root + "/UnhyeongungCollect/Prefabs/";

        private struct PropDef
        {
            public string Label;
            public float X, Z, Yaw, Above;   // Above: stand on top of another prop of this height
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

                // Sit the lowest point of the piece on the floor (or on the prop it stands on).
                var renderers = holder.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                var bounds = renderers[0].bounds;
                foreach (var rr in renderers) bounds.Encapsulate(rr.bounds);
                var floor = FloorY(def.X, def.Z) + def.Above;
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
            ("사당", new Vector2(12f, 22f)),
        };

        // Invisible walls (centre x, z, size x, size z): the outer yard (바깥마당), which 도 검률 has closed
        // (the 중문 and the gap beside the thatched shed), and a side gate with no door leaf.
        private static readonly Vector4[] Blockers =
        {
            new Vector4(-2.1f, -17.2f, 0.6f, 2.4f),
            // Gap between the thatched shed and the 행랑채 west arm. Reaches up to the shed's south edge: the yard
            // ground now ramps up to the stone footings there, so the old ledge no longer stops anyone.
            new Vector4(-13.5f, -16.2f, 6.2f, 3.2f),
            new Vector4(-11.0f, -12.8f, 0.5f, 4.4f), // the shed's open east side, from the yard wall's end down
            new Vector4(18.8f, 27.9f, 1.4f, 2.6f),   // east side gate by the shrine, out to the thatched barn
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
