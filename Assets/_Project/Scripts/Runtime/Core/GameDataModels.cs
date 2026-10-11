using System;

// Serializable mirrors of the JSON files in Resources/Data. Field names must match the JSON keys
// because they are parsed with JsonUtility.
namespace MurderMystery.Core
{
    public enum ClueKind { Public, Trace, Item }
    public enum GoalType { VoteCulprit, VoteNotAccused, Possess, Secret, BonusIntent }

    [Serializable]
    public class ClueData
    {
        public string id;
        public string kind;
        public string spotId;
        public string owner;
        public string layer;
        public bool readable;
        public bool @sealed;
        public string title;
        public string text;
        public string sealedText;
        public string[] exposes = Array.Empty<string>();

        public ClueKind Kind => (ClueKind)Enum.Parse(typeof(ClueKind), kind);
        public bool IsDeep => layer == "Deep";

        // A trace clue may sit on several spots ("08a,08b").
        public string[] SpotIds => string.IsNullOrEmpty(spotId)
            ? Array.Empty<string>()
            : spotId.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
    }

    [Serializable]
    public class RetrievedTraceData
    {
        public string spotId;
        public string text;
    }

    [Serializable]
    public class CluesFile
    {
        public ClueData[] clues = Array.Empty<ClueData>();
        public RetrievedTraceData[] retrievedTraces = Array.Empty<RetrievedTraceData>();
    }

    [Serializable]
    public class GoalData
    {
        public string id;
        public string type;
        public int points;
        public string[] itemIds = Array.Empty<string>();

        public GoalType Type => (GoalType)Enum.Parse(typeof(GoalType), type);
    }

    [Serializable]
    public class CharacterData
    {
        public string id;
        public string name;
        public bool isCulprit;
        public bool canRead;
        public string[] startItems = Array.Empty<string>();
        public string[] startFacts = Array.Empty<string>();
        public GoalData[] goals = Array.Empty<GoalData>();
    }

    [Serializable]
    public class BonusQuestionData
    {
        public string text;
        public string[] choices = Array.Empty<string>();
        public int answerIndex;
    }

    [Serializable]
    public class CharactersFile
    {
        public string culpritId;
        public CharacterData[] characters = Array.Empty<CharacterData>();
        public BonusQuestionData bonusQuestion;
    }

    [Serializable]
    public class FactData
    {
        public string id;
        public string scope;
        public string[] holders = Array.Empty<string>();
        public string description;
    }

    [Serializable]
    public class FactsFile
    {
        public FactData[] facts = Array.Empty<FactData>();
    }

    [Serializable]
    public class AnswerEntry
    {
        public int tier;
        public string[] requiredFacts = Array.Empty<string>();
        public string[] conveysFacts = Array.Empty<string>();
        public string[] variants = Array.Empty<string>();
    }

    [Serializable]
    public class TopicData
    {
        public string id;
        public string category;
        public string label;
        public string[] visibleIfAny = Array.Empty<string>();
        public AnswerEntry[] entries = Array.Empty<AnswerEntry>();
    }

    [Serializable]
    public class DialogueFile
    {
        public string[] refusals = Array.Empty<string>();
        public string[] openers = Array.Empty<string>();
        public string[] closers = Array.Empty<string>();
        public TopicData[] topics = Array.Empty<TopicData>();
    }

    [Serializable]
    public class EndingData
    {
        public string key;
        public string title;
        public string[] lines = Array.Empty<string>();
    }

    [Serializable]
    public class ReplayStopData
    {
        public string time;
        public string text;
    }

    [Serializable]
    public class EpilogueData
    {
        public ReplayStopData[] replayStops = Array.Empty<ReplayStopData>();
        public string[] documentOrder = Array.Empty<string>();
        public string finalLine;
    }

    [Serializable]
    public class EndingsFile
    {
        public EndingData[] endings = Array.Empty<EndingData>();
        public EpilogueData epilogue = new EpilogueData();
    }
}
