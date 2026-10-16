using System;
using System.Collections.Generic;
using System.Linq;
using MurderMystery.Core;
using UnityEngine;

namespace MurderMystery.Gameplay
{
    /// <summary>
    /// Offline prototype: owns a LocalSessionGateway in-process. The local player controls one seat and can
    /// switch seats (debug); idle seats vote at random so a solo run can reach the ending.
    /// </summary>
    public class GameSessionHost : MonoBehaviour, IGameClient
    {
        [SerializeField] private string localCharacterId = "jeong";
        [SerializeField] private int seed = 1;
        [SerializeField] private bool runTimers = true;

        public static GameSessionHost Instance { get; private set; }
        public LocalSessionGateway Gateway { get; private set; }
        public GameSession Session => Gateway.Session;

        public string LocalId => localCharacterId;
        public GameData Data => Session.Data;
        public PlayerSnapshot Snapshot { get; private set; }
        public IReadOnlyList<string> Log => log;
        public bool IsOffline => true;
        public event Action Changed;

        private readonly List<string> log = new List<string>();
        private float refresh;

        private void Awake()
        {
            Instance = this;
            GameClient.Current = this;
            Gateway = new LocalSessionGateway(GameData.LoadFromResources(), seed);
            Gateway.Announcement += line => Push($"도 검률: {line}");
            // Prototype starts straight at the first investigation round.
            Gateway.JumpTo(GamePhase.Investigation1);
            Push($"[{Data.CharacterById[localCharacterId].name}] 으로 시작합니다. 1차 조사 단계.");
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(GameClient.Current, this)) GameClient.Current = null;
        }

        private void Update()
        {
            if (runTimers) Gateway.Tick(Time.deltaTime);
            refresh -= Time.deltaTime;
            if (refresh <= 0f) Refresh();
        }

        private void Refresh()
        {
            refresh = 0.25f;
            Snapshot = Gateway.GetSnapshot(localCharacterId);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ debug-only (offline)

        public void SetLocalCharacter(string characterId)
        {
            if (!Session.Players.ContainsKey(characterId)) return;
            localCharacterId = characterId;
            Push($"인물 변경: {Data.CharacterById[characterId].name}");
        }

        public void AdvancePhase()
        {
            Gateway.Flow.Advance();
            Push($"단계: {PhaseLabel(Session.Phase)}");
        }

        // ------------------------------------------------------------------ IGameClient

        public void Investigate(string spotId) => Report(Gateway.Investigate(LocalId, spotId));
        public void Retrieve(string spotId) => Report(Gateway.RetrieveHidden(LocalId, spotId));
        public void TryUnlock(string lockId) => Report(Gateway.TryUnlock(LocalId, lockId));
        public void Open(string clueId) => Report(Gateway.OpenEnvelope(LocalId, clueId));
        public void Disclose(string clueId) => Report(Gateway.Disclose(LocalId, clueId));
        public void Give(string toId, string itemId) => Report(Gateway.Give(LocalId, toId, itemId));
        public void Show(string toId, string clueId) => Report(Gateway.Show(LocalId, toId, clueId));
        public void RequestTalk(string toId) => Report(Gateway.RequestTalk(LocalId, toId));
        public void RespondTalk(int talkId, bool accept) => Report(Gateway.RespondTalk(LocalId, talkId, accept));
        public void EndTalk() => Report(Gateway.EndTalk(LocalId));
        public void SetReady(bool ready) => Report(Gateway.SetReady(LocalId, ready));

        public void Ask(string topicId)
        {
            var answer = Gateway.AskNpc(LocalId, topicId);
            Push($"도 검률: {answer.Text}");
            Refresh();
        }

        public void Vote(string targetId, int bonusAnswer)
        {
            Report(Gateway.CastVote(LocalId, targetId, bonusAnswer));
            // Idle seats vote at random so a solo run can finish.
            var rng = new System.Random();
            foreach (var seat in Session.Players.Keys.Where(k => k != LocalId).ToList())
            {
                if (Gateway.Vote == null || Gateway.Vote.Stage == VoteStage.Done) break;
                if (Gateway.Vote.HasVoted(seat)) continue;
                var options = Gateway.Vote.Candidates.Where(x => x != seat).ToList();
                if (options.Count > 0) Gateway.CastVote(seat, options[rng.Next(options.Count)], rng.Next(4));
            }
            Refresh();
        }

        public void Report(ActionResult r)
        {
            foreach (var line in Describe(Data, r)) Push(line);
            Refresh();
        }

        /// <summary>Human-readable log lines for an action result (shared with the network client).</summary>
        public static IEnumerable<string> Describe(GameData data, ActionResult r)
        {
            if (!r.Success)
            {
                yield return r.Error;
                yield break;
            }
            foreach (var id in r.GainedItems) yield return $"획득: {data.ClueById[id].title}";
            foreach (var id in r.LearnedClues)
                if (!r.GainedItems.Contains(id)) yield return $"알게 됨: {data.ClueById[id].title}";
            foreach (var m in r.Messages) yield return m;
        }

        public void Push(string line)
        {
            log.Add(line);
            if (log.Count > 8) log.RemoveAt(0);
            Changed?.Invoke();
        }

        public static string PhaseLabel(GamePhase phase) => phase switch
        {
            GamePhase.CharacterSelect => "배역 선택",
            GamePhase.Reading => "리딩",
            GamePhase.Introductions => "자기소개",
            GamePhase.AutopsyBriefing => "첫 검시 발표",
            GamePhase.Investigation1 => "1차 조사 (아침 7시)",
            GamePhase.Discussion1 => "1차 토론",
            GamePhase.Investigation2 => "2차 조사 (아침 9시)",
            GamePhase.Discussion2 => "2차 토론",
            GamePhase.FinalStatements => "최종 변론",
            GamePhase.Vote => "투표",
            _ => "엔딩"
        };
    }
}
