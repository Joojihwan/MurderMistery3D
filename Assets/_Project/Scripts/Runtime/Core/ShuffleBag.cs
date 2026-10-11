using System;
using System.Collections.Generic;

namespace MurderMystery.Core
{
    /// <summary>
    /// Hands out indices 0..count-1 in shuffled order, reshuffling when empty, and never repeats
    /// the last index across a reshuffle boundary (docs/design/npc_rules.md §5).
    /// </summary>
    public class ShuffleBag
    {
        private readonly int count;
        private readonly Random rng;
        private readonly Queue<int> queue = new Queue<int>();
        private int last = -1;

        public ShuffleBag(int count, Random rng)
        {
            this.count = count;
            this.rng = rng;
        }

        public int Next()
        {
            if (count <= 0) return -1;
            if (queue.Count == 0) Refill();
            last = queue.Dequeue();
            return last;
        }

        private void Refill()
        {
            var items = new List<int>(count);
            for (var i = 0; i < count; i++) items.Add(i);
            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
            if (count > 1 && items[0] == last)
                (items[0], items[1]) = (items[1], items[0]);
            foreach (var i in items) queue.Enqueue(i);
        }
    }
}
