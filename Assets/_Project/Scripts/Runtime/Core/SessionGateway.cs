using System;
using System.Collections.Generic;
using System.Linq;

namespace MurderMystery.Core
{
    /// <summary>
    /// The intents a client can send. The prototype calls <see cref="LocalSessionGateway"/> directly;
    /// the networked version forwards the same calls to the host over RPC.
    /// </summary>
    public interface ISessionGateway
    {
        PlayerSnapshot GetSnapshot(string playerId);
        ActionResult Investigate(string playerId, string spotId, string preferredItem = null);
        ActionResult RetrieveHidden(string playerId, string spotId);
        ActionResult TryUnlock(string playerId, string lockId);
        ActionResult OpenEnvelope(string playerId, string clueId);
        ActionResult Disclose(string playerId, string clueId);
        ActionResult Give(string fromId, string toId, string itemId);
        ActionResult Show(string fromId, string toId, string clueId);
        NpcAnswer AskNpc(string playerId, string topicId);
        ActionResult RequestRoom(string playerId, string roomId);
        ActionResult RespondRoom(string playerId, int requestId, bool accept);
        ActionResult LeaveRoom(string playerId);
        ActionResult SetReady(string playerId, bool ready);
        ActionResult CastVote(string voterId, string targetId, int bonusAnswer);
    }

    /// <summary>
    /// Host-side implementation: owns the session, phase flow, the private rooms and the vote.
    /// Enforces the "hand items over only in the same room or when nearby" rule; proximity is
    /// supplied by the caller because the rule core knows nothing about positions.
    /// </summary>
    public class LocalSessionGateway : ISessionGateway
    {
        public GameSession Session { get; }
        public GameFlow Flow { get; }
        public RoomManager Rooms { get; }
        /// <summary>Messages for one player that are not the answer to their own action (room requests...).</summary>
        public event Action<string, string> Notice;
        public VoteFlow Vote { get; private set; }

        public Func<string, string, bool> AreNearby { get; set; } = (_, __) => false;

        /// <summary>Seats taken by real players. Ready-skip and vote closing only wait for these.</summary>
        public Func<IEnumerable<string>> ActiveSeats
        {
            get => activeSeats;
            set
            {
                activeSeats = value;
                Flow.RequiredSeats = value;
            }
        }
        private Func<IEnumerable<string>> activeSeats;

        public event Action<string> Announcement;

        public LocalSessionGateway(GameData data, int seed = 0)
        {
            Session = new GameSession(data, seed);
            Flow = new GameFlow(Session, data.Flow);
            Rooms = new RoomManager(Session);
            Rooms.Notify += (who, text) =>
            {
                AddNotice(who, text);
                Notice?.Invoke(who, text);
            };
            Flow.PhaseStarted += OnPhaseStarted;
            Flow.Warning += w => Announcement?.Invoke(w);
        }

        /// <summary>Debug/test helper: jump straight to a phase with a fresh timer.</summary>
        public void JumpTo(GamePhase phase)
        {
            Session.SetPhase(phase);
            Flow.Resync();
        }

        public void Tick(float dt)
        {
            Flow.Tick(dt);
            Rooms.Tick();
        }

        private void OnPhaseStarted(GamePhase phase, PhaseConfig cfg)
        {
            if (phase == GamePhase.Vote) Vote = new VoteFlow(Session, activeSeats);
            if (phase == GamePhase.Ending) Vote?.ForceFinish();
            if (!string.IsNullOrEmpty(cfg?.announcement)) Announcement?.Invoke(cfg.announcement);
        }

        public PlayerSnapshot GetSnapshot(string playerId) => PlayerSnapshot.Build(this, playerId);

        private readonly Dictionary<string, List<NoticeView>> notices = new Dictionary<string, List<NoticeView>>();
        private int noticeSeq;

        private void AddNotice(string playerId, string text)
        {
            if (!notices.TryGetValue(playerId, out var list)) notices[playerId] = list = new List<NoticeView>();
            list.Add(new NoticeView { seq = ++noticeSeq, text = text });
            if (list.Count > 6) list.RemoveAt(0);
        }

        public IEnumerable<NoticeView> NoticesFor(string playerId) =>
            notices.TryGetValue(playerId, out var list) ? list : Enumerable.Empty<NoticeView>();
        public ActionResult Investigate(string playerId, string spotId, string preferredItem = null) =>
            Session.Investigate(playerId, spotId, preferredItem);
        public ActionResult RetrieveHidden(string playerId, string spotId) => Session.RetrieveHidden(playerId, spotId);
        public ActionResult TryUnlock(string playerId, string lockId) => Session.TryUnlock(playerId, lockId);
        public ActionResult OpenEnvelope(string playerId, string clueId) => Session.OpenEnvelope(playerId, clueId);
        public ActionResult Disclose(string playerId, string clueId) => Session.Disclose(playerId, clueId);

        public ActionResult Give(string fromId, string toId, string itemId)
        {
            if (!Rooms.InSameRoom(fromId, toId) && !AreNearby(fromId, toId))
                return ActionResult.Fail("물건은 같은 방 안에 있거나 가까이 있을 때만 건넬 수 있습니다.");
            return Session.Give(fromId, toId, itemId);
        }

        public ActionResult Show(string fromId, string toId, string clueId)
        {
            if (!Rooms.InSameRoom(fromId, toId)) return ActionResult.Fail("보여 주기는 같은 방 안에서만 할 수 있습니다.");
            return Session.Show(fromId, toId, clueId);
        }

        public NpcAnswer AskNpc(string playerId, string topicId) => Session.AskNpc(playerId, topicId);
        public ActionResult RequestRoom(string playerId, string roomId) => Rooms.RequestEnter(playerId, roomId);
        public ActionResult RespondRoom(string playerId, int requestId, bool accept) => Rooms.Respond(playerId, requestId, accept);
        public ActionResult LeaveRoom(string playerId) =>
            Rooms.PendingOf(playerId) != null && Rooms.RoomOf(playerId) == null ? Rooms.CancelRequest(playerId) : Rooms.Leave(playerId);

        public ActionResult SetReady(string playerId, bool ready)
        {
            Flow.SetReady(playerId, ready);
            return new ActionResult { Success = true };
        }

        public ActionResult CastVote(string voterId, string targetId, int bonusAnswer)
        {
            if (Session.Phase != GamePhase.Vote || Vote == null) return ActionResult.Fail("투표 단계가 아닙니다.");
            var r = Vote.Cast(voterId, targetId, bonusAnswer);
            if (r.Success && Vote.AllVoted && Vote.Close()) Flow.Advance();
            return r;
        }
    }
}
