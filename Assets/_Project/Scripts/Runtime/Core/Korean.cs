namespace MurderMystery.Core
{
    public static class Korean
    {
        /// <summary>Appends the particle that fits the last syllable: withFinal after a final consonant (받침).</summary>
        public static string WithParticle(string word, string withFinal, string withoutFinal)
        {
            if (string.IsNullOrEmpty(word)) return word;
            var last = word[word.Length - 1];
            var hasFinal = last >= '\uAC00' && last <= '\uD7A3' && (last - 0xAC00) % 28 != 0;
            return word + (hasFinal ? withFinal : withoutFinal);
        }
    }
}
