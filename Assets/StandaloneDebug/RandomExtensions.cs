using System;

namespace SignScramble.StandaloneDebug
{
    public static class RandomExtensions
    {
        // Fisher-Yates shuffle
        public static void Shuffle<T>(this Random rng, T[] array)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (array == null) return;
            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T tmp = array[i];
                array[i] = array[j];
                array[j] = tmp;
            }
        }
    }
}
