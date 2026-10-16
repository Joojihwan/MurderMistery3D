using System;
using System.Collections.Generic;
using System.Linq;

namespace MurderMystery.Core
{
    /// <summary>A private talking room (docs/design/game_rules.md §5).</summary>
    public class TalkRoom
    {
        public string Id;
        public string Name;
        public int Capacity;
        /// <summary>The council room: everyone is put in it for the council phase and nobody may leave.</summary>
        public bool Council;
        public readonly List<string> Members = new List<string>();
        public readonly List<RoomRequest> Requests = new List<RoomRequest>();
        // Consecutive refusals per requester; at RefusalLimit the requester is shut out until someone leaves.
        public readonly Dictionary<string, int> Refusals = new Dictionary<string, int>();
        public readonly HashSet<string> ShutOut = new HashSet<string>();
    }

    public class RoomRequest
    {
        public int Id;
        public string RoomId;
        public string Requester;
        /// <summary>Who was inside when the request was made: all of them must agree.</summary>
        public readonly HashSet<string> Approvers = new HashSet<string>();
        public readonly HashSet<string> Approved = new HashSet<string>();
    }

    /// <summary>
    /// Private rooms replace 1:1 secret talks. Two small rooms (3 people) can be used in the investigation
    /// phases: an empty room is entered freely; otherwise everyone inside at the time of the request must
    /// agree, each on their own (a later arrival does not need to agree to an earlier request). The
    /// requester never learns who refused. Three refusals in a row shut the requester out of that room until
    /// someone comes out; when it empties anyone may walk in again. A full room refuses automatically.
    /// The council room (5) holds everyone during the council phases and cannot be left.
    /// Messages for players other than the actor go to <see cref="Notify"/>.
    /// </summary>
    public class RoomManager
    {
        public const int RefusalLimit = 3;

        private readonly GameSession session;
        private readonly List<TalkRoom> rooms = new List<TalkRoom>();
        private int nextRequestId = 1;
        private GamePhase lastPhase;

        /// <summary>(player, message): something happened that this player should be told.</summary>
        public event Action<string, string> Notify;
        /// <summary>Membership changed (someone entered or left a room).</summary>
        public event Action Changed;

        public IReadOnlyList<TalkRoom> Rooms => rooms;

        public RoomManager(GameSession session)
        {
            this.session = session;
            rooms.Add(new TalkRoom { Id = "small1", Name = "작은 방 (동)", Capacity = 3 });
            rooms.Add(new TalkRoom { Id = "small2", Name = "작은 방 (서)", Capacity = 3 });
            rooms.Add(new TalkRoom { Id = "council", Name = "큰 방", Capacity = 5, Council = true });
            lastPhase = session.Phase;
            ApplyPhase();
        }

        public TalkRoom Room(string id) => rooms.FirstOrDefault(r => r.Id == id);
        public TalkRoom RoomOf(string playerId) => rooms.FirstOrDefault(r => r.Members.Contains(playerId));

        public bool InSameRoom(string a, string b)
        {
            var room = RoomOf(a);
            return room != null && room.Members.Contains(b);
        }

        public RoomRequest PendingOf(string requester) =>
            rooms.SelectMany(r => r.Requests).FirstOrDefault(q => q.Requester == requester);

        /// <summary>Requests this player still has to answer (inside the room, has not answered yet).</summary>
        public IEnumerable<RoomRequest> AwaitingAnswerFrom(string playerId) =>
            rooms.SelectMany(r => r.Requests).Where(q => q.Approvers.Contains(playerId) && !q.Approved.Contains(playerId));

        private static bool IsCouncilPhase(GamePhase p) => p == GamePhase.Council1 || p == GamePhase.Council2;

        public ActionResult RequestEnter(string playerId, string roomId)
        {
            SyncPhase();
            var room = Room(roomId);
            if (room == null || !session.Players.ContainsKey(playerId)) return ActionResult.Fail("없는 방입니다.");
            if (room.Council) return ActionResult.Fail("큰 방은 밀담 시간에만 열립니다.");
            if (session.InvestigationRound == 0) return ActionResult.Fail("작은 방은 조사 시간에만 쓸 수 있습니다.");
            var current = RoomOf(playerId);
            if (current == room) return ActionResult.Fail("이미 이 방 안에 있습니다.");
            if (current != null) return ActionResult.Fail("먼저 지금 있는 방에서 나오십시오.");
            if (PendingOf(playerId) != null) return ActionResult.Fail("이미 들어가기를 청해 두었습니다. 답을 기다리십시오.");
            if (room.ShutOut.Contains(playerId))
                return ActionResult.Fail("세 번 거절당했습니다. 안에서 누군가 나올 때까지 다시 청할 수 없습니다.");
            if (room.Members.Count >= room.Capacity) return ActionResult.Fail("방이 꽉 찼습니다.");

            var result = new ActionResult { Success = true };
            if (room.Members.Count == 0)
            {
                Admit(room, playerId);
                result.Messages.Add($"{room.Name}에 들어갔습니다.");
                return result;
            }

            var request = new RoomRequest { Id = nextRequestId++, RoomId = room.Id, Requester = playerId };
            request.Approvers.UnionWith(room.Members);
            room.Requests.Add(request);
            result.Messages.Add($"{room.Name}에 들어가기를 청했습니다. 안에 있는 사람이 모두 허락해야 들어갈 수 있습니다.");
            foreach (var m in room.Members) Notify?.Invoke(m, $"{Subject(playerId)} 들어오기를 청합니다.");
            return result;
        }

        public ActionResult Respond(string playerId, int requestId, bool accept)
        {
            SyncPhase();
            var room = rooms.FirstOrDefault(r => r.Requests.Any(q => q.Id == requestId));
            var request = room?.Requests.First(q => q.Id == requestId);
            if (request == null || !request.Approvers.Contains(playerId) || request.Approved.Contains(playerId))
                return ActionResult.Fail("답할 요청이 없습니다.");

            if (!accept)
            {
                Refuse(room, request, counts: true);
                return new ActionResult { Success = true };
            }
            request.Approved.Add(playerId);
            Settle(room, request);
            return new ActionResult { Success = true };
        }

        public ActionResult Leave(string playerId)
        {
            SyncPhase();
            var room = RoomOf(playerId);
            if (room == null) return ActionResult.Fail("방 안에 있지 않습니다.");
            if (room.Council) return ActionResult.Fail("밀담 시간이 끝날 때까지 큰 방을 나갈 수 없습니다.");
            Remove(room, playerId);
            var result = new ActionResult { Success = true };
            result.Messages.Add($"{room.Name}에서 나왔습니다.");
            return result;
        }

        /// <summary>Withdraws this player's own pending request.</summary>
        public ActionResult CancelRequest(string playerId)
        {
            var request = PendingOf(playerId);
            if (request == null) return ActionResult.Fail("청해 둔 요청이 없습니다.");
            Room(request.RoomId).Requests.Remove(request);
            return new ActionResult { Success = true };
        }

        public void Tick() => SyncPhase();

        // ------------------------------------------------------------------ internals

        private string Name(string id) => session.Data.CharacterById[id].name;

        /// <summary>The name with the right subject particle (이/가).</summary>
        private string Subject(string id) => Korean.WithParticle(Name(id), "이", "가");

        private void Admit(TalkRoom room, string playerId)
        {
            room.Members.Add(playerId);
            room.Refusals.Remove(playerId);
            // Whoever is in a room has no other request open, and newcomers need not agree to earlier ones.
            foreach (var r in rooms) r.Requests.RemoveAll(q => q.Requester == playerId);
            foreach (var m in room.Members.Where(m => m != playerId)) Notify?.Invoke(m, $"{Subject(playerId)} 들어왔습니다.");
            Changed?.Invoke();
        }

        private void Remove(TalkRoom room, string playerId)
        {
            room.Members.Remove(playerId);
            foreach (var m in room.Members) Notify?.Invoke(m, $"{Subject(playerId)} 나갔습니다.");
            // Someone came out: everyone may ask again.
            room.ShutOut.Clear();
            room.Refusals.Clear();
            if (room.Members.Count == 0)
            {
                // An empty room is open to anyone: waiting requests go in, first come first served.
                foreach (var q in room.Requests.ToList())
                {
                    room.Requests.Remove(q);
                    if (room.Members.Count < room.Capacity) Admit(room, q.Requester);
                    else Notify?.Invoke(q.Requester, $"{room.Name}이 꽉 차서 들어가지 못했습니다.");
                }
            }
            else
            {
                foreach (var q in room.Requests.ToList())
                {
                    q.Approvers.Remove(playerId);
                    q.Approved.Remove(playerId);
                    Settle(room, q);
                }
            }
            Changed?.Invoke();
        }

        /// <summary>Admits a request once everyone it waits for has agreed (or refuses it when the room is full).</summary>
        private void Settle(TalkRoom room, RoomRequest request)
        {
            if (!room.Requests.Contains(request) || !request.Approvers.IsSubsetOf(request.Approved)) return;
            room.Requests.Remove(request);
            if (room.Members.Count >= room.Capacity)
            {
                Notify?.Invoke(request.Requester, $"{room.Name}이 꽉 차서 들어가지 못했습니다.");
                return;
            }
            Admit(room, request.Requester);
            Notify?.Invoke(request.Requester, $"{room.Name}에 들어갔습니다.");
        }

        private void Refuse(TalkRoom room, RoomRequest request, bool counts)
        {
            room.Requests.Remove(request);
            var who = request.Requester;
            if (counts)
            {
                room.Refusals[who] = (room.Refusals.TryGetValue(who, out var n) ? n : 0) + 1;
                if (room.Refusals[who] >= RefusalLimit) room.ShutOut.Add(who);
            }
            Notify?.Invoke(who, room.ShutOut.Contains(who)
                ? $"{room.Name}에 들어가지 못했습니다. 세 번 거절당해, 안에서 누군가 나올 때까지 다시 청할 수 없습니다."
                : $"{room.Name}에 들어가지 못했습니다.");
        }

        // Phase changes: small rooms empty when investigation ends; the council phase gathers everyone.
        private void SyncPhase()
        {
            if (session.Phase == lastPhase) return;
            lastPhase = session.Phase;
            ApplyPhase();
        }

        private void ApplyPhase()
        {
            var changed = false;
            foreach (var room in rooms)
            {
                changed |= room.Members.Count > 0 || room.Requests.Count > 0;
                room.Members.Clear();
                room.Requests.Clear();
                room.Refusals.Clear();
                room.ShutOut.Clear();
            }
            if (IsCouncilPhase(session.Phase))
            {
                var council = rooms.First(r => r.Council);
                council.Members.AddRange(session.Players.Keys);
                changed = true;
            }
            if (changed) Changed?.Invoke();
        }
    }
}
