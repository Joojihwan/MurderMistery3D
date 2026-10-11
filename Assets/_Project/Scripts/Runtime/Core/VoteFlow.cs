using System;
using System.Collections.Generic;
using System.Linq;

namespace MurderMystery.Core
{
    public enum VoteStage { Main, Runoff, Done }

    /// <summary>
    /// game_rules.md §9: everyone votes (not for themselves). A tie triggers one runoff among the tied
    /// leaders; a second tie leaves the report unresolved. The bonus answer is taken from the main vote.
    /// </summary>
    public class VoteFlow
    {
        private readonly GameSession session;
        private readonly Dictionary<string, string> votes = new Dictionary<string, string>();
        private readonly Dictionary<string, int> bonus = new Dictionary<string, int>();

        public VoteStage Stage { get; private set; } = VoteStage.Main;
        public IReadOnlyList<string> Candidates { get; private set; }
        public string Accused { get; private set; }
        public bool Resolved => Stage == VoteStage.Done && Accused != null;

        private readonly Func<IEnumerable<string>> voters;

        public VoteFlow(GameSession session, Func<IEnumerable<string>> voters = null)
        {
            this.session = session;
            this.voters = voters ?? (() => session.Players.Keys);
            Candidates = session.Players.Keys.ToList();
        }

        public ActionResult Cast(string voter, string target, int bonusAnswer = -1)
        {
            if (Stage == VoteStage.Done) return ActionResult.Fail("투표가 끝났습니다.");
            if (!session.Players.ContainsKey(voter)) return ActionResult.Fail("unknown player");
            if (voter == target) return ActionResult.Fail("자기 자신에게는 투표할 수 없습니다.");
            if (!Candidates.Contains(target)) return ActionResult.Fail("후보가 아닌 사람입니다.");
            votes[voter] = target;
            if (Stage == VoteStage.Main) bonus[voter] = bonusAnswer;
            return new ActionResult { Success = true };
        }

        public bool HasVoted(string playerId) => votes.ContainsKey(playerId);

        public bool AllVoted => voters().All(id => votes.ContainsKey(id) || !CanVote(id));

        /// <summary>Time ran out: close what we have; an unfinished runoff ends unresolved.</summary>
        public void ForceFinish()
        {
            if (Stage == VoteStage.Done) return;
            if (!Close()) Close();
        }

        // In a two-way runoff a candidate can only vote for the other one; a candidate in a runoff
        // where every other candidate is themselves cannot vote at all.
        private bool CanVote(string id) => Candidates.Any(c => c != id);

        /// <summary>Closes the current round. Returns true when the vote is finished (resolved or not).</summary>
        public bool Close()
        {
            var counts = votes.Values.GroupBy(v => v).Select(g => (target: g.Key, n: g.Count()))
                .OrderByDescending(x => x.n).ToList();
            var max = counts.Count > 0 ? counts[0].n : 0;
            var leaders = counts.Where(c => c.n == max).Select(c => c.target).ToList();

            if (leaders.Count == 1)
            {
                Finish(leaders[0]);
                return true;
            }
            if (Stage == VoteStage.Main && leaders.Count > 1)
            {
                Stage = VoteStage.Runoff;
                Candidates = leaders;
                votes.Clear();
                return false;
            }
            Finish(null);
            return true;
        }

        private void Finish(string accused)
        {
            Accused = accused;
            Stage = VoteStage.Done;
            foreach (var p in session.Players.Values)
            {
                p.VoteTarget = votes.TryGetValue(p.CharacterId, out var v) ? v : null;
                p.BonusAnswer = bonus.TryGetValue(p.CharacterId, out var b) ? b : -1;
            }
        }

        /// <summary>Ending key for endings.json: "A", "B_&lt;characterId&gt;" or "C".</summary>
        public string EndingKey
        {
            get
            {
                if (Stage != VoteStage.Done) throw new InvalidOperationException("vote not finished");
                if (Accused == null) return "C";
                return Accused == session.Data.Characters.culpritId ? "A" : $"B_{Accused}";
            }
        }
    }
}
