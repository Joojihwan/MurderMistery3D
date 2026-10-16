using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MurderMystery.Core
{
    /// <summary>
    /// Parsed, read-only scenario data with lookups.
    /// </summary>
    public class GameData
    {
        public CluesFile Clues { get; }
        public CharactersFile Characters { get; }
        public FactsFile Facts { get; }
        public DialogueFile Dialogue { get; }
        public FlowFile Flow { get; }
        public EndingsFile Endings { get; }

        public IReadOnlyDictionary<string, ClueData> ClueById { get; }
        public IReadOnlyDictionary<string, CharacterData> CharacterById { get; }
        public IReadOnlyDictionary<string, TopicData> TopicById { get; }
        public IReadOnlyDictionary<string, LockData> LockById { get; }
        public HashSet<string> PublicFacts { get; }
        public HashSet<string> AllFactIds { get; }
        /// <summary>Resources folder the data came from (books live in its "books" subfolder).</summary>
        public string Folder { get; private set; } = "Data";

        public GameData(string cluesJson, string charactersJson, string factsJson, string dialogueJson,
            string flowJson = null, string endingsJson = null)
            : this(JsonUtility.FromJson<CluesFile>(cluesJson),
                JsonUtility.FromJson<CharactersFile>(charactersJson),
                JsonUtility.FromJson<FactsFile>(factsJson),
                JsonUtility.FromJson<DialogueFile>(dialogueJson),
                flowJson == null ? null : JsonUtility.FromJson<FlowFile>(flowJson),
                endingsJson == null ? null : JsonUtility.FromJson<EndingsFile>(endingsJson))
        {
        }

        // Pre-parsed overload so the rules can be exercised outside the Unity runtime.
        public GameData(CluesFile clues, CharactersFile characters, FactsFile facts, DialogueFile dialogue,
            FlowFile flow = null, EndingsFile endings = null)
        {
            Clues = clues;
            Characters = characters;
            Facts = facts;
            Dialogue = dialogue;
            Flow = flow ?? new FlowFile();
            Endings = endings ?? new EndingsFile();

            ClueById = Clues.clues.ToDictionary(c => c.id);
            CharacterById = Characters.characters.ToDictionary(c => c.id);
            TopicById = Dialogue.topics.ToDictionary(t => t.id);
            LockById = (Clues.locks ?? new LockData[0]).ToDictionary(l => l.id);

            PublicFacts = new HashSet<string>(Facts.facts.Where(f => f.scope == "Public").Select(f => f.id));
            PublicFacts.UnionWith(Clues.clues.Where(c => c.Kind == ClueKind.Public).Select(c => c.id));

            AllFactIds = new HashSet<string>(Facts.facts.Select(f => f.id));
            AllFactIds.UnionWith(ClueById.Keys);
        }

        /// <param name="folder">Resources folder of the scenario's rule data (ScenarioManifest.dataPath).</param>
        public static GameData LoadFromResources(string folder = "Data")
        {
            string Load(string name) => Resources.Load<TextAsset>($"{folder}/{name}").text;
            return new GameData(Load("clues"), Load("characters"), Load("facts"), Load("npc_dialogue"),
                Load("flow"), Load("endings")) { Folder = folder };
        }

        /// <summary>
        /// A scenario book as rich text: "common" for the shared introduction, or a character id. Each
        /// client shows only its own seat's book (ScenarioBookExporter copies them from docs/scenario_books).
        /// </summary>
        public string Book(string id) => Resources.Load<TextAsset>($"{Folder}/books/{id}")?.text;

        public IEnumerable<ClueData> CluesAtSpot(string spotId) =>
            Clues.clues.Where(c => c.SpotIds.Contains(spotId));

        /// <summary>The lock in front of a spot, if any.</summary>
        public LockData LockOfSpot(string spotId) =>
            (Clues.locks ?? new LockData[0]).FirstOrDefault(l => l.spotIds.Contains(spotId));

        public string RetrievedTraceText(string spotId) =>
            Clues.retrievedTraces.FirstOrDefault(t => t.spotId == spotId)?.text;
    }
}
