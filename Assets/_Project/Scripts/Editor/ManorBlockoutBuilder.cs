using System.Collections.Generic;
using System.IO;
using System.Linq;
using MurderMystery.Investigation;
using MurderMystery.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MurderMystery.EditorTools
{
    /// <summary>
    /// Generates the grey-box manor scene from docs/design/map_layout.md.
    /// 1 unit = 1 m, origin = main gate centre, +X east, +Z north.
    /// Re-running rebuilds the scene from scratch, so edit coordinates here, not in the scene.
    /// </summary>
    public static class ManorBlockoutBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Manor_Blockout.unity";
        private const string MatDir = "Assets/_Project/Art/Materials/Blockout";

        private const float WallT = 0.15f;      // interior wall thickness
        private const float RoomH = 2.4f;       // floor to ceiling
        private const float DoorH = 2.0f;
        private const float BoundaryT = 0.4f;   // compound wall thickness

        private enum Side { S, N, W, E }

        private struct Door
        {
            public Side Side;
            public float Center;
            public float Width;

            public Door(Side side, float center, float width)
            {
                Side = side;
                Center = center;
                Width = width;
            }
        }

        private static Dictionary<string, Material> mats;

        [MenuItem("Tools/Manor/Build Blockout Scene")]
        public static void BuildScene()
        {
            EnsureFolder("Assets/_Project/Scenes");
            EnsureFolder(MatDir);
            CreateMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("Manor_Blockout").transform;
            BuildGround(Group(root, "Ground"));
            BuildWalls(Group(root, "Walls"));
            var roofs = Group(root, "Roofs");
            BuildOuterZone(Group(root, "OuterZone"), roofs);
            BuildSarangchae(Group(root, "Sarangchae (사랑채)"), roofs);
            BuildAnchae(Group(root, "Anchae (안채)"), roofs);
            BuildByeoldang(Group(root, "Byeoldang (별당)"), roofs);
            BuildMisc(Group(root, "Misc"), roofs);
            BuildSpots(Group(root, "InvestigationSpots"));
            BuildMarkers(Group(root, "GameplayMarkers"));
            BuildLightAndCamera();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ManorBlockoutBuilder] Saved {ScenePath}");
        }

        // ---------------------------------------------------------------- ground & walls

        private static void BuildGround(Transform p)
        {
            Box(p, "Ground_Outer (바깥마당)", -24, 24, -0.1f, 0f, 0, 12, mats["OuterGround"]);
            Box(p, "Ground_Inner (안쪽 흙마당)", -24, 24, -0.1f, 0f, 12, 50, mats["Mud"]);

            var pond = Cylinder(p, "Pond (연못)", 14f, 16.5f, -0.05f, 0.02f, 8f, 5f, mats["Water"]);
            Object.DestroyImmediate(pond.GetComponent<Collider>());
        }

        private static void BuildWalls(Transform p)
        {
            const float h = 2.4f;
            // Compound boundary. The south side is formed by the haengnang wings and the gate.
            Box(p, "Wall_North", -24, 24, 0, h, 50 - BoundaryT, 50, mats["Wall"]);
            Box(p, "Wall_West", -24, -24 + BoundaryT, 0, h, 0, 50, mats["Wall"]);
            Box(p, "Wall_East", 24 - BoundaryT, 24, 0, h, 0, 50, mats["Wall"]);

            // Inner wall with the jungmun gap (X -2..2).
            Box(p, "Wall_Inner_W (내담)", -24, -2, 0, 2.0f, 12 - BoundaryT / 2, 12 + BoundaryT / 2, mats["Wall"]);
            Box(p, "Wall_Inner_E (내담)", 2, 24, 0, 2.0f, 12 - BoundaryT / 2, 12 + BoundaryT / 2, mats["Wall"]);

            // Saitdam between the back yard and the anmadang, gap X -2..0 (사잇문).
            Box(p, "Wall_Sait_W (사잇담)", -24, -2, 0, 1.6f, 34 - 0.15f, 34 + 0.15f, mats["Wall"]);
            Box(p, "Wall_Sait_E (사잇담)", 0, 24, 0, 1.6f, 34 - 0.15f, 34 + 0.15f, mats["Wall"]);

            // Wall closing off the shrine yard from the kitchen wing side.
            Box(p, "Wall_Shrine_E", -10.6f, -10.3f, 0, 1.6f, 34, 50, mats["Wall"]);

            // Low wall between sarangmadang and the byeoldang yard, hyeopmun gap Z 17..19.
            Box(p, "Wall_Byeoldang_S (낮은 담)", 3.85f, 4.15f, 0, 1.2f, 12, 17, mats["Wall"]);
            Box(p, "Wall_Byeoldang_N (낮은 담)", 3.85f, 4.15f, 0, 1.2f, 19, 34, mats["Wall"]);
        }

        // ---------------------------------------------------------------- buildings

        private static void BuildOuterZone(Transform p, Transform roofs)
        {
            // Haengnang wings double as the south compound wall.
            Platform(p, "Haengnang_W_Base", -24, -2, 0, 4, 0.3f);
            Room(p, "Haengnang_W (행랑채, 도 검률 묵은 방)", -24, -2, 0, 4, 0.3f,
                new[] { new Door(Side.N, -4, 1.0f), new Door(Side.N, -12, 1.0f) });
            Platform(p, "Haengnang_E_Base", 2, 24, 0, 4, 0.3f);
            Room(p, "Haengnang_E (행랑채, 문간방·문지기)", 2, 24, 0, 4, 0.3f,
                new[] { new Door(Side.N, 4, 1.0f), new Door(Side.N, 14, 1.0f) });
            Roof(roofs, "Roof_Haengnang_W", -24, -2, -0.5f, 4.8f, 0.3f + RoomH + 0.2f);
            Roof(roofs, "Roof_Haengnang_E", 2, 24, -0.5f, 4.8f, 0.3f + RoomH + 0.2f);

            // Main gate (closed) and the jungmun (barred).
            Box(p, "Daemun_Post_W", -2.3f, -2f, 0, 3.2f, 0, 1, mats["Wood"]);
            Box(p, "Daemun_Post_E", 2f, 2.3f, 0, 3.2f, 0, 1, mats["Wood"]);
            Box(p, "Daemun_Door (대문, 닫힘)", -2f, 2f, 0, 2.8f, 0.4f, 0.6f, mats["WoodDark"]);
            Roof(roofs, "Roof_Daemun", -2.6f, 2.6f, -0.4f, 1.4f, 3.3f);

            Box(p, "Jungmun_Post_W", -2.3f, -2f, 0, 2.8f, 11.7f, 12.3f, mats["Wood"]);
            Box(p, "Jungmun_Post_E", 2f, 2.3f, 0, 2.8f, 11.7f, 12.3f, mats["Wood"]);
            Box(p, "Jungmun_Door (중문, 닫힘 · 이동 차단)", -2f, 2f, 0, 2.4f, 11.9f, 12.1f, mats["WoodDark"]);
            Roof(roofs, "Roof_Jungmun", -2.6f, 2.6f, 11.3f, 12.7f, 2.9f);
        }

        private static void BuildSarangchae(Transform p, Transform roofs)
        {
            const float b = 0.6f; // gidan height
            Platform(p, "Gidan (기단)", -22, 2, 24, 31, b);
            Box(p, "Toetmaru (툇마루)", -22, 2, b - 0.02f, b + 0.02f, 24, 25.5f, mats["Maru"]);

            Room(p, "Seojae (서재)", -22, -17, 25.5f, 31, b,
                new[] { new Door(Side.S, -19.5f, 1.2f), new Door(Side.E, 28.25f, 1.4f) });
            Room(p, "Sarangbang (사랑방)", -17, -10, 25.5f, 31, b,
                new[] { new Door(Side.S, -13.5f, 1.6f), new Door(Side.E, 28f, 1.2f) }, Side.W);
            Room(p, "Daecheong (대청)", -10, -5, 25.5f, 31, b, new Door[0], Side.S, Side.W, Side.E);
            Box(p, "Daecheong_Floor (대청 마루)", -10, -5, b - 0.02f, b + 0.02f, 25.5f, 31, mats["Maru"]);
            Room(p, "Jageunsarang (작은사랑, 재하 방)", -5, 2, 25.5f, 31, b,
                new[] { new Door(Side.S, -1.5f, 1.2f), new Door(Side.W, 28f, 1.0f) });

            // Stepping stones in front of each room and the central stair to the daecheong.
            Box(p, "Seomdol_Seojae (섬돌)", -20.0f, -19.0f, 0, 0.3f, 23.2f, 23.9f, mats["Stone"]);
            Box(p, "Seomdol_Sarangbang (섬돌)", -14.0f, -13.0f, 0, 0.3f, 23.2f, 23.9f, mats["Stone"]);
            Box(p, "Stair_Daecheong_1 (대청 계단)", -8.5f, -6.5f, 0, 0.2f, 22.8f, 23.4f, mats["Stone"]);
            Box(p, "Stair_Daecheong_2 (대청 계단)", -8.5f, -6.5f, 0, 0.4f, 23.4f, 24.0f, mats["Stone"]);
            Box(p, "Seomdol_Jageunsarang (섬돌)", -2.0f, -1.0f, 0, 0.3f, 23.2f, 23.9f, mats["Stone"]);

            // Props.
            Box(p, "Prop_Seoan (서안)", -20.1f, -18.9f, b, b + 0.35f, 27.7f, 28.3f, mats["WoodDark"]);
            Cylinder(p, "Prop_Chotdae (촛대)", -18.95f, 28.1f, b + 0.35f, b + 0.75f, 0.08f, 0.08f, mats["Metal"]);
            Box(p, "Prop_Mungap (문갑)", -21.6f, -20.4f, b, b + 0.5f, 30.3f, 30.75f, mats["WoodDark"]);
            Cylinder(p, "Prop_Hwaro (화로)", -13.5f, 27.5f, b, b + 0.35f, 0.5f, 0.5f, mats["Metal"]);
            Cylinder(p, "Prop_Soban (소반)", -12f, 28.5f, b, b + 0.3f, 0.5f, 0.5f, mats["Wood"]);
            Cylinder(p, "Prop_Tangyak_Bowl (탕약 사발)", -12f, 28.5f, b + 0.3f, b + 0.38f, 0.16f, 0.16f, mats["Ceramic"]);
            Box(p, "Prop_Sabangtakja_Drawer (사방탁자 서랍)", -16.3f, -15.7f, b, b + 1.2f, 30.3f, 30.75f, mats["WoodDark"]);
            Box(p, "Prop_Bookshelf (작은사랑 서가)", -0.6f, 0.6f, b, b + 1.5f, 30.4f, 30.8f, mats["WoodDark"]);
            Cylinder(p, "Prop_OilBottle (빈 기름병)", -4.5f, 25.2f, b, b + 0.25f, 0.12f, 0.12f, mats["Ceramic"]);
            foreach (var x in new[] { -19.5f, -13.5f, -7.5f, -1.5f })
                Cylinder(p, $"Prop_Deungjan (등잔) {x}", x, 24.3f, b, b + 0.5f, 0.1f, 0.1f, mats["Metal"]);

            // Victim placeholder lying at the desk.
            var victim = Capsule(p, "Victim_Placeholder (윤 대감)", -19.5f, 27.2f, b + 0.25f, mats["Victim"]);
            victim.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            victim.transform.localScale = new Vector3(0.45f, 0.8f, 0.45f);

            Roof(roofs, "Roof_Sarangchae", -23, 3, 23, 32, b + RoomH + 0.4f);
        }

        private static void BuildAnchae(Transform p, Transform roofs)
        {
            const float b = 0.5f;
            // North wing.
            Platform(p, "Gidan_North (기단)", -4, 16, 43, 48, b);
            Box(p, "Toetmaru_North (툇마루)", -4, 16, b - 0.02f, b + 0.02f, 43, 44, mats["Maru"]);
            Room(p, "Anbang (안방, 빈방)", -4, 3, 44, 48, b,
                new[] { new Door(Side.S, -0.5f, 1.2f), new Door(Side.E, 46f, 1.0f) });
            Room(p, "Andaecheong (안대청)", 3, 9, 44, 48, b, new Door[0], Side.S, Side.W, Side.E);
            Box(p, "Andaecheong_Floor (안대청 마루)", 3, 9, b - 0.02f, b + 0.02f, 44, 48, mats["Maru"]);
            Room(p, "Geonneonbang (건넌방, 정씨)", 9, 16, 44, 48, b,
                new[] { new Door(Side.S, 12.5f, 1.2f), new Door(Side.S, 14.8f, 0.9f), new Door(Side.W, 46f, 1.0f) });
            Roof(roofs, "Roof_Anchae_North", -5, 17, 42, 49, b + RoomH + 0.4f);

            // West wing: earthen-floor kitchen and the pantry / Buni's room.
            Room(p, "Bueok (부엌)", -10, -4, 39, 46, 0f,
                new[] { new Door(Side.E, 42f, 1.4f) });
            Platform(p, "Gidan_Chanbang", -10, -4, 35, 39, b);
            Room(p, "Chanbang_Buni (찬방·분이 방)", -10, -4, 35, 39, b,
                new[] { new Door(Side.E, 37f, 1.0f) }, Side.N);
            Roof(roofs, "Roof_Anchae_West", -11, -3, 34.5f, 47, RoomH + 0.6f);

            // Storehouse in the SW corner of the anmadang, door facing the kitchen side.
            Platform(p, "Gidan_Gotgan", -4, 0, 35, 38, 0.3f);
            Room(p, "Gotgan (곳간)", -4, 0, 35, 38, 0.3f, new[] { new Door(Side.N, -2f, 1.0f) });
            Roof(roofs, "Roof_Gotgan", -4.4f, 0.4f, 34.6f, 38.4f, 0.3f + RoomH + 0.2f);

            // Props.
            Box(p, "Prop_Agungi (아궁이)", -7.8f, -6.2f, 0, 0.6f, 43.2f, 44.0f, mats["Stone"]);
            Cylinder(p, "Prop_Yaktanggwan (약탕관)", -7f, 43.6f, 0.6f, 0.9f, 0.3f, 0.3f, mats["Ceramic"]);
            Cylinder(p, "Prop_Ssaldok (쌀독)", -8.5f, 40f, 0, 0.8f, 0.6f, 0.6f, mats["Ceramic"]);
            Cylinder(p, "Prop_RatPoisonJar (쥐약 단지)", -2f, 36.5f, 0.3f, 0.7f, 0.3f, 0.3f, mats["Ceramic"]);
            Box(p, "Prop_Bandaji (반닫이)", 13.5f, 14.5f, b, b + 0.7f, 47.3f, 47.75f, mats["WoodDark"]);
        }

        private static void BuildByeoldang(Transform p, Transform roofs)
        {
            const float b = 0.6f;
            Platform(p, "Gidan (기단)", 10, 20, 21, 28, b);
            Box(p, "Toetmaru (툇마루)", 10, 20, b - 0.02f, b + 0.02f, 21, 22, mats["Maru"]);
            Room(p, "Room_Heo (허 의원 방)", 10, 15, 22, 28, b, new[] { new Door(Side.S, 12.5f, 1.2f) });
            Room(p, "Room_Byeon (변 행수 방)", 15, 20, 22, 28, b, new[] { new Door(Side.S, 17.5f, 1.2f) }, Side.W);
            Box(p, "Seomdol_Heo (섬돌)", 12.0f, 13.0f, 0, 0.3f, 20.2f, 20.9f, mats["Stone"]);
            Box(p, "Seomdol_Byeon (섬돌)", 17.0f, 18.0f, 0, 0.3f, 20.2f, 20.9f, mats["Stone"]);
            Roof(roofs, "Roof_Byeoldang", 9, 21, 20, 29, b + RoomH + 0.4f);

            Box(p, "Prop_MedicineChest (허 의원 약상자)", 11.75f, 12.25f, b, b + 0.35f, 25.8f, 26.2f, mats["WoodDark"]);
            Box(p, "Prop_Heo_Bundle (허 의원 봇짐)", 13.75f, 14.25f, b, b + 0.3f, 26.8f, 27.2f, mats["Cloth"]);
            Box(p, "Prop_Byeon_Bundle (변 행수 짐 보따리)", 18.15f, 18.85f, b, b + 0.45f, 26.75f, 27.25f, mats["Cloth"]);
            Box(p, "Prop_Jinsin_Shoes (진흙 진신)", 17.3f, 17.7f, b, b + 0.12f, 21.35f, 21.65f, mats["Leather"]);

            // Outhouse just inside the hyeopmun.
            Room(p, "Dwitgan (뒷간)", 5, 8, 13, 16, 0f, new[] { new Door(Side.W, 14.5f, 0.9f) });
            Roof(roofs, "Roof_Dwitgan", 4.7f, 8.3f, 12.7f, 16.3f, RoomH + 0.2f);
        }

        private static void BuildMisc(Transform p, Transform roofs)
        {
            // Locked shrine (background only).
            Platform(p, "Sadang_Base (사당)", -22, -12, 37, 47, 0.5f);
            Box(p, "Sadang_Block (사당, 잠김)", -21, -13, 0.5f, 3.0f, 38, 46, mats["Locked"]);
            Roof(roofs, "Roof_Sadang", -22, -12, 37, 47, 3.0f);

            Platform(p, "Jangdokdae (장독대)", 16, 22, 43, 48, 0.3f);
            for (var i = 0; i < 6; i++)
                Cylinder(p, $"Prop_Jangdok {i}", 17f + (i % 3) * 2f, 44.5f + (i / 3) * 2f, 0.3f, 1.0f, 0.7f, 0.7f, mats["Ceramic"]);

            Cylinder(p, "Well (우물)", 19f, 38f, 0, 0.7f, 1.2f, 1.2f, mats["Stone"]);
        }

        // ---------------------------------------------------------------- gameplay data

        private static void BuildSpots(Transform p)
        {
            Spot(p, "01", "서재 서안", -19.5f, 0.95f, 28.0f, SpotLayer.Surface, "N1");
            Spot(p, "02", "서재 문갑", -21.0f, 1.1f, 30.5f, SpotLayer.Surface, "C4", "D1", "D2");
            Spot(p, "03", "서재 문지방", -19.5f, 0.6f, 25.6f, SpotLayer.Surface, "C3b");
            Spot(p, "04", "사랑방 화로", -13.5f, 0.95f, 27.5f, SpotLayer.Surface, "C1");
            Spot(p, "05", "사랑방 소반", -12.0f, 0.95f, 28.5f, SpotLayer.Surface, "E1");
            Spot(p, "06", "사랑방 서랍", -16.0f, 1.2f, 30.5f, SpotLayer.Surface, "C5");
            Spot(p, "07", "대청·툇마루 바닥", -7.5f, 0.6f, 25.0f, SpotLayer.Surface, "C3a");
            Spot(p, "08a", "작은사랑 앞 섬돌", -1.5f, 0.3f, 23.5f, SpotLayer.Surface, "C3c");
            Spot(p, "08b", "사랑방 앞 섬돌", -13.5f, 0.3f, 23.5f, SpotLayer.Surface, "C3c");
            Spot(p, "09", "등잔·빈 기름병", -4.5f, 0.8f, 25.2f, SpotLayer.Surface, "N3");
            Spot(p, "10", "작은사랑 서가", 0.0f, 1.3f, 30.4f, SpotLayer.Deep, "H4", "C7");
            Spot(p, "11", "곳간 쥐약 단지", -2.0f, 0.7f, 36.5f, SpotLayer.Surface, "C2");
            Spot(p, "12", "부엌 약탕관", -7.0f, 0.9f, 43.6f, SpotLayer.Surface, "N2");
            Spot(p, "13", "부엌 쌀독", -8.5f, 0.8f, 40.0f, SpotLayer.Deep, "H1");
            Spot(p, "14", "건넌방 반닫이", 14.0f, 1.2f, 47.4f, SpotLayer.Deep, "H3");
            Spot(p, "15", "허 의원 약상자", 12.0f, 0.95f, 26.0f, SpotLayer.Surface, "C6");
            Spot(p, "16", "허 의원 봇짐", 14.0f, 0.9f, 27.0f, SpotLayer.Deep, "H6");
            Spot(p, "17", "변 행수 짐 보따리", 18.5f, 1.05f, 27.0f, SpotLayer.Deep, "H5a", "H5b");
            Spot(p, "18", "별당 툇마루 진신", 17.5f, 0.72f, 21.5f, SpotLayer.Surface, "C8");
        }

        private static void BuildMarkers(Transform p)
        {
            Marker(p, "NPC_DoGeomnyul_Daecheong", MarkerType.NpcPoint, -7.5f, 0.6f, 28f, 0.5f);
            Marker(p, "NPC_DoGeomnyul_Stairs", MarkerType.NpcPoint, -7.5f, 0.6f, 24.6f, 0.5f);
            Marker(p, "DiscussionArea", MarkerType.DiscussionArea, -11f, 0f, 18f, 4f);
            for (var i = 0; i < 5; i++)
            {
                var a = Mathf.PI / 2f + i * Mathf.PI * 2f / 5f;
                Marker(p, $"PlayerSpawn_{i}", MarkerType.PlayerSpawn,
                    -11f + Mathf.Cos(a) * 3f, 0f, 18f + Mathf.Sin(a) * 3f, 0.4f);
            }

            var npc = Capsule(p, "NPC_Placeholder (도 검률)", -7.5f, 28f, 0.6f + 0.9f, mats["Npc"]);
            npc.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
        }

        private static void Spot(Transform p, string id, string name, float x, float y, float z, SpotLayer layer, params string[] clues)
        {
            var go = new GameObject($"Spot_{id} ({name})");
            go.transform.SetParent(p, false);
            go.transform.position = new Vector3(x, y, z);
            go.AddComponent<InvestigationSpot>().Configure(id, name, layer, clues);
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 1.2f;
        }

        private static void Marker(Transform p, string id, MarkerType type, float x, float y, float z, float radius)
        {
            var go = new GameObject(id);
            go.transform.SetParent(p, false);
            go.transform.position = new Vector3(x, y, z);
            go.AddComponent<GameplayMarker>().Configure(type, id, radius);
        }

        private static void BuildLightAndCamera()
        {
            var lightGo = new GameObject("Directional Light (비 갠 아침)");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.92f, 0.94f, 1f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(35f, -40f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.52f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.29f, 0.25f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.78f, 0.8f, 0.82f);
            RenderSettings.fogStartDistance = 25f;
            RenderSettings.fogEndDistance = 120f;

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.78f, 0.8f, 0.82f);
            cam.farClipPlane = 300f;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(6f, 22f, -6f);
            camGo.transform.LookAt(new Vector3(-6f, 0f, 26f));
        }

        // ---------------------------------------------------------------- capture (verification)

        /// <summary>
        /// Batch-mode helper: renders top-down (roofs hidden) and perspective views of the saved scene.
        /// Usage: -executeMethod MurderMystery.EditorTools.ManorBlockoutBuilder.CaptureViews -captureOut &lt;dir&gt;
        /// </summary>
        public static void CaptureViews()
        {
            var args = System.Environment.GetCommandLineArgs();
            var idx = System.Array.IndexOf(args, "-captureOut");
            var outDir = idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : "Logs/BlockoutCapture";
            Directory.CreateDirectory(outDir);

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roofs = GameObject.Find("Manor_Blockout/Roofs");

            var camGo = new GameObject("CaptureCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.78f, 0.8f, 0.82f);
            cam.farClipPlane = 400f;

            // Fog would wash out the high top-down camera.
            var fog = RenderSettings.fog;
            RenderSettings.fog = false;
            roofs.SetActive(false);
            cam.orthographic = true;
            cam.orthographicSize = 26.5f;
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 100f, 25f), Quaternion.Euler(90f, 0f, 0f));
            Render(cam, 1400, 1400, Path.Combine(outDir, "blockout_top.png"));

            RenderSettings.fog = fog;
            roofs.SetActive(true);
            cam.orthographic = false;
            cam.fieldOfView = 50f;
            camGo.transform.position = new Vector3(18f, 26f, -8f);
            camGo.transform.LookAt(new Vector3(-4f, 0f, 28f));
            Render(cam, 1600, 900, Path.Combine(outDir, "blockout_persp.png"));

            camGo.transform.position = new Vector3(-11f, 1.6f, 17f);
            camGo.transform.LookAt(new Vector3(-11f, 1.4f, 30f));
            cam.fieldOfView = 70f;
            Render(cam, 1600, 900, Path.Combine(outDir, "blockout_eye_sarangmadang.png"));

            Object.DestroyImmediate(camGo);
            Debug.Log($"[ManorBlockoutBuilder] Captures written to {Path.GetFullPath(outDir)}");
        }

        private static void Render(Camera cam, int w, int h, string path)
        {
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }

        // ---------------------------------------------------------------- geometry helpers

        private static void Room(Transform parent, string name, float x0, float x1, float z0, float z1, float floorY,
            Door[] doors, params Side[] skip)
        {
            var root = Group(parent, name);
            // Floor top sits 1 cm above the platform to avoid z-fighting.
            Box(root, "Floor", x0, x1, floorY - 0.04f, floorY + 0.01f, z0, z1, mats["Floor"]);
            foreach (var side in new[] { Side.S, Side.N, Side.W, Side.E })
            {
                if (skip.Contains(side)) continue;
                Wall(root, side, x0, x1, z0, z1, floorY, doors.Where(d => d.Side == side).ToArray());
            }
        }

        private static void Wall(Transform root, Side side, float x0, float x1, float z0, float z1, float floorY, Door[] doors)
        {
            var alongX = side == Side.S || side == Side.N;
            var a0 = alongX ? x0 : z0;
            var a1 = alongX ? x1 : z1;
            float c0, c1; // cross-axis extent, kept inside the room bounds
            switch (side)
            {
                case Side.S: c0 = z0; c1 = z0 + WallT; break;
                case Side.N: c0 = z1 - WallT; c1 = z1; break;
                case Side.W: c0 = x0; c1 = x0 + WallT; break;
                default: c0 = x1 - WallT; c1 = x1; break;
            }

            var top = floorY + RoomH;
            var cursor = a0;
            foreach (var d in doors.OrderBy(d => d.Center))
            {
                var g0 = Mathf.Max(a0, d.Center - d.Width / 2f);
                var g1 = Mathf.Min(a1, d.Center + d.Width / 2f);
                if (g0 > cursor) Seg(root, $"Wall_{side}", alongX, cursor, g0, c0, c1, floorY, top);
                Seg(root, $"Lintel_{side}", alongX, g0, g1, c0, c1, floorY + DoorH, top);
                cursor = g1;
            }
            if (a1 > cursor) Seg(root, $"Wall_{side}", alongX, cursor, a1, c0, c1, floorY, top);
        }

        private static void Seg(Transform root, string name, bool alongX, float a0, float a1, float c0, float c1, float y0, float y1)
        {
            if (alongX) Box(root, name, a0, a1, y0, y1, c0, c1, mats["RoomWall"]);
            else Box(root, name, c0, c1, y0, y1, a0, a1, mats["RoomWall"]);
        }

        private static void Platform(Transform p, string name, float x0, float x1, float z0, float z1, float h)
        {
            Box(p, name, x0, x1, 0f, h, z0, z1, mats["Stone"]);
        }

        private static void Roof(Transform roofs, string name, float x0, float x1, float z0, float z1, float y)
        {
            Box(roofs, name, x0, x1, y, y + 0.3f, z0, z1, mats["Roof"]);
        }

        private static GameObject Box(Transform p, string name, float x0, float x1, float y0, float y1, float z0, float z1, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(p, false);
            go.transform.position = new Vector3((x0 + x1) / 2f, (y0 + y1) / 2f, (z0 + z1) / 2f);
            go.transform.localScale = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0));
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        private static GameObject Cylinder(Transform p, string name, float x, float z, float y0, float y1, float dx, float dz, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(p, false);
            go.transform.position = new Vector3(x, (y0 + y1) / 2f, z);
            // Unity's cylinder is 2 units tall.
            go.transform.localScale = new Vector3(dx, (y1 - y0) / 2f, dz);
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        private static GameObject Capsule(Transform p, string name, float x, float z, float y, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.SetParent(p, false);
            go.transform.position = new Vector3(x, y, z);
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        private static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            return go.transform;
        }

        // ---------------------------------------------------------------- assets

        private static void CreateMaterials()
        {
            mats = new Dictionary<string, Material>
            {
                ["OuterGround"] = Mat("BO_OuterGround", new Color(0.55f, 0.53f, 0.49f)),
                ["Mud"] = Mat("BO_Mud", new Color(0.56f, 0.47f, 0.36f)),
                ["Water"] = Mat("BO_Water", new Color(0.45f, 0.6f, 0.65f)),
                ["Wall"] = Mat("BO_Wall", new Color(0.78f, 0.74f, 0.66f)),
                ["Stone"] = Mat("BO_Stone", new Color(0.6f, 0.6f, 0.58f)),
                ["Maru"] = Mat("BO_Maru", new Color(0.66f, 0.5f, 0.32f)),
                ["Floor"] = Mat("BO_Floor", new Color(0.82f, 0.74f, 0.58f)),
                ["RoomWall"] = Mat("BO_RoomWall", new Color(0.93f, 0.9f, 0.82f)),
                ["Roof"] = Mat("BO_Roof", new Color(0.3f, 0.32f, 0.35f)),
                ["Wood"] = Mat("BO_Wood", new Color(0.55f, 0.4f, 0.25f)),
                ["WoodDark"] = Mat("BO_WoodDark", new Color(0.35f, 0.24f, 0.15f)),
                ["Metal"] = Mat("BO_Metal", new Color(0.3f, 0.3f, 0.3f)),
                ["Ceramic"] = Mat("BO_Ceramic", new Color(0.42f, 0.33f, 0.27f)),
                ["Cloth"] = Mat("BO_Cloth", new Color(0.35f, 0.45f, 0.6f)),
                ["Leather"] = Mat("BO_Leather", new Color(0.2f, 0.15f, 0.1f)),
                ["Locked"] = Mat("BO_Locked", new Color(0.5f, 0.48f, 0.45f)),
                ["Victim"] = Mat("BO_Victim", new Color(0.9f, 0.9f, 0.88f)),
                ["Npc"] = Mat("BO_Npc", new Color(0.12f, 0.37f, 0.66f)),
            };
        }

        private static Material Mat(string name, Color c)
        {
            var path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void AddSceneToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
