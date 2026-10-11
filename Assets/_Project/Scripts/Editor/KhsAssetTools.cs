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

        /// <summary>
        /// Caps texture import sizes to keep the player build small: garments are seen at a few metres
        /// (1024), the house up close (2048). The KHS sources are up to 4096.
        /// </summary>
        [MenuItem("Tools/Murder Mystery/KHS/Cap Texture Sizes")]
        public static void CapTextureSizes()
        {
            var changed = CapTextureSizes(Root + "/Joseon_Royal_Wear", 1024) + CapTextureSizes(Root + "/KimMyeonggwanHouse", 2048);
            Debug.Log($"[KHS] capped {changed} textures");
        }

        public static int CapTextureSizes(string folder, int maxSize)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return 0;
            var changed = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
                {
                    var importer = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter;
                    if (importer == null || importer.maxTextureSize <= maxSize) continue;
                    importer.maxTextureSize = maxSize;
                    importer.SaveAndReimport();
                    changed++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            return changed;
        }

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
