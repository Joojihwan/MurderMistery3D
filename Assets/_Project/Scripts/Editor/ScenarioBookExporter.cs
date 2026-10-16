using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MurderMystery.EditorTools
{
    /// <summary>
    /// Copies the scenario books (docs/scenario_books/*.md) into Resources so the game can show each player
    /// their own book in the notebook. Markdown is turned into Unity rich text (bold, headings); tables and
    /// quotes are kept as plain lines. Run after editing a book: Tools > Murder Mystery > Export Scenario Books.
    /// </summary>
    public static class ScenarioBookExporter
    {
        private const string Source = "docs/scenario_books";
        private const string Target = "Assets/_Project/Resources/Data/books";

        private static readonly (string file, string id)[] Books =
        {
            ("00_common.md", "common"),
            ("01_jeong.md", "jeong"),
            ("02_jaeha.md", "jaeha"),
            ("03_buni.md", "buni"),
            ("04_heo.md", "heo"),
            ("05_byeon.md", "byeon"),
        };

        [MenuItem("Tools/Murder Mystery/Export Scenario Books")]
        public static void Export()
        {
            Directory.CreateDirectory(Target);
            foreach (var (file, id) in Books)
            {
                var path = Path.Combine(Source, file);
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[ScenarioBookExporter] {path} not found");
                    continue;
                }
                File.WriteAllText(Path.Combine(Target, id + ".txt"), ToRichText(File.ReadAllText(path)), new UTF8Encoding(false));
            }
            AssetDatabase.Refresh();
            Debug.Log($"[ScenarioBookExporter] exported {Books.Length} books to {Target}");
        }

        public static string ToRichText(string md)
        {
            var sb = new StringBuilder();
            foreach (var raw in md.Replace("\r", "").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (Regex.IsMatch(line, @"^\|[-\s|:]+\|$")) continue;            // table separator row
                if (line.StartsWith("|")) line = string.Join("  ·  ", line.Trim('|').Split('|')).Trim();
                if (line == "---") line = "";
                line = Regex.Replace(line, @"\*\*(.+?)\*\*", "<b>$1</b>");
                if (line.StartsWith("# ")) line = $"<size=20><b>{line.Substring(2)}</b></size>";
                else if (line.StartsWith("## ")) line = $"\n<b>{line.Substring(3)}</b>";
                else if (line.StartsWith("> ")) line = $"<i>{line.Substring(2)}</i>";
                sb.AppendLine(line);
            }
            return Regex.Replace(sb.ToString(), @"\n{3,}", "\n\n").Trim() + "\n";
        }
    }
}
