using System.Collections.Generic;
using System.Linq;
using MurderMystery.Core;
using NUnit.Framework;

namespace MurderMystery.Tests
{
    public class FlowAndVoteTests
    {
        private GameData data;

        [SetUp]
        public void SetUp() => data = TestData.Load();

        [Test]
        public void Flow_AdvancesOnTimeout_AndWarnsOnce()
        {
            var g = new LocalSessionGateway(data);
            var warnings = new List<string>();
            g.Flow.Warning += warnings.Add;
            Assert.AreEqual(GamePhase.CharacterSelect, g.Session.Phase);

            g.Tick(160f);
            g.Tick(5f);
            Assert.AreEqual(1, warnings.Count, "warning at 30 s left, once");
            g.Tick(20f);
            Assert.AreEqual(GamePhase.Reading, g.Session.Phase);
            Assert.AreEqual(900f, g.Flow.Remaining, 0.01f);
        }

        [Test]
        public void Flow_AllReadySkipsAhead_EndingNeverAdvances()
        {
            var g = new LocalSessionGateway(data);
            foreach (var id in g.Session.Players.Keys) g.SetReady(id, true);
            Assert.AreEqual(GamePhase.Reading, g.Session.Phase);

            g.JumpTo(GamePhase.Ending);
            g.Tick(100000f);
            Assert.AreEqual(GamePhase.Ending, g.Session.Phase);
        }

        [Test]
        public void SecretTalk_RequestAcceptExpire_AndLimit()
        {
            var g = new LocalSessionGateway(data);
            g.JumpTo(GamePhase.Investigation1);

            var req = g.RequestTalk("jeong", "buni");
            Assert.IsTrue(req.Success, req.Error);
            var id = int.Parse(req.Messages[0]);
            Assert.IsTrue(g.RespondTalk("buni", id, true).Success);
            Assert.IsTrue(g.Talks.InTalkTogether("jeong", "buni"));
            Assert.IsFalse(g.RequestTalk("jeong", "heo").Success, "already in a talk");

            g.Tick(181f);
            Assert.IsFalse(g.Talks.InTalkTogether("jeong", "buni"), "3 minute limit");

            var second = int.Parse(g.RequestTalk("jeong", "heo").Messages[0]);
            g.RespondTalk("heo", second, true);
            g.Talks.End(second);
            Assert.IsFalse(g.RequestTalk("jeong", "byeon").Success, "two initiated talks per round");
            Assert.IsTrue(g.RequestTalk("byeon", "jeong").Success, "receiving is unlimited");
        }

        [Test]
        public void ShowAndGive_RequireSecretTalkOrProximity()
        {
            var g = new LocalSessionGateway(data);
            g.JumpTo(GamePhase.Investigation1);
            Assert.IsFalse(g.Show("buni", "jeong", "H2").Success);
            Assert.IsFalse(g.Give("buni", "jeong", "H2").Success);

            g.AreNearby = (a, b) => true;
            Assert.IsTrue(g.Give("buni", "jeong", "H2").Success);
        }

        [Test]
        public void Vote_RunoffThenUnresolved_PicksEndingC()
        {
            var g = new LocalSessionGateway(data);
            g.JumpTo(GamePhase.FinalStatements);
            g.Flow.Advance(); // vote → VoteFlow created
            Assert.AreEqual(GamePhase.Vote, g.Session.Phase);

            g.CastVote("jeong", "byeon", 1);
            g.CastVote("buni", "byeon", 1);
            g.CastVote("heo", "jaeha", 1);
            g.CastVote("byeon", "jaeha", 1);
            g.CastVote("jaeha", "heo", -1);
            Assert.AreEqual(VoteStage.Runoff, g.Vote.Stage);
            CollectionAssert.AreEquivalent(new[] { "byeon", "jaeha" }, g.Vote.Candidates);
            Assert.IsFalse(g.CastVote("jeong", "heo", 0).Success, "heo is not a runoff candidate");

            g.CastVote("jeong", "byeon", 0);
            g.CastVote("buni", "jaeha", 0);
            g.CastVote("heo", "jaeha", 0);
            g.CastVote("byeon", "jaeha", 0);
            g.CastVote("jaeha", "byeon", 0);
            Assert.AreEqual(VoteStage.Done, g.Vote.Stage);
            Assert.AreEqual("A", g.Vote.EndingKey);
            Assert.AreEqual(GamePhase.Ending, g.Session.Phase);
            Assert.AreEqual(1, g.Session.Players["heo"].BonusAnswer, "bonus comes from the main vote");
        }

        [Test]
        public void Vote_WrongAccusation_PicksPersonalEndingB()
        {
            var s = new GameSession(data);
            s.SetPhase(GamePhase.Vote);
            var v = new VoteFlow(s);
            foreach (var id in new[] { "jeong", "heo", "byeon", "jaeha" }) v.Cast(id, "buni");
            v.Cast("buni", "byeon");
            Assert.IsTrue(v.Close());
            Assert.AreEqual("B_buni", v.EndingKey);
            Assert.IsTrue(data.Endings.endings.Any(e => e.key == v.EndingKey));
        }

        [Test]
        public void Snapshot_ContainsOnlyOwnPrivateData()
        {
            var g = new LocalSessionGateway(data);
            g.JumpTo(GamePhase.Investigation2);
            g.RetrieveHidden("byeon", "17");
            g.RetrieveHidden("jaeha", "10");

            var jeong = g.GetSnapshot("jeong");
            Assert.IsFalse(jeong.inventory.Any(c => c.id == "H5a" || c.id == "H4"));
            Assert.IsFalse(jeong.knownClueIds.Contains("H4"));
            Assert.IsFalse(jeong.notebook.Any(n => n.Contains("장부")));
            Assert.IsFalse(jeong.topics.Any(t => t.id == "T_MEDCHEST"), "topic list must not leak heo's knowledge");

            var jaeha = g.GetSnapshot("jaeha");
            var yejo = jaeha.inventory.First(c => c.id == "H4");
            Assert.IsTrue(yejo.sealedClosed);
            StringAssert.DoesNotContain("정식 아들", yejo.text, "sealed text only after opening");

            var buni = g.GetSnapshot("buni");
            Assert.IsTrue(buni.inventory.Any(c => c.id == "H2"));
            StringAssert.Contains("읽을 수 없다", buni.inventory.First(c => c.id == "H2").text);
        }
    }
}
