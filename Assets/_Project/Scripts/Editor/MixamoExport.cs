using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEngine;
using UnityEngine.Rendering;

namespace MurderMystery.EditorTools
{
    /// <summary>
    /// Exports each dressed character (Resources/Characters) as one combined mesh in its A-pose, ready for
    /// Mixamo's auto-rigger. Output: Exports/Mixamo/&lt;id&gt;.fbx (git-ignored). Sub-meshes keep the garment
    /// material names so the rigged FBX coming back from Mixamo can be re-dressed by name. Skirts are left out
    /// (see <see cref="CharacterOutfitBuilder.Skirts"/>)
    /// (see docs/design/asset_plan.md §3).
    /// </summary>
    public static class MixamoExport
    {
        public const string OutDir = "Exports/Mixamo";
        public static readonly string[] Ids = { "jaeha", "heo", "byeon", "jeong", "buni", "dogeomnyul" };

        [MenuItem("Tools/Murder Mystery/Characters/Export Outfits for Mixamo")]
        public static void ExportAll() => Debug.Log("[Mixamo] " + ExportAllReport());

        public static string ExportAllReport()
        {
            Directory.CreateDirectory(OutDir);
            var report = new List<string>();
            foreach (var id in Ids)
            {
                var prefab = Resources.Load<GameObject>($"Characters/{id}");
                if (prefab == null)
                {
                    report.Add($"{id}: no prefab");
                    continue;
                }
                var combined = Combine(prefab, out var tris);
                var path = Path.Combine(OutDir, id + ".fbx");
                ModelExporter.ExportObject(path, combined, new ExportModelOptions { ExportFormat = ExportFormat.Binary });
                Object.DestroyImmediate(combined);
                report.Add($"{id}: {tris} tris -> {path}");
            }
            return string.Join("\n", report);
        }

        /// <summary>One mesh, one sub-mesh per material, baked at the prefab's own scale (metres, feet at y = 0).</summary>
        private static GameObject Combine(GameObject prefab, out int tris)
        {
            var instance = Object.Instantiate(prefab);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var root = instance.transform.worldToLocalMatrix;

            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            var skirtLeftOut = false;
            Material underwear = null;
            foreach (var mf in instance.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.name == "SM_Underpants" && mr != null) underwear = mr.sharedMaterial;
                if (CharacterOutfitBuilder.Skirts.Contains(mf.name)) skirtLeftOut = true;
                if (mf.sharedMesh == null || mr == null || CharacterOutfitBuilder.Skirts.Contains(mf.name) ||
                    mf.name == CharacterOutfitBuilder.HairCap || mf.name.StartsWith(CharacterOutfitBuilder.AttachHead)) continue;
                for (var sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                {
                    var mat = mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)];
                    if (!byMaterial.TryGetValue(mat, out var list)) byMaterial[mat] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = sub, transform = root * mf.transform.localToWorldMatrix });
                }
            }

            // Without the skirt, the short 저고리 and the 속바지 waistband leave the hollow torso showing, which
            // reads as a broken body to the auto-rigger: fill it with a core (hidden under the skirt in game).
            if (skirtLeftOut && underwear != null)
            {
                var model = instance.transform.Find("Model");
                var s = model != null ? model.lossyScale : Vector3.one;
                var core = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                var coreMatrix = Matrix4x4.TRS(new Vector3(0f, 1.15f * s.y, 0f), Quaternion.identity,
                    new Vector3(0.24f * s.x, 0.28f * s.y, 0.18f * s.z));
                if (!byMaterial.TryGetValue(underwear, out var list)) byMaterial[underwear] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = core.GetComponent<MeshFilter>().sharedMesh, transform = coreMatrix });
                Object.DestroyImmediate(core);
            }

            // First merge per material, then join the per-material meshes as sub-meshes.
            var parts = new List<CombineInstance>();
            var materials = new List<Material>();
            foreach (var pair in byMaterial)
            {
                var part = new Mesh { indexFormat = IndexFormat.UInt32 };
                part.CombineMeshes(pair.Value.ToArray(), true, true);
                parts.Add(new CombineInstance { mesh = part, transform = Matrix4x4.identity });
                materials.Add(pair.Key);
            }
            var mesh = new Mesh { name = prefab.name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), false, false);
            mesh.RecalculateBounds();
            Object.DestroyImmediate(instance);

            var go = new GameObject(prefab.name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
            tris = mesh.triangles.Length / 3;
            return go;
        }
    }
}
