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
    /// material names so the rigged FBX coming back from Mixamo can be re-dressed by name
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
            foreach (var mf in instance.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null) continue;
                for (var sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                {
                    var mat = mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)];
                    if (!byMaterial.TryGetValue(mat, out var list)) byMaterial[mat] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = sub, transform = root * mf.transform.localToWorldMatrix });
                }
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
