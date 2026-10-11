using System;
using System.Collections.Generic;
using System.Linq;

namespace MurderMystery.Core
{
    public class SecretTalk
    {
        public int Id;
        public string Initiator;
        public string Partner;
        public float Remaining;
        public bool Active;

        public bool Involves(string id) => Initiator == id || Partner == id;
    }

    /// <summary>
    /// 1:1 secret talks (game_rules.md §5): investigation phases only, 3 minutes each, each player may
    /// initiate 2 per round (receiving is unlimited), one talk at a time per player.
    /// </summary>
    public class SecretTalkManager
    {
        public const float TalkSeconds = 180f;

        private readonly GameSession session;
        private readonly List<SecretTalk> talks = new List<SecretTalk>();
        private int nextId = 1;
        private GamePhase lastPhase;

        public event Action<SecretTalk> Started;
        public event Action<SecretTalk> Ended;

        public SecretTalkManager(GameSession session)
        {
            this.session = session;
            lastPhase = session.Phase;
        }

        public IEnumerable<SecretTalk> Pending => talks.Where(t => !t.Active);
        public IEnumerable<SecretTalk> ActiveTalks => talks.Where(t => t.Active);

        public SecretTalk ActiveFor(string playerId) => talks.FirstOrDefault(t => t.Active && t.Involves(playerId));

        public bool InTalkTogether(string a, string b)
        {
            var t = ActiveFor(a);
            return t != null && t.Involves(b);
        }

        public ActionResult Request(string from, string to)
        {
            SyncPhase();
            if (session.InvestigationRound == 0) return ActionResult.Fail("밀담은 조사 단계에서만 할 수 있습니다.");
            if (from == to || !session.Players.ContainsKey(from) || !session.Players.ContainsKey(to))
                return ActionResult.Fail("밀담 상대가 올바르지 않습니다.");
            if (session.Players[from].SecretTalksLeft <= 0) return ActionResult.Fail("이번 조사에서는 더 밀담을 청할 수 없습니다.");
            if (ActiveFor(from) != null) return ActionResult.Fail("이미 밀담 중입니다.");
            if (talks.Any(t => !t.Active && t.Initiator == from && t.Partner == to)) return ActionResult.Fail("이미 청한 밀담입니다.");

            var talk = new SecretTalk { Id = nextId++, Initiator = from, Partner = to };
            talks.Add(talk);
            var r = new ActionResult { Success = true, TalkId = talk.Id };
            r.Messages.Add($"{session.Data.CharacterById[to].name}에게 밀담을 청했습니다.");
            return r;
        }

        public ActionResult Respond(string responder, int talkId, bool accept)
        {
            SyncPhase();
            var talk = talks.FirstOrDefault(t => t.Id == talkId && !t.Active);
            if (talk == null || talk.Partner != responder) return ActionResult.Fail("응답할 밀담 요청이 없습니다.");
            talks.Remove(talk);
            if (!accept) return new ActionResult { Success = true };

            if (ActiveFor(talk.Initiator) != null || ActiveFor(responder) != null)
                return ActionResult.Fail("상대가 이미 다른 밀담 중입니다.");
            var initiator = session.Players[talk.Initiator];
            if (initiator.SecretTalksLeft <= 0) return ActionResult.Fail("요청한 사람이 더 밀담을 청할 수 없습니다.");

            initiator.SecretTalksLeft--;
            talk.Active = true;
            talk.Remaining = TalkSeconds;
            talks.Add(talk);
            // A player in a talk withdraws their other pending requests.
            talks.RemoveAll(t => !t.Active && (t.Involves(talk.Initiator) || t.Involves(talk.Partner)));
            Started?.Invoke(talk);
            return new ActionResult { Success = true };
        }

        public void End(int talkId)
        {
            var talk = talks.FirstOrDefault(t => t.Id == talkId && t.Active);
            if (talk == null) return;
            talks.Remove(talk);
            talk.Active = false;
            Ended?.Invoke(talk);
        }

        public void Tick(float deltaSeconds)
        {
            SyncPhase();
            foreach (var t in talks.Where(t => t.Active).ToList())
            {
                t.Remaining -= deltaSeconds;
                if (t.Remaining <= 0f) End(t.Id);
            }
        }

        // Phase changes close every talk and drop pending requests.
        private void SyncPhase()
        {
            if (session.Phase == lastPhase) return;
            lastPhase = session.Phase;
            foreach (var t in talks.Where(t => t.Active).ToList()) End(t.Id);
            talks.Clear();
        }
    }
}
