using System;
using System.Collections.Generic;
using System.Linq;

namespace MurderMystery.Core
{
    public class NpcAnswer
    {
        public string Text;
        public bool Refused;
        public string TopicId;
        public int Tier = -1;
    }

    /// <summary>
    /// Rule-based answers for the neutral NPC (docs/design/npc_rules.md). Answers only from
    /// public facts ∪ the asker's facts; anything else gets a line from the single refusal pool.
    /// </summary>
    public class NpcResponder
    {
        private readonly GameData data;
        private readonly Random rng;
        private readonly Dictionary<string, ShuffleBag> bags = new Dictionary<string, ShuffleBag>();

        public NpcResponder(GameData data, Random rng)
        {
            this.data = data;
            this.rng = rng;
        }

        public IEnumerable<TopicData> VisibleTopics(ISet<string> known) =>
            data.Dialogue.topics.Where(t => t.visibleIfAny.Length == 0 || t.visibleIfAny.Any(known.Contains));

        public NpcAnswer Answer(string askerId, string topicId, ISet<string> known)
        {
            data.TopicById.TryGetValue(topicId, out var topic);
            var entry = topic?.entries
                .OrderByDescending(e => e.tier)
                .FirstOrDefault(e => e.requiredFacts.All(known.Contains));

            string body;
            var answer = new NpcAnswer { TopicId = topicId };
            if (entry == null)
            {
                // Same pool and draw regardless of whether the info exists elsewhere (R4).
                body = Draw($"{askerId}|refusal", data.Dialogue.refusals);
                answer.Refused = true;
            }
            else
            {
                var entryIndex = Array.IndexOf(topic.entries, entry);
                body = Draw($"{askerId}|{topicId}|{entryIndex}", entry.variants);
                answer.Tier = entry.tier;
            }

            var opener = Draw($"{askerId}|opener", data.Dialogue.openers);
            var closer = Draw($"{askerId}|closer", data.Dialogue.closers);
            answer.Text = string.Join(" ", new[] { opener, body, closer }.Where(s => !string.IsNullOrEmpty(s)));
            return answer;
        }

        private string Draw(string key, string[] pool)
        {
            if (pool == null || pool.Length == 0) return string.Empty;
            if (!bags.TryGetValue(key, out var bag))
            {
                bag = new ShuffleBag(pool.Length, rng);
                bags[key] = bag;
            }
            return pool[bag.Next()];
        }
    }
}
