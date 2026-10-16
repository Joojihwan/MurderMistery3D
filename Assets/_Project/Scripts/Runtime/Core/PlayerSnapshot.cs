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
    public class RoomView
    {
        public string id;
        public string name;
        public int capacity;
        public string[] members = Array.Empty<string>();   // public: everyone sees who is talking inside
    }

    [Serializable]
    public class RoomRequestView
    {
        public int id;
        public string roomId;
        public string from;
    }

    [Serializable]
    public class NoticeView
    {
        public int seq;
        public string text;
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
        public bool ready;
        public CardView[] inventory = Array.Empty<CardView>();
        public CardView[] publicBoard = Array.Empty<CardView>();
        public string[] knownClueIds = Array.Empty<string>();
        public string[] notebook = Array.Empty<string>();
        public TopicView[] topics = Array.Empty<TopicView>();
        public string[] retrievableSpots = Array.Empty<string>();
        /// <summary>Locks that are open (shared by everyone).</summary>
        public string[] unlockedLocks = Array.Empty<string>();

        // Private rooms: all rooms with their members (public), this player's room and requests.
        public RoomView[] rooms = Array.Empty<RoomView>();
        public string room;                 // the room this player is in, or empty
        public string pendingRoom;          // the room this player is waiting to enter
        public RoomRequestView[] roomRequests = Array.Empty<RoomRequestView>();   // waiting for this player's answer
        public string[] shutOutOf = Array.Empty<string>();
        // Recent messages for this player only (increasing seq), e.g. "들어가지 못했습니다".
        public NoticeView[] notices = Array.Empty<NoticeView>();

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

        /// <summary>Full view including rooms, vote and ending state held by the gateway.</summary>
        public static PlayerSnapshot Build(LocalSessionGateway g, string playerId, SeatView[] seats = null)
        {
            var snap = Build(g.Session, g.Flow, playerId);
            snap.rooms = g.Rooms.Rooms.Select(r => new RoomView
            {
                id = r.Id, name = r.Name, capacity = r.Capacity, members = r.Members.ToArray()
            }).ToArray();
            snap.room = g.Rooms.RoomOf(playerId)?.Id ?? string.Empty;
            snap.pendingRoom = g.Rooms.PendingOf(playerId)?.RoomId ?? string.Empty;
            snap.roomRequests = g.Rooms.AwaitingAnswerFrom(playerId)
                .Select(q => new RoomRequestView { id = q.Id, roomId = q.RoomId, from = q.Requester }).ToArray();
            snap.shutOutOf = g.Rooms.Rooms.Where(r => r.ShutOut.Contains(playerId)).Select(r => r.Id).ToArray();
            snap.notices = g.NoticesFor(playerId).ToArray();

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
