using System;
using System.Collections.Generic;
using System.Linq;

namespace MurderMystery.Core
{
    public class ActionResult
    {
        public bool Success;
        public string Error;
        public readonly List<string> GainedItems = new List<string>();
        public readonly List<string> LearnedClues = new List<string>();
        public readonly List<string> Messages = new List<string>();

        public static ActionResult Fail(string error) => new ActionResult { Success = false, Error = error };
    }

    /// <summary>
    /// Authoritative game state and rules (docs/design/game_rules.md). Engine- and network-agnostic
    /// so it can run on a host and be unit tested.
    /// </summary>
    public class GameSession
    {
        public GameData Data { get; }
        public GamePhase Phase { get; private set; } = GamePhase.CharacterSelect;
        public IReadOnlyDictionary<string, PlayerState> Players => players;
        public IReadOnlyCollection<string> PublicBoard => publicBoard;
        public IReadOnlyDictionary<string, SpotState> Spots => spots;
        /// <summary>Locks someone has opened (open for everyone from then on).</summary>
        public IReadOnlyCollection<string> UnlockedLocks => unlocked;

        private readonly Dictionary<string, PlayerState> players = new Dictionary<string, PlayerState>();
        private readonly Dictionary<string, SpotState> spots = new Dictionary<string, SpotState>();
        private readonly HashSet<string> publicBoard = new HashSet<string>();   // clue IDs disclosed to all
        private readonly HashSet<string> openedEnvelopes = new HashSet<string>();
        private readonly HashSet<string> unlocked = new HashSet<string>();
        private readonly NpcResponder npc;

        public GameSession(GameData data, int seed = 0)
        {
            Data = data;
            npc = new NpcResponder(data, new Random(seed));

            foreach (var c in data.Characters.characters)
            {
                var p = new PlayerState { CharacterId = c.id, CanRead = c.canRead };
                p.KnownFacts.UnionWith(c.startFacts);
                foreach (var item in c.startItems)
                {
                    p.Inventory.Add(item);
                    p.KnownFacts.Add(item);
                }
                players[c.id] = p;
            }

            // Public clues also sit on spots so visiting them shows the (already known) card.
            foreach (var clue in data.Clues.clues)
            {
                foreach (var spotId in clue.SpotIds)
                {
                    if (!spots.TryGetValue(spotId, out var spot))
                    {
                        spot = new SpotState { SpotId = spotId };
                        spots[spotId] = spot;
                    }
                    spot.Deep |= clue.IsDeep;
                    if (clue.Kind == ClueKind.Item) spot.RemainingItems.Add(clue.id);
                    else spot.Traces.Add(clue.id);
                }
            }
        }

        public const string DefaultDisturbedText = "누군가 이미 무언가를 가져간 흔적이 있다.";

        public int InvestigationRound => Phase == GamePhase.Investigation1 ? 1 : Phase == GamePhase.Investigation2 ? 2 : 0;

        // ------------------------------------------------------------------ phases

        public void AdvancePhase()
        {
            if (Phase == GamePhase.Ending) return;
            Phase += 1;
            // Rooms sealed until a later phase open for everyone when it begins.
            foreach (var l in Data.Clues.locks ?? new LockData[0])
                if (l.OpensLater && l.opensAtPhase == Phase.ToString()) unlocked.Add(l.id);
            if (Phase == GamePhase.AutopsyBriefing)
            {
                foreach (var p in players.Values) p.KnownFacts.UnionWith(Data.PublicFacts);
            }
            if (InvestigationRound > 0)
            {
                foreach (var p in players.Values) p.QuestionsLeft = GameRules.QuestionsPerRound;
            }
            else
            {
                // Unused questions do not carry over.
                foreach (var p in players.Values) p.QuestionsLeft = 0;
            }
        }

        public void SetPhase(GamePhase phase)
        {
            while (Phase != phase && Phase != GamePhase.Ending) AdvancePhase();
        }

        // ------------------------------------------------------------------ investigation

        public ActionResult Investigate(string playerId, string spotId, string preferredItem = null)
        {
            if (!players.TryGetValue(playerId, out var p)) return ActionResult.Fail("unknown player");
            if (InvestigationRound == 0) return ActionResult.Fail("조사 단계가 아닙니다.");
            if (!spots.TryGetValue(spotId, out var spot)) return ActionResult.Fail("조사할 것이 없는 곳입니다.");
            if (spot.Deep && InvestigationRound < 2) return ActionResult.Fail("이곳은 2차 조사부터 살필 수 있습니다.");
            var lockData = Data.LockOfSpot(spotId);
            if (lockData != null && !unlocked.Contains(lockData.id)) return ActionResult.Fail(lockData.lockedText);
            // Searching is free; only the clock limits it.
            var result = new ActionResult { Success = true };

            foreach (var traceId in spot.Traces) Learn(p, traceId, $"spot:{spotId}", result);

            if (spot.RemainingItems.Count > 0)
            {
                var item = preferredItem != null && spot.RemainingItems.Contains(preferredItem)
                    ? preferredItem
                    : spot.RemainingItems[0];
                TakeItem(p, spot, item, result);
            }
            else if (spot.Disturbed)
            {
                // Taking evidence always leaves a sign, so hiding it is never invisible.
                var trace = Data.RetrievedTraceText(spotId) ?? DefaultDisturbedText;
                result.Messages.Add(trace);
                p.Notebook.Add(new NotebookEntry { Source = $"spot:{spotId}", Text = trace });
            }

            if (result.GainedItems.Count == 0 && result.LearnedClues.Count == 0 && result.Messages.Count == 0)
                result.Messages.Add("특별한 것은 보이지 않는다.");
            return result;
        }

        /// <summary>The hider takes back their own hidden items (allowed from round 1, even behind a sealed door).</summary>
        public ActionResult RetrieveHidden(string playerId, string spotId)
        {
            if (!players.TryGetValue(playerId, out var p)) return ActionResult.Fail("unknown player");
            if (InvestigationRound == 0) return ActionResult.Fail("조사 단계가 아닙니다.");
            if (!spots.TryGetValue(spotId, out var spot)) return ActionResult.Fail("숨긴 물건이 없습니다.");
            var own = spot.RemainingItems.Where(id => Data.ClueById[id].owner == playerId).ToList();
            if (own.Count == 0) return ActionResult.Fail("이곳에 당신이 숨긴 물건은 남아 있지 않습니다.");
            var sealedBy = Data.LockOfSpot(spotId);
            if (sealedBy != null && !unlocked.Contains(sealedBy.id)) return ActionResult.Fail(sealedBy.lockedText);
            var result = new ActionResult { Success = true };
            foreach (var item in own) TakeItem(p, spot, item, result);
            return result;
        }

        private void TakeItem(PlayerState p, SpotState spot, string itemId, ActionResult result)
        {
            spot.RemainingItems.Remove(itemId);
            spot.Disturbed = true;
            p.Inventory.Add(itemId);
            result.GainedItems.Add(itemId);
            Learn(p, itemId, $"spot:{spot.SpotId}", result);
        }

        private void Learn(PlayerState p, string clueId, string source, ActionResult result)
        {
            var isNew = p.KnownFacts.Add(clueId);
            if (isNew) result.LearnedClues.Add(clueId);
            var clue = Data.ClueById[clueId];
            p.Notebook.Add(new NotebookEntry { Source = source, Text = $"{clue.title}: {ReadableText(p, clue, clue.text)}" });
        }

        // ------------------------------------------------------------------ locks

        /// <summary>Tries a lock with whatever key the player holds. Allowed in any phase (it is not a search).</summary>
        public ActionResult TryUnlock(string playerId, string lockId)
        {
            if (!players.TryGetValue(playerId, out var p)) return ActionResult.Fail("unknown player");
            if (!Data.LockById.TryGetValue(lockId, out var lockData)) return ActionResult.Fail("잠긴 곳이 아닙니다.");
            if (unlocked.Contains(lockId)) return new ActionResult { Success = true };
            if (!lockData.Openable) return ActionResult.Fail(lockData.lockedText);
            if (!p.Inventory.Contains(lockData.keyId))
            {
                var keys = p.Inventory.Where(id => !string.IsNullOrEmpty(Data.ClueById[id].unlocks)).ToList();
                return ActionResult.Fail(keys.Count == 0
                    ? lockData.lockedText
                    : $"가진 열쇠({string.Join(", ", keys.Select(k => Data.ClueById[k].title))})가 맞지 않는다.");
            }

            unlocked.Add(lockId);
            var result = new ActionResult { Success = true };
            result.Messages.Add(lockData.openedText);
            p.Notebook.Add(new NotebookEntry { Source = $"lock:{lockId}", Text = lockData.openedText });
            return result;
        }

        public bool IsUnlocked(string lockId) => unlocked.Contains(lockId);

        // ------------------------------------------------------------------ documents & sharing

        public ActionResult OpenEnvelope(string playerId, string clueId)
        {
            if (!players.TryGetValue(playerId, out var p)) return ActionResult.Fail("unknown player");
            if (!p.Inventory.Contains(clueId)) return ActionResult.Fail("가지고 있지 않은 물건입니다.");
            var clue = Data.ClueById[clueId];
            if (!clue.@sealed) return ActionResult.Fail("봉해진 문서가 아닙니다.");

            openedEnvelopes.Add(clueId);
            var result = new ActionResult { Success = true };
            var text = ReadableText(p, clue, clue.sealedText);
            result.Messages.Add(text);
            p.Notebook.Add(new NotebookEntry { Source = $"open:{clueId}", Text = $"{clue.title} (뜯음): {text}" });
            return result;
        }

        public bool IsOpened(string clueId) => openedEnvelopes.Contains(clueId);

        public string ReadableText(PlayerState p, ClueData clue, string text)
        {
            if (p.CanRead || !clue.readable) return text;
            // Buni recognises only her own name written in Hangul.
            return clue.id == "H1" ? "글을 읽을 수 없다. 다만 겉봉 한쪽에 '분이'라는 두 글자가 보인다." : "글을 읽을 수 없다.";
        }

        public ActionResult Disclose(string playerId, string clueId)
        {
            if (!players.TryGetValue(playerId, out var p)) return ActionResult.Fail("unknown player");
            if (!p.KnownFacts.Contains(clueId) || !Data.ClueById.ContainsKey(clueId))
                return ActionResult.Fail("공개할 수 있는 단서가 아닙니다.");

            publicBoard.Add(clueId);
            foreach (var other in players.Values) other.KnownFacts.Add(clueId);
            return new ActionResult { Success = true };
        }

        public ActionResult Give(string fromId, string toId, string itemId)
        {
            if (!players.TryGetValue(fromId, out var from) || !players.TryGetValue(toId, out var to))
                return ActionResult.Fail("unknown player");
            if (!from.Inventory.Remove(itemId)) return ActionResult.Fail("가지고 있지 않은 물건입니다.");
            to.Inventory.Add(itemId);
            to.KnownFacts.Add(itemId);
            to.Notebook.Add(new NotebookEntry { Source = $"given:{fromId}", Text = Data.ClueById[itemId].title });
            return new ActionResult { Success = true };
        }

        public ActionResult Show(string fromId, string toId, string clueId)
        {
            if (!players.TryGetValue(fromId, out var from) || !players.TryGetValue(toId, out var to))
                return ActionResult.Fail("unknown player");
            if (!from.KnownFacts.Contains(clueId) || !Data.ClueById.TryGetValue(clueId, out var clue))
                return ActionResult.Fail("보여 줄 수 있는 단서가 아닙니다.");
            to.KnownFacts.Add(clueId);
            var body = IsOpened(clueId) ? clue.sealedText : clue.text;
            to.Notebook.Add(new NotebookEntry { Source = $"shown:{fromId}", Text = $"{clue.title}: {ReadableText(to, clue, body)}" });
            return new ActionResult { Success = true };
        }

        // ------------------------------------------------------------------ NPC

        public IEnumerable<TopicData> VisibleTopics(string playerId) => npc.VisibleTopics(players[playerId].KnownFacts);

        public NpcAnswer AskNpc(string playerId, string topicId)
        {
            if (!players.TryGetValue(playerId, out var p)) return new NpcAnswer { Refused = true, Text = string.Empty };
            if (InvestigationRound == 0) return new NpcAnswer { Refused = true, Text = "지금은 개별 질문을 받지 않소." };
            if (p.QuestionsLeft <= 0) return new NpcAnswer { Refused = true, Text = "이번에는 더 물을 수 없소." };

            p.QuestionsLeft--;
            var answer = npc.Answer(playerId, topicId, p.KnownFacts);
            p.Notebook.Add(new NotebookEntry { Source = $"npc:{topicId}", Text = answer.Text });
            return answer;
        }

        // ------------------------------------------------------------------ vote & scoring

        public void CastVote(string playerId, string targetId, int bonusAnswer)
        {
            if (playerId == targetId) throw new ArgumentException("자기 자신에게는 투표할 수 없습니다.");
            players[playerId].VoteTarget = targetId;
            players[playerId].BonusAnswer = bonusAnswer;
        }

        /// <summary>Returns the accused character, or null when the vote is tied (caller runs the re-vote).</summary>
        public string TallyVotes(IEnumerable<string> restrictTo = null)
        {
            var allowed = restrictTo?.ToHashSet();
            var counts = players.Values
                .Where(p => p.VoteTarget != null && (allowed == null || allowed.Contains(p.VoteTarget)))
                .GroupBy(p => p.VoteTarget)
                .Select(g => (target: g.Key, n: g.Count()))
                .OrderByDescending(x => x.n)
                .ToList();
            if (counts.Count == 0) return null;
            if (counts.Count > 1 && counts[0].n == counts[1].n) return null;
            return counts[0].target;
        }

        public IReadOnlyList<string> TiedLeaders()
        {
            var counts = players.Values.Where(p => p.VoteTarget != null)
                .GroupBy(p => p.VoteTarget).Select(g => (g.Key, n: g.Count())).ToList();
            if (counts.Count == 0) return Array.Empty<string>();
            var max = counts.Max(c => c.n);
            return counts.Where(c => c.n == max).Select(c => c.Key).ToList();
        }

        public Dictionary<string, ScoreSheet> Score(string accusedId) => Scoring.Compute(this, accusedId);

        internal bool IsExposed(string goalId) =>
            publicBoard.Any(clueId => Data.ClueById[clueId].exposes.Contains(goalId));
    }
}
