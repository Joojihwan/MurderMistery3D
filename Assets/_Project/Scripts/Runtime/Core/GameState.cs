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
        Council1,
        Discussion1,
        Investigation2,
        Council2,
        Discussion2,
        FinalStatements,
        Vote,
        Ending
    }

    public static class GameRules
    {
        public const int QuestionsPerRound = 2;
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
        public int QuestionsLeft;
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
