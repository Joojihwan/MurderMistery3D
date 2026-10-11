using System.Collections.Generic;
using System.Linq;

namespace MurderMystery.Core
{
    /// <summary>
    /// Authoring checks from docs/design/npc_rules.md §8 plus cross-file reference integrity.
    /// Returns human-readable issues; an empty list means the data is consistent.
    /// </summary>
    public static class DataValidator
    {
        public const int MinFactVariants = 3;
        public const int MinPoolLines = 5;

        // Evaluative words that would leak an opinion through the neutral NPC's shared pools.
        private static readonly string[] BannedPoolWords = { "수상", "의심", "범인", "거짓", "틀림없", "분명히" };

        public static List<string> Validate(GameData data)
        {
            var issues = new List<string>();
            var facts = data.AllFactIds;
            var characters = data.CharacterById;

            void CheckFact(string where, string id)
            {
                if (!facts.Contains(id)) issues.Add($"{where}: unknown fact '{id}'");
            }

            // NPC dialogue.
            if (data.Dialogue.refusals.Length < MinPoolLines) issues.Add($"refusals: need ≥{MinPoolLines} lines");
            if (data.Dialogue.openers.Length < MinPoolLines) issues.Add($"openers: need ≥{MinPoolLines} lines");
            if (data.Dialogue.closers.Length < MinPoolLines) issues.Add($"closers: need ≥{MinPoolLines} lines");
            foreach (var line in data.Dialogue.refusals.Concat(data.Dialogue.openers).Concat(data.Dialogue.closers))
            foreach (var w in BannedPoolWords.Where(line.Contains))
                issues.Add($"pool line '{line}' contains evaluative word '{w}'");

            foreach (var t in data.Dialogue.topics)
            {
                foreach (var f in t.visibleIfAny) CheckFact($"{t.id}.visibleIfAny", f);
                if (t.entries.Length == 0) issues.Add($"{t.id}: no entries");
                foreach (var e in t.entries)
                {
                    var where = $"{t.id}[tier {e.tier}]";
                    foreach (var f in e.requiredFacts) CheckFact(where, f);
                    foreach (var f in e.conveysFacts) CheckFact(where, f);
                    var allowed = new HashSet<string>(e.requiredFacts);
                    allowed.UnionWith(data.PublicFacts);
                    foreach (var f in e.conveysFacts.Where(f => !allowed.Contains(f)))
                        issues.Add($"{where}: conveys '{f}' that the asker is not guaranteed to know (NPC must not add information)");
                    if (e.variants.Length < MinFactVariants) issues.Add($"{where}: need ≥{MinFactVariants} variants");
                    if (e.variants.Distinct().Count() != e.variants.Length) issues.Add($"{where}: duplicate variants");
                }
                // A visible topic whose lowest entry needs facts outside visibleIfAny is fine (refusal),
                // but an always-visible topic should normally answer everyone.
                if (t.visibleIfAny.Length == 0 && t.entries.All(e => e.requiredFacts.Any(f => !data.PublicFacts.Contains(f))))
                    issues.Add($"{t.id}: always visible but has no answer reachable from public facts");
            }

            // Characters and goals.
            var culprits = data.Characters.characters.Count(c => c.isCulprit);
            if (culprits != 1) issues.Add($"expected exactly one culprit, found {culprits}");
            if (!characters.ContainsKey(data.Characters.culpritId) || !characters[data.Characters.culpritId].isCulprit)
                issues.Add($"culpritId '{data.Characters.culpritId}' does not match the isCulprit flag");

            var secretGoals = new HashSet<string>();
            foreach (var c in data.Characters.characters)
            {
                foreach (var f in c.startFacts) CheckFact($"{c.id}.startFacts", f);
                foreach (var i in c.startItems)
                    if (!data.ClueById.TryGetValue(i, out var clue) || clue.Kind != ClueKind.Item)
                        issues.Add($"{c.id}.startItems: '{i}' is not an item clue");
                foreach (var g in c.goals)
                {
                    if (g.Type == GoalType.Secret) secretGoals.Add(g.id);
                    if (g.Type == GoalType.Possess)
                        foreach (var i in g.itemIds)
                            if (!data.ClueById.TryGetValue(i, out var clue) || clue.Kind != ClueKind.Item)
                                issues.Add($"{g.id}: possess target '{i}' is not an item clue");
                    if (g.Type == GoalType.BonusIntent && c.isCulprit)
                        issues.Add($"{g.id}: the culprit cannot have the bonus goal");
                }
            }

            // Clues.
            var exposedGoals = new HashSet<string>();
            foreach (var clue in data.Clues.clues)
            {
                if (clue.Kind != ClueKind.Public && clue.SpotIds.Length == 0 &&
                    !data.Characters.characters.Any(c => c.startItems.Contains(clue.id)))
                    issues.Add($"{clue.id}: not public, not placed on a spot and nobody starts with it");
                if (!string.IsNullOrEmpty(clue.owner) && !characters.ContainsKey(clue.owner))
                    issues.Add($"{clue.id}: unknown owner '{clue.owner}'");
                if (clue.IsDeep && clue.Kind == ClueKind.Item && string.IsNullOrEmpty(clue.owner))
                    issues.Add($"{clue.id}: deep item without an owner (nobody can retrieve it in round 1)");
                if (clue.@sealed && string.IsNullOrEmpty(clue.sealedText))
                    issues.Add($"{clue.id}: sealed but has no sealedText");
                foreach (var g in clue.exposes)
                {
                    if (!secretGoals.Contains(g)) issues.Add($"{clue.id}: exposes unknown secret goal '{g}'");
                    exposedGoals.Add(g);
                }
            }
            foreach (var g in secretGoals.Where(g => !exposedGoals.Contains(g)))
                issues.Add($"{g}: no clue can expose this secret, so it can never fail");

            // Flow: every phase needs a config; only Ending may have no duration.
            var configured = new HashSet<string>(data.Flow.phases.Select(p => p.phase));
            foreach (var phase in System.Enum.GetNames(typeof(GamePhase)).Where(n => !configured.Contains(n)))
                issues.Add($"flow: no config for phase {phase}");
            foreach (var p in data.Flow.phases.Where(p => p.phase != nameof(GamePhase.Ending) && p.durationSec <= 0))
                issues.Add($"flow: phase {p.phase} has no duration");

            // Endings: A, C and one B per innocent character; epilogue documents must be sealed items.
            var endingKeys = new HashSet<string>(data.Endings.endings.Select(e => e.key));
            var required = new List<string> { "A", "C" };
            required.AddRange(data.Characters.characters.Where(c => !c.isCulprit).Select(c => $"B_{c.id}"));
            foreach (var k in required.Where(k => !endingKeys.Contains(k))) issues.Add($"endings: missing '{k}'");
            foreach (var id in data.Endings.epilogue.documentOrder)
                if (!data.ClueById.TryGetValue(id, out var doc) || !doc.@sealed)
                    issues.Add($"endings: epilogue document '{id}' is not a sealed clue");

            var deepSpots = data.Clues.clues.Where(c => c.IsDeep).SelectMany(c => c.SpotIds).Distinct();
            foreach (var s in deepSpots.Where(s => data.RetrievedTraceText(s) == null))
                issues.Add($"spot {s}: deep spot without a retrieved-trace text");

            return issues;
        }
    }
}
