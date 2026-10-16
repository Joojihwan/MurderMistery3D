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

        private static LocalSessionGateway InInvestigation(GameData d)
        {
            var g = new LocalSessionGateway(d);
            g.JumpTo(GamePhase.Investigation1);
            return g;
        }

        private static int RequestId(LocalSessionGateway g, string requester) => g.Rooms.PendingOf(requester).Id;

        [Test]
        public void Room_EmptyIsFree_OthersNeedEveryInsideToAgree()
        {
            var g = InInvestigation(data);
            Assert.IsTrue(g.RequestRoom("jeong", "small1").Success);
            Assert.AreEqual("small1", g.Rooms.RoomOf("jeong").Id, "empty room: walk straight in");

            Assert.IsTrue(g.RequestRoom("buni", "small1").Success);
            Assert.IsNull(g.Rooms.RoomOf("buni"), "waits for jeong");
            Assert.IsTrue(g.RespondRoom("jeong", RequestId(g, "buni"), true).Success);
            Assert.AreEqual("small1", g.Rooms.RoomOf("buni").Id);

            // Two inside: both must agree; a later arrival is not asked about an earlier request.
            g.RequestRoom("heo", "small1");
            var heo = RequestId(g, "heo");
            g.RespondRoom("jeong", heo, true);
            Assert.IsNull(g.Rooms.RoomOf("heo"), "buni has not agreed yet");
            g.RespondRoom("buni", heo, true);
            Assert.AreEqual("small1", g.Rooms.RoomOf("heo").Id);

            Assert.IsFalse(g.RequestRoom("byeon", "small1").Success, "three is the limit");
        }

        [Test]
        public void Room_SimultaneousRequests_EachNeedsOnlyTheOriginalOccupants()
        {
            var g = InInvestigation(data);
            g.RequestRoom("jeong", "small1");
            g.RequestRoom("buni", "small1");
            g.RequestRoom("heo", "small1");
            g.RespondRoom("jeong", RequestId(g, "buni"), true);
            Assert.AreEqual("small1", g.Rooms.RoomOf("buni").Id);
            g.RespondRoom("jeong", RequestId(g, "heo"), true);
            Assert.AreEqual("small1", g.Rooms.RoomOf("heo").Id, "buni came in later and is not asked");
        }

        [Test]
        public void Room_FullRoomRefusesWaitingRequest()
        {
            var g = InInvestigation(data);
            g.RequestRoom("jeong", "small1");
            g.RequestRoom("buni", "small1");
            g.RequestRoom("heo", "small1");
            g.RequestRoom("byeon", "small1");
            g.RespondRoom("jeong", RequestId(g, "buni"), true);
            g.RespondRoom("jeong", RequestId(g, "heo"), true);
            g.RespondRoom("jeong", RequestId(g, "byeon"), true);
            Assert.IsNull(g.Rooms.RoomOf("byeon"), "full: refused automatically");
            Assert.IsNull(g.Rooms.PendingOf("byeon"));
        }

        [Test]
        public void Room_ThreeRefusalsShutOut_UntilSomeoneLeaves()
        {
            var g = InInvestigation(data);
            g.RequestRoom("jeong", "small1");
            for (var i = 0; i < 3; i++)
            {
                Assert.IsTrue(g.RequestRoom("byeon", "small1").Success, $"attempt {i + 1}");
                g.RespondRoom("jeong", RequestId(g, "byeon"), false);
            }
            Assert.IsFalse(g.RequestRoom("byeon", "small1").Success, "shut out after three refusals");
            Assert.IsTrue(g.RequestRoom("byeon", "small2").Success, "other room unaffected");
            g.LeaveRoom("byeon");

            g.RequestRoom("buni", "small1");
            g.RespondRoom("jeong", RequestId(g, "buni"), true);
            g.LeaveRoom("buni");                       // someone came out
            Assert.IsTrue(g.RequestRoom("byeon", "small1").Success, "may ask again once someone leaves");
            g.LeaveRoom("byeon");                      // withdraws the request
            g.LeaveRoom("jeong");
            Assert.IsTrue(g.RequestRoom("byeon", "small1").Success);
            Assert.AreEqual("small1", g.Rooms.RoomOf("byeon").Id, "empty again: anyone walks in");
        }

        [Test]
        public void Room_RequesterNeverLearnsWhoRefused()
        {
            var g = InInvestigation(data);
            var notices = new List<(string who, string text)>();
            g.Notice += (who, text) => notices.Add((who, text));
            g.RequestRoom("jeong", "small1");
            g.RequestRoom("buni", "small1");
            g.RespondRoom("jeong", RequestId(g, "buni"), true);
            g.RequestRoom("heo", "small1");
            g.RespondRoom("buni", RequestId(g, "heo"), false);
            var told = notices.Where(n => n.who == "heo").Select(n => n.text).ToList();
            Assert.IsNotEmpty(told);
            Assert.IsFalse(told.Any(t => t.Contains("분이") || t.Contains("정씨")), string.Join(" / ", told));
        }

        [Test]
        public void Council_GathersEveryone_NoLeaving_ThenEmpties()
        {
            var g = InInvestigation(data);
            g.RequestRoom("jeong", "small1");
            g.Flow.Advance();                          // council
            Assert.AreEqual(GamePhase.Council1, g.Session.Phase);
            g.Tick(0f);
            Assert.AreEqual(5, g.Rooms.Room("council").Members.Count);
            Assert.IsNull(g.Rooms.Room("small1").Members.FirstOrDefault());
            Assert.IsFalse(g.LeaveRoom("jeong").Success, "cannot leave the council room");
            Assert.IsFalse(g.RequestRoom("buni", "small2").Success);

            g.Flow.Advance();                          // discussion
            g.Tick(0f);
            Assert.AreEqual(0, g.Rooms.Room("council").Members.Count);
        }

        [Test]
        public void ShowAndGive_RequireSameRoomOrProximity()
        {
            var g = InInvestigation(data);
            Assert.IsFalse(g.Show("buni", "jeong", "H2").Success);
            Assert.IsFalse(g.Give("buni", "jeong", "H2").Success);

            g.RequestRoom("buni", "small2");
            g.RequestRoom("jeong", "small2");
            g.RespondRoom("buni", RequestId(g, "jeong"), true);
            Assert.IsTrue(g.Show("buni", "jeong", "H2").Success);
            g.LeaveRoom("jeong");

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
        public void PartialTable_ReadyAndVoteOnlyWaitForOccupiedSeats()
        {
            var g = new LocalSessionGateway(data);
            g.ActiveSeats = () => new[] { "jeong", "jaeha" };
            g.SetReady("jeong", true);
            g.SetReady("jaeha", true);
            Assert.AreEqual(GamePhase.Reading, g.Session.Phase, "two humans ready is enough");

            g.JumpTo(GamePhase.FinalStatements);
            g.Flow.Advance();
            g.CastVote("jeong", "jaeha", 1);
            g.CastVote("jaeha", "jeong", -1);
            // 1:1 tie → runoff between both; each can only vote for the other → tie → unresolved.
            g.CastVote("jeong", "jaeha", 1);
            g.CastVote("jaeha", "jeong", -1);
            Assert.AreEqual(GamePhase.Ending, g.Session.Phase);
            Assert.AreEqual("C", g.Vote.EndingKey);
        }

        [Test]
        public void VoteTimeout_ForcesAResult()
        {
            var g = new LocalSessionGateway(data);
            g.JumpTo(GamePhase.FinalStatements);
            g.Flow.Advance();
            g.CastVote("jeong", "byeon", 0);
            g.Tick(1000f);   // vote timer expires
            Assert.AreEqual(GamePhase.Ending, g.Session.Phase);
            Assert.AreEqual("B_byeon", g.Vote.EndingKey);
            Assert.AreEqual("B_byeon", g.GetSnapshot("heo").endingKey);
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
