using System.Collections.Generic;
using System.Linq;
using MurderMystery.Core;
using NUnit.Framework;

namespace MurderMystery.Tests
{
    public class GameRulesTests
    {
        private GameData data;

        [SetUp]
        public void SetUp() => data = TestData.Load();

        private GameSession NewSession(GamePhase phase)
        {
            var s = new GameSession(data, seed: 42);
            s.SetPhase(phase);
            return s;
        }

        [Test]
        public void Data_HasNoValidationIssues()
        {
            var issues = DataValidator.Validate(data);
            Assert.IsEmpty(issues, string.Join("\n", issues));
        }

        [Test]
        public void StartState_BuniHoldsLetterAndCannotRead()
        {
            var s = NewSession(GamePhase.AutopsyBriefing);
            var buni = s.Players["buni"];
            Assert.IsTrue(buni.Inventory.Contains("H2"));
            Assert.IsFalse(buni.CanRead);
            foreach (var p in s.Players.Values)
                Assert.IsTrue(data.PublicFacts.All(p.KnownFacts.Contains), p.CharacterId);
        }

        [Test]
        public void Searching_IsFree_AndRepeatable()
        {
            var s = NewSession(GamePhase.Investigation1);
            for (var i = 0; i < 10; i++) Assert.IsTrue(s.Investigate("heo", "01").Success);
            Assert.IsTrue(s.Investigate("heo", "04").Success);
        }

        [Test]
        public void DeepSpot_BlockedInRound1_ButOwnerCanRetrieve()
        {
            var s = NewSession(GamePhase.Investigation1);
            Assert.IsFalse(s.Investigate("jeong", "13").Success, "deep spot must be closed in round 1");

            var r = s.RetrieveHidden("buni", "13");
            Assert.IsTrue(r.Success, r.Error);
            CollectionAssert.Contains(r.GainedItems, "H1");

            s.SetPhase(GamePhase.Investigation2);
            var later = s.Investigate("jeong", "13");
            Assert.IsTrue(later.Success);
            Assert.IsEmpty(later.GainedItems);
            Assert.IsTrue(later.Messages.Any(m => m.Contains("쌀")), "retrieved trace expected");
        }

        [Test]
        public void ItemIsTakenOnce_TracesAreShared()
        {
            var s = NewSession(GamePhase.Investigation1);
            var first = s.Investigate("jeong", "02", "D1");
            CollectionAssert.Contains(first.GainedItems, "D1");
            CollectionAssert.Contains(first.LearnedClues, "C4");

            var second = s.Investigate("heo", "02");
            CollectionAssert.Contains(second.GainedItems, "D2");
            CollectionAssert.Contains(second.LearnedClues, "C4");

            var third = s.Investigate("byeon", "02");
            Assert.IsEmpty(third.GainedItems);
        }

        [Test]
        public void PublicOnlySpot_IsFree_AndTakenEvidenceLeavesATrace()
        {
            var s = NewSession(GamePhase.Investigation1);
            s.Investigate("heo", "05");

            s.Investigate("jaeha", "04");                       // culprit pockets the burnt paper
            var later = s.Investigate("jeong", "04");
            Assert.IsEmpty(later.GainedItems);
            Assert.IsTrue(later.Messages.Any(m => m.Contains("화로")), "disturbed hearth must be visible");
        }

        [Test]
        public void LockedSpot_NeedsTheRightKey_ThenOpensForEveryone()
        {
            var s = NewSession(GamePhase.Investigation1);
            Assert.IsFalse(s.Investigate("jeong", "20").Success, "shrine is locked");
            Assert.IsFalse(s.TryUnlock("jeong", "shrine").Success, "no key");

            s.Investigate("jeong", "27");                                   // the rusty key fits nothing
            CollectionAssert.Contains(s.Players["jeong"].Inventory, "M1");
            var wrong = s.TryUnlock("jeong", "shrine");
            Assert.IsFalse(wrong.Success);
            StringAssert.Contains("맞지 않는다", wrong.Error);

            s.Investigate("heo", "19", "K1");                               // the shrine key, from the master's robe
            Assert.IsTrue(s.TryUnlock("heo", "shrine").Success);
            Assert.IsTrue(s.IsUnlocked("shrine"));
            Assert.IsTrue(s.Investigate("jeong", "20").Success, "open for everyone once unlocked");
        }

        [Test]
        public void SealedQuarters_OpenAtTheSecondSearch()
        {
            var s = NewSession(GamePhase.Investigation1);
            Assert.IsFalse(s.Investigate("jeong", "15").Success, "guests' room sealed in round 1");
            Assert.IsFalse(s.RetrieveHidden("heo", "16").Success, "even the owner waits for the seal to come off");
            Assert.IsFalse(s.TryUnlock("heo", "sealed_guests").Success);
            s.SetPhase(GamePhase.Investigation2);
            Assert.IsTrue(s.IsUnlocked("sealed_guests"));
            Assert.IsTrue(s.Investigate("jeong", "15").Success);
        }

        [Test]
        public void GateWithoutKey_NeverOpens()
        {
            var s = NewSession(GamePhase.Investigation1);
            var r = s.TryUnlock("byeon", "outer");
            Assert.IsFalse(r.Success);
            StringAssert.Contains("열 수 없다", r.Error);
        }

        [Test]
        public void Npc_UsesHighestTierTheAskerQualifiesFor()
        {
            var s = NewSession(GamePhase.Investigation1);
            Assert.AreEqual(1, s.AskNpc("heo", "T_CAUSE").Tier, "heo knows what a faint reaction means");
            Assert.AreEqual(0, s.AskNpc("jeong", "T_CAUSE").Tier);
        }

        [Test]
        public void Npc_RefusesWhenNoEntryIsReachable_AndLimitsQuestions()
        {
            var s = NewSession(GamePhase.Investigation1);
            // Heo sees the med-chest topic but jeong does not know C6 or F_HEO_OWN_ARSENIC.
            Assert.IsFalse(s.VisibleTopics("jeong").Any(t => t.id == "T_MEDCHEST"));
            var refused = s.AskNpc("jeong", "T_MEDCHEST");
            Assert.IsTrue(refused.Refused);
            Assert.IsTrue(data.Dialogue.refusals.Any(r => refused.Text.Contains(r)));

            s.AskNpc("jeong", "T_GATE");
            var third = s.AskNpc("jeong", "T_GATE");
            Assert.IsTrue(third.Refused, "only two questions per round");
        }

        [Test]
        public void Npc_NeverConveysFactsTheAskerDoesNotKnow()
        {
            var s = NewSession(GamePhase.AutopsyBriefing);
            var responder = new NpcResponder(data, new System.Random(1));
            foreach (var p in s.Players.Values)
            foreach (var topic in data.Dialogue.topics)
            {
                var answer = responder.Answer(p.CharacterId, topic.id, p.KnownFacts);
                if (answer.Refused) continue;
                var entry = topic.entries.First(e => e.tier == answer.Tier && e.requiredFacts.All(p.KnownFacts.Contains));
                foreach (var f in entry.conveysFacts)
                    Assert.IsTrue(p.KnownFacts.Contains(f), $"{p.CharacterId}/{topic.id} leaked {f}");
            }
        }

        [Test]
        public void ShuffleBag_NeverRepeatsBackToBack()
        {
            var bag = new ShuffleBag(3, new System.Random(7));
            var last = -1;
            for (var i = 0; i < 300; i++)
            {
                var next = bag.Next();
                Assert.AreNotEqual(last, next);
                last = next;
            }
        }

        [Test]
        public void Buni_SeesOnlyHerNameOnTheEnvelope()
        {
            var s = NewSession(GamePhase.Investigation1);
            s.RetrieveHidden("buni", "13");
            var opened = s.OpenEnvelope("buni", "H1");
            Assert.IsTrue(opened.Success);
            StringAssert.Contains("분이", opened.Messages[0]);
            StringAssert.DoesNotContain("평민", opened.Messages[0]);

            s.Give("buni", "jeong", "H1");
            s.Show("jeong", "buni", "H1");
            Assert.IsTrue(s.Players["jeong"].Inventory.Contains("H1"));
        }

        [Test]
        public void Disclose_MakesClueKnownToEveryone_AndExposesSecrets()
        {
            var s = NewSession(GamePhase.Investigation2);
            s.RetrieveHidden("jaeha", "10");
            Assert.IsTrue(s.Disclose("jaeha", "C7").Success);
            Assert.IsTrue(s.Players.Values.All(p => p.KnownFacts.Contains("C7")));

            var scores = s.Score("jaeha");
            Assert.IsFalse(scores["jaeha"].Goals.First(g => g.GoalId == "jaeha.secret").Achieved);
        }

        [Test]
        public void Scoring_CorrectAccusation_And_Unresolved()
        {
            var s = NewSession(GamePhase.Vote);
            foreach (var id in new[] { "jeong", "buni", "heo", "byeon" }) s.CastVote(id, "jaeha", 1);
            s.CastVote("jaeha", "byeon", -1);
            var accused = s.TallyVotes();
            Assert.AreEqual("jaeha", accused);

            var scores = s.Score(accused);
            Assert.AreEqual(1, scores["jaeha"].Total, "caught; only the kept secret scores");
            Assert.AreEqual(3 + 1 + 1, scores["heo"].Total, "vote + secret kept + bonus (no D2 held)");

            var unresolved = s.Score(null);
            Assert.AreEqual(5 + 1, unresolved["jaeha"].Total, "escape + secret kept");
        }

        [Test]
        public void Vote_TieReturnsNull()
        {
            var s = NewSession(GamePhase.Vote);
            s.CastVote("jeong", "byeon", 0);
            s.CastVote("buni", "byeon", 0);
            s.CastVote("heo", "jaeha", 0);
            s.CastVote("byeon", "jaeha", 0);
            s.CastVote("jaeha", "heo", -1);
            Assert.IsNull(s.TallyVotes());
            CollectionAssert.AreEquivalent(new[] { "byeon", "jaeha" }, s.TiedLeaders());
        }

        [Test]
        public void SolutionChain_IsDiscoverable()
        {
            // Every link of the intended deduction exists either as a placed clue or a character memory.
            var reachable = new HashSet<string>(data.Characters.characters.SelectMany(c => c.startFacts));
            reachable.UnionWith(data.Clues.clues.Select(c => c.id));
            foreach (var f in new[] { "C1", "C7", "C2", "N3", "N2", "E1", "F_BUNI_SAW_JAEHA_OIL", "F_BYEON_SHADOW_2305", "F_HEO_FAINT_MEANS_SMALL", "F_BUNI_ALIVE_0045", "F_BYEON_DEAD_0130" })
                Assert.IsTrue(reachable.Contains(f), f);
        }
    }
}
