using System;
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

    [Serializable]
    public class TalkRequestView
    {
        public int id;
        public string from;
    }

    [Serializable]
    public class ScoreView
    {
        public string characterId;
        public int total;
        public string goals;      // "○VoteCulprit, ×Secret, ..."
    }

    [Serializable]
    public class SeatView
    {
        public string characterId;
        public string playerName;  // empty = unclaimed
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
        public bool timed;
        public int investigationPoints;
        public int questionsLeft;
        public int secretTalksLeft;
        public bool ready;
        public CardView[] inventory = Array.Empty<CardView>();
        public CardView[] publicBoard = Array.Empty<CardView>();
        public string[] knownClueIds = Array.Empty<string>();
        public string[] notebook = Array.Empty<string>();
        public TopicView[] topics = Array.Empty<TopicView>();
        public string[] retrievableSpots = Array.Empty<string>();
        /// <summary>Locks that are open (shared by everyone).</summary>
        public string[] unlockedLocks = Array.Empty<string>();

        // Secret talks (only the ones involving this player).
        public TalkRequestView[] incomingTalks = Array.Empty<TalkRequestView>();
        public string talkPartner;
        public int talkId;
        public float talkRemainingSec;

        // Vote and ending (public once reached).
        public string voteStage;
        public string[] voteCandidates = Array.Empty<string>();
        public bool hasVoted;
        public string endingKey;
        public ScoreView[] scores = Array.Empty<ScoreView>();

        public SeatView[] seats = Array.Empty<SeatView>();

        public GamePhase Phase => (GamePhase)Enum.Parse(typeof(GamePhase), phase);

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
                timed = flow?.Current != null && flow.Current.durationSec > 0f,
                ready = flow != null && flow.Ready.Contains(playerId),
                investigationPoints = p.InvestigationPoints,
                questionsLeft = p.QuestionsLeft,
                secretTalksLeft = p.SecretTalksLeft,
                inventory = p.Inventory.OrderBy(i => i).Select(Card).ToArray(),
                publicBoard = s.PublicBoard.OrderBy(i => i).Select(Card).ToArray(),
                knownClueIds = p.KnownFacts.Where(s.Data.ClueById.ContainsKey).OrderBy(i => i).ToArray(),
                notebook = p.Notebook.Select(e => e.Text).ToArray(),
                topics = s.VisibleTopics(playerId)
                    .Select(t => new TopicView { id = t.id, category = t.category, label = t.label }).ToArray(),
                retrievableSpots = s.Spots.Values
                    .Where(sp => sp.RemainingItems.Any(i => s.Data.ClueById[i].owner == playerId))
                    .Select(sp => sp.SpotId).ToArray(),
                unlockedLocks = s.UnlockedLocks.OrderBy(i => i).ToArray()
            };
        }

        /// <summary>Full view including talks, vote and ending state held by the gateway.</summary>
        public static PlayerSnapshot Build(LocalSessionGateway g, string playerId, SeatView[] seats = null)
        {
            var snap = Build(g.Session, g.Flow, playerId);
            snap.incomingTalks = g.Talks.Pending.Where(t => t.Partner == playerId)
                .Select(t => new TalkRequestView { id = t.Id, from = t.Initiator }).ToArray();
            var active = g.Talks.ActiveFor(playerId);
            if (active != null)
            {
                snap.talkId = active.Id;
                snap.talkPartner = active.Initiator == playerId ? active.Partner : active.Initiator;
                snap.talkRemainingSec = active.Remaining;
            }

            if (g.Vote != null)
            {
                snap.voteStage = g.Vote.Stage.ToString();
                snap.voteCandidates = g.Vote.Candidates.ToArray();
                snap.hasVoted = g.Vote.HasVoted(playerId);
                if (g.Vote.Stage == VoteStage.Done)
                {
                    snap.endingKey = g.Vote.EndingKey;
                    snap.scores = g.Session.Score(g.Vote.Accused).Values
                        .OrderByDescending(x => x.Total)
                        .Select(x => new ScoreView
                        {
                            characterId = x.CharacterId,
                            total = x.Total,
                            goals = string.Join(", ", x.Goals.Select(goal => $"{(goal.Achieved ? "○" : "×")}{goal.Type}"))
                        }).ToArray();
                }
            }
            else if (g.Session.Phase == GamePhase.Ending)
            {
                snap.endingKey = "C";
            }

            snap.seats = seats ?? g.Session.Data.Characters.characters
                .Select(c => new SeatView { characterId = c.id, playerName = string.Empty }).ToArray();
            return snap;
        }
    }
}
