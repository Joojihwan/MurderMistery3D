using System;
using System.Collections.Generic;
using MurderMystery.Core;

namespace MurderMystery.Gameplay
{
    /// <summary>
    /// What the local player's UI and controls talk to. The offline prototype runs the session in-process;
    /// the networked client forwards intents to the host and only ever sees its own snapshot.
    /// </summary>
    public interface IGameClient
    {
        string LocalId { get; }
        /// <summary>Public scenario data (names, bonus question, ending texts). Never per-player state.</summary>
        GameData Data { get; }
        PlayerSnapshot Snapshot { get; }
        IReadOnlyList<string> Log { get; }
        /// <summary>True for the in-process prototype, which also allows debug controls.</summary>
        bool IsOffline { get; }

        event Action Changed;

        void Investigate(string spotId);
        void Retrieve(string spotId);
        /// <summary>Try a locked door or container with the keys the player holds.</summary>
        void TryUnlock(string lockId);
        void Open(string clueId);
        void Disclose(string clueId);
        void Give(string toId, string itemId);
        void Show(string toId, string clueId);
        void Ask(string topicId);
        void RequestTalk(string toId);
        void RespondTalk(int talkId, bool accept);
        void EndTalk();
        void SetReady(bool ready);
        void Vote(string targetId, int bonusAnswer);
    }

    /// <summary>The client the HUD and interactor use; set by the offline host or the network client.</summary>
    public static class GameClient
    {
        public static IGameClient Current { get; set; }
    }
}
