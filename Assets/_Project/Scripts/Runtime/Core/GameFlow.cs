using System;
using System.Collections.Generic;
using System.Linq;

namespace MurderMystery.Core
{
    [Serializable]
    public class PhaseConfig
    {
        public string phase;
        public float durationSec;
        public string clock;
        public float warnAtSec;
        public string announcement;
        public string warning;

        public GamePhase Phase => (GamePhase)Enum.Parse(typeof(GamePhase), phase);
    }

    [Serializable]
    public class FlowFile
    {
        public PhaseConfig[] phases = Array.Empty<PhaseConfig>();
    }

    /// <summary>
    /// Drives phase timing on the host: counts down each phase, fires the NPC announcement on entry
    /// and a warning near the end, and advances early when every player marks ready.
    /// Phases with durationSec 0 (Ending) never auto-advance.
    /// </summary>
    public class GameFlow
    {
        private readonly GameSession session;
        private readonly Dictionary<GamePhase, PhaseConfig> configs;
        private readonly HashSet<string> ready = new HashSet<string>();
        private bool warned;

        public float Remaining { get; private set; }
        public PhaseConfig Current => configs.TryGetValue(session.Phase, out var c) ? c : null;
        public IReadOnlyCollection<string> Ready => ready;

        public event Action<GamePhase, PhaseConfig> PhaseStarted;
        public event Action<string> Warning;

        public GameFlow(GameSession session, FlowFile flow)
        {
            this.session = session;
            configs = flow.phases.ToDictionary(p => p.Phase);
            EnterCurrent();
        }

        public void Tick(float deltaSeconds)
        {
            var cfg = Current;
            if (cfg == null || cfg.durationSec <= 0f) return;

            Remaining -= deltaSeconds;
            if (!warned && cfg.warnAtSec > 0f && Remaining <= cfg.warnAtSec && !string.IsNullOrEmpty(cfg.warning))
            {
                warned = true;
                Warning?.Invoke(cfg.warning);
            }
            if (Remaining <= 0f) Advance();
        }

        /// <summary>Marks a player ready to move on; when all seats are ready the phase ends early.</summary>
        public void SetReady(string playerId, bool isReady)
        {
            if (isReady) ready.Add(playerId);
            else ready.Remove(playerId);
            var cfg = Current;
            if (cfg != null && cfg.durationSec > 0f && session.Players.Keys.All(ready.Contains)) Advance();
        }

        public void Advance()
        {
            if (session.Phase == GamePhase.Ending) return;
            session.AdvancePhase();
            EnterCurrent();
        }

        /// <summary>Re-enters the session's current phase (after a debug/test jump), resetting its timer.</summary>
        public void Resync() => EnterCurrent();

        private void EnterCurrent()
        {
            ready.Clear();
            warned = false;
            var cfg = Current;
            Remaining = cfg?.durationSec ?? 0f;
            PhaseStarted?.Invoke(session.Phase, cfg);
        }
    }
}
