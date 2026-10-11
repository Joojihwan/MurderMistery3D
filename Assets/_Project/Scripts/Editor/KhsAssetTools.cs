using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEngine;

namespace MurderMystery.EditorTools
{
    /// <summary>
    /// Helpers for the Korea Heritage Service packages under Assets/ThirdParty/KHS (not in git; see
    /// docs/design/asset_plan.md for how to fetch them again).
    /// </summary>
    public static class KhsAssetTools
    {
        public const string Root = "Assets/ThirdParty/KHS";

        /// <summary>Converts every Built-in "Standard" material under the KHS folder to URP/Lit.</summary>
        [MenuItem("Tools/Murder Mystery/KHS/Convert Built-in Materials to URP")]
        public static void ConvertMaterials() => Debug.Log($"[KHS] converted {ConvertMaterialsCount()} materials to URP/Lit");

        public static int ConvertMaterialsCount()
        {
            if (!AssetDatabase.IsValidFolder(Root)) return 0;
            var upgraders = new MaterialUpgrader[] { new StandardUpgrader("Standard"), new StandardUpgrader("Standard (Specular setup)") };
            var count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Root }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                var upgrader = upgraders.FirstOrDefault(u => mat != null && mat.shader != null && u.OldShaderPath == mat.shader.name);
                if (upgrader == null) continue;
                MaterialUpgrader.Upgrade(mat, upgrader, MaterialUpgrader.UpgradeFlags.None);
                EditorUtility.SetDirty(mat);
                count++;
            }
            AssetDatabase.SaveAssets();
            return count;
        }
    }
}
