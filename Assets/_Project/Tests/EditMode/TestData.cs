using MurderMystery.Core;

namespace MurderMystery.Tests
{
    /// <summary>
    /// Loads the real scenario data. Inside Unity it uses Resources; the offline harness
    /// (dotnet test, no Unity runtime) parses the same JSON files with Newtonsoft.
    /// </summary>
    public static class TestData
    {
        public static GameData Load()
        {
#if UNITY_5_3_OR_NEWER
            return GameData.LoadFromResources();
#else
            T Read<T>(string name) => Newtonsoft.Json.JsonConvert.DeserializeObject<T>(
                System.IO.File.ReadAllText(System.IO.Path.Combine(DataDir, name + ".json")));
            return new GameData(Read<CluesFile>("clues"), Read<CharactersFile>("characters"),
                Read<FactsFile>("facts"), Read<DialogueFile>("npc_dialogue"),
                Read<FlowFile>("flow"), Read<EndingsFile>("endings"));
#endif
        }

#if !UNITY_5_3_OR_NEWER
        public static string DataDir =
            System.Environment.GetEnvironmentVariable("MM_DATA_DIR") ?? "Assets/_Project/Resources/Data";
#endif
    }
}
