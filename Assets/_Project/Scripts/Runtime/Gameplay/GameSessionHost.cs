using System;
using System.Collections.Generic;
using MurderMystery.Core;
using UnityEngine;

namespace MurderMystery.Gameplay
{
    /// <summary>
    /// Owns the single local GameSession for the prototype (later: the network host).
    /// The local player controls one character; the others are idle seats.
    /// </summary>
    public class GameSessionHost : MonoBehaviour
    {
        [SerializeField] private string localCharacterId = "jeong";
        [SerializeField] private int seed = 1;

        [SerializeField] private bool runTimers = true;

        public static GameSessionHost Instance { get; private set; }
        public LocalSessionGateway Gateway { get; private set; }
        public GameSession Session => Gateway.Session;
        public string LocalId => localCharacterId;
        public PlayerState Local => Session.Players[localCharacterId];

        public readonly List<string> Log = new List<string>();
        public event Action Changed;

        private void Awake()
        {
            Instance = this;
            Gateway = new LocalSessionGateway(GameData.LoadFromResources(), seed);
            Gateway.Announcement += line => Push($"도 검률: {line}");
            // Prototype starts straight at the first investigation round.
            Gateway.JumpTo(GamePhase.Investigation1);
            Push($"[{Session.Data.CharacterById[localCharacterId].name}] 으로 시작합니다. 1차 조사 단계.");
        }

        public void SetLocalCharacter(string characterId)
        {
            if (!Session.Players.ContainsKey(characterId)) return;
            localCharacterId = characterId;
            Push($"인물 변경: {Session.Data.CharacterById[characterId].name}");
        }

        private void Update()
        {
            if (runTimers) Gateway.Tick(Time.deltaTime);
        }

        public void AdvancePhase()
        {
            Gateway.Flow.Advance();
            Push($"단계: {PhaseLabel(Session.Phase)}");
        }

        public void Report(ActionResult r)
        {
            if (!r.Success)
            {
                Push(r.Error);
                return;
            }
            foreach (var id in r.GainedItems) Push($"획득: {Session.Data.ClueById[id].title}");
            foreach (var id in r.LearnedClues)
                if (!r.GainedItems.Contains(id)) Push($"알게 됨: {Session.Data.ClueById[id].title}");
            foreach (var m in r.Messages) Push(m);
            Changed?.Invoke();
        }

        public void Push(string line)
        {
            Log.Add(line);
            if (Log.Count > 8) Log.RemoveAt(0);
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
