using System.Collections.Generic;

namespace MurderMystery.Core
{
    public enum GamePhase
    {
        CharacterSelect,
        Reading,
        Introductions,
        AutopsyBriefing,
        Investigation1,
        Discussion1,
        Investigation2,
        Discussion2,
        FinalStatements,
        Vote,
        Ending
    }

    public static class GameRules
    {
        public const int InvestigationPointsPerRound = 4;
        public const int SpotCost = 1;
        public const int RetrieveCost = 2;
        public const int QuestionsPerRound = 2;
        public const int InitiatedSecretTalksPerRound = 2;
    }

    public class NotebookEntry
    {
        public string Source;   // "spot:04", "npc:T_CAUSE", "shown:byeon" ...
        public string Text;
    }

    public class PlayerState
    {
        public string CharacterId;
        public bool CanRead;
        public int InvestigationPoints;
        public int QuestionsLeft;
        public int SecretTalksLeft;
        public readonly HashSet<string> Inventory = new HashSet<string>();
        public readonly HashSet<string> KnownFacts = new HashSet<string>();
        public readonly List<NotebookEntry> Notebook = new List<NotebookEntry>();
        public string VoteTarget;
        public int BonusAnswer = -1;
    }

    public class SpotState
    {
        public string SpotId;
        public bool Deep;
        public readonly List<string> RemainingItems = new List<string>();
        public readonly List<string> Traces = new List<string>();
        // Set once any item has left the spot; later visitors see the retrieved trace.
        public bool Disturbed;
    }
}
