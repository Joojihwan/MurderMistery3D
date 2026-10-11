using System;
using System.Collections.Generic;
using System.Linq;

namespace MurderMystery.Core
{
    [Serializable]
    public class CardView
    {
        public string id;
        public string title;
        public string text;       // already filtered for literacy; sealed text only once opened
        public bool sealedClosed;
    }

    [Serializable]
    public class TopicView
    {
        public string id;
        public string category;
        public string label;
    }

    /// <summary>
    /// Everything one client may see (docs/design/tech_decisions.md §3). Built on the host and sent only
    /// to its owner; it must never contain another seat's private facts, items or notebook.
    /// </summary>
    [Serializable]
    public class PlayerSnapshot
    {
        public string characterId;
        public string phase;
        public string clock;
        public float remainingSec;
        public int investigationPoints;
        public int questionsLeft;
        public int secretTalksLeft;
        public CardView[] inventory = Array.Empty<CardView>();
        public CardView[] publicBoard = Array.Empty<CardView>();
        public string[] knownClueIds = Array.Empty<string>();
        public string[] notebook = Array.Empty<string>();
        public TopicView[] topics = Array.Empty<TopicView>();

        public static PlayerSnapshot Build(GameSession s, GameFlow flow, string playerId)
        {
            var p = s.Players[playerId];
            CardView Card(string id)
            {
                var clue = s.Data.ClueById[id];
                var opened = clue.@sealed && s.IsOpened(id);
                return new CardView
                {
                    id = id,
                    title = clue.title,
                    text = s.ReadableText(p, clue, opened ? clue.sealedText : clue.text),
                    sealedClosed = clue.@sealed && !opened
                };
            }

            return new PlayerSnapshot
            {
                characterId = playerId,
                phase = s.Phase.ToString(),
                clock = flow?.Current?.clock ?? string.Empty,
                remainingSec = flow?.Remaining ?? 0f,
                investigationPoints = p.InvestigationPoints,
                questionsLeft = p.QuestionsLeft,
                secretTalksLeft = p.SecretTalksLeft,
                inventory = p.Inventory.OrderBy(i => i).Select(Card).ToArray(),
                publicBoard = s.PublicBoard.OrderBy(i => i).Select(Card).ToArray(),
                knownClueIds = p.KnownFacts.Where(s.Data.ClueById.ContainsKey).OrderBy(i => i).ToArray(),
                notebook = p.Notebook.Select(e => e.Text).ToArray(),
                topics = s.VisibleTopics(playerId)
                    .Select(t => new TopicView { id = t.id, category = t.category, label = t.label }).ToArray()
            };
        }
    }
}
