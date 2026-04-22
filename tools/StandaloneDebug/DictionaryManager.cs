using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

#nullable enable

namespace SignScramble.StandaloneDebug
{
    public class DictionaryManager
    {
        private HashSet<string> validWords = new HashSet<string>();

        public DictionaryManager(string dictionaryPath = "dictionary.txt")
        {
            LoadDictionary(dictionaryPath);
        }

        private void LoadDictionary(string dictionaryPath)
        {
            string? pathToUse = null;
            if (File.Exists(dictionaryPath))
            {
                pathToUse = dictionaryPath;
            }
            else
            {
                // try common relative locations (from StandaloneDebug)
                string alt1 = Path.Combine("..", "Resources", Path.GetFileName(dictionaryPath));
                string alt2 = Path.Combine("..", "..", "Assets", "Resources", Path.GetFileName(dictionaryPath));
                if (File.Exists(alt1)) pathToUse = alt1;
                else if (File.Exists(alt2)) pathToUse = alt2;
            }

            if (pathToUse == null)
            {
                Console.Error.WriteLine($"Warning: Dictionary file not found (tried '{dictionaryPath}'). Continuing with empty dictionary.");
                return;
            }

            foreach (var line in File.ReadLines(pathToUse))
            {
                var word = line.Trim().ToUpperInvariant();
                if (!string.IsNullOrEmpty(word))
                {
                    validWords.Add(word);
                }
            }
        }

        public bool IsValidWord(string word)
        {
            return validWords.Contains(word.ToUpperInvariant());
        }
    }
}
