using System.Collections.Generic;
using System.Linq;
using MurderMystery.Core;
using NUnit.Framework;

namespace MurderMystery.Tests
{
    /// <summary>
    /// Monte-Carlo playthroughs of the investigation rounds with simple random agents.
    /// Measures how often each clue is discovered by anyone / by a non-culprit. Explicit: run on demand.
    /// </summary>
    public class BalanceSimulation
    {
        [Test, Explicit("Prints balance statistics; run manually")]
        public void InvestigationCoverage()
        {
            var data = TestData.Load();
            const int runs = 2000;
            var foundByAnyone = new Dictionary<string, int>();
            var foundByInnocent = new Dictionary<string, int>();
            var retrievedRound1 = 0;
            var culpritHidC1 = 0;

            foreach (var scenario in new[] { "random", "culprit-cleans-up" })
            {
                foundByAnyone.Clear();
                foundByInnocent.Clear();
                retrievedRound1 = 0;
                culpritHidC1 = 0;

                for (var run = 0; run < runs; run++)
                {
                    var rng = new System.Random(run);
                    var s = new GameSession(data, run);
                    s.SetPhase(GamePhase.Investigation1);
                    PlayRound(s, rng, 1, scenario, ref retrievedRound1, ref culpritHidC1);
                    s.SetPhase(GamePhase.Investigation2);
                    PlayRound(s, rng, 2, scenario, ref retrievedRound1, ref culpritHidC1);

                    foreach (var clue in data.Clues.clues.Where(c => c.Kind != ClueKind.Public))
                    {
                        var holders = s.Players.Values.Where(p => p.KnownFacts.Contains(clue.id) && !data.CharacterById[p.CharacterId].startItems.Contains(clue.id)).ToList();
                        if (holders.Count > 0) Inc(foundByAnyone, clue.id);
                        if (holders.Any(p => p.CharacterId != data.Characters.culpritId)) Inc(foundByInnocent, clue.id);
                    }
                }

                TestContext.WriteLine($"=== {scenario} ({runs} runs) ===");
                TestContext.WriteLine($"owner retrievals in round 1 per game: {retrievedRound1 / (double)runs:F2}");
                if (scenario != "random") TestContext.WriteLine($"culprit took C1 before anyone else: {culpritHidC1 * 100.0 / runs:F0}%");
                foreach (var clue in data.Clues.clues.Where(c => c.Kind != ClueKind.Public))
                {
                    foundByAnyone.TryGetValue(clue.id, out var a);
                    foundByInnocent.TryGetValue(clue.id, out var b);
                    TestContext.WriteLine($"{clue.id,-4} {clue.title,-20} anyone {a * 100.0 / runs,5:F0}%   non-culprit {b * 100.0 / runs,5:F0}%");
                }
            }
        }

        private static void PlayRound(GameSession s, System.Random rng, int round, string scenario, ref int retrieved, ref int culpritHidC1)
        {
            var data = s.Data;
            var order = s.Players.Keys.OrderBy(_ => rng.Next()).ToList();
            // Players act in turns, one action each, until everyone is out of points.
            var anyActed = true;
            while (anyActed)
            {
                anyActed = false;
                foreach (var id in order)
                {
                    var p = s.Players[id];
                    if (p.InvestigationPoints <= 0) continue;

                    // Owners retrieve their own hidden items in round 1 half the time (the culprit always, when cleaning up).
                    var ownSpot = s.Spots.Values.FirstOrDefault(sp => sp.RemainingItems.Any(i => data.ClueById[i].owner == id));
                    var wantsRetrieve = ownSpot != null && p.InvestigationPoints >= GameRules.RetrieveCost &&
                                        (round == 2 || rng.NextDouble() < 0.5 || (scenario != "random" && data.CharacterById[id].isCulprit));
                    if (wantsRetrieve && s.RetrieveHidden(id, ownSpot.SpotId).Success)
                    {
                        if (round == 1) retrieved++;
                        anyActed = true;
                        continue;
                    }

                    if (scenario != "random" && data.CharacterById[id].isCulprit && s.Spots["04"].RemainingItems.Contains("C1"))
                    {
                        if (s.Investigate(id, "04").Success) { culpritHidC1++; anyActed = true; continue; }
                    }

                    var candidates = s.Spots.Values
                        .Where(sp => !sp.Deep || round == 2)
                        .Where(sp => !sp.Traces.All(t => p.KnownFacts.Contains(t)) || sp.RemainingItems.Count > 0)
                        .ToList();
                    if (candidates.Count == 0) { p.InvestigationPoints = 0; continue; }
                    var pick = candidates[rng.Next(candidates.Count)];
                    if (s.Investigate(id, pick.SpotId).Success) anyActed = true;
                    else p.InvestigationPoints = 0;
                }
            }
        }

        private static void Inc(Dictionary<string, int> d, string k) => d[k] = d.TryGetValue(k, out var v) ? v + 1 : 1;
    }
}
