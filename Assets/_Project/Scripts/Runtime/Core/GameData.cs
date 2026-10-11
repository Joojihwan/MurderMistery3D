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

        public IReadOnlyDictionary<string, ClueData> ClueById { get; }
        public IReadOnlyDictionary<string, CharacterData> CharacterById { get; }
        public IReadOnlyDictionary<string, TopicData> TopicById { get; }
        public HashSet<string> PublicFacts { get; }
        public HashSet<string> AllFactIds { get; }

        public GameData(string cluesJson, string charactersJson, string factsJson, string dialogueJson)
            : this(JsonUtility.FromJson<CluesFile>(cluesJson),
                JsonUtility.FromJson<CharactersFile>(charactersJson),
                JsonUtility.FromJson<FactsFile>(factsJson),
                JsonUtility.FromJson<DialogueFile>(dialogueJson))
        {
        }

        // Pre-parsed overload so the rules can be exercised outside the Unity runtime.
        public GameData(CluesFile clues, CharactersFile characters, FactsFile facts, DialogueFile dialogue)
        {
            Clues = clues;
            Characters = characters;
            Facts = facts;
            Dialogue = dialogue;

            ClueById = Clues.clues.ToDictionary(c => c.id);
            CharacterById = Characters.characters.ToDictionary(c => c.id);
            TopicById = Dialogue.topics.ToDictionary(t => t.id);

            PublicFacts = new HashSet<string>(Facts.facts.Where(f => f.scope == "Public").Select(f => f.id));
            PublicFacts.UnionWith(Clues.clues.Where(c => c.Kind == ClueKind.Public).Select(c => c.id));

            AllFactIds = new HashSet<string>(Facts.facts.Select(f => f.id));
            AllFactIds.UnionWith(ClueById.Keys);
        }

        public static GameData LoadFromResources()
        {
            string Load(string name) => Resources.Load<TextAsset>($"Data/{name}").text;
            return new GameData(Load("clues"), Load("characters"), Load("facts"), Load("npc_dialogue"));
        }

        public IEnumerable<ClueData> CluesAtSpot(string spotId) =>
            Clues.clues.Where(c => c.SpotIds.Contains(spotId));

        public string RetrievedTraceText(string spotId) =>
            Clues.retrievedTraces.FirstOrDefault(t => t.spotId == spotId)?.text;
    }
}
