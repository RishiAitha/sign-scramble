using System;
using System.Diagnostics;
using System.Linq;
using SignScramble.StandaloneDebug;

#pragma warning disable CS8600, CS8602, CS8604, CS8618

BoardGenerator generator = new();

Console.WriteLine("=== Full Step-1 Generation Debug Run ===");
Console.WriteLine("GenerateBoards will choose candidate word sets and run training.");

Stopwatch stopwatch = Stopwatch.StartNew();
try
{
    var results = generator.GenerateBoards(5);
    stopwatch.Stop();

    if (results == null || results.Count == 0)
    {
        Console.WriteLine("GenerateBoards returned no boards.");
    }
    else
    {
        Console.WriteLine($"GenerateBoards produced {results.Count} board(s) in {stopwatch.ElapsedMilliseconds} ms.");
        Console.WriteLine();

        int validBoards = 0;

        for (int i = 0; i < results.Count; i++)
        {
            var gen = results[i];
            BoardGenerator.Board board = new(gen.BoardString);

            // compute true target score for active words
            int targetScore = 0;
            foreach (string w in gen.ActiveWords) targetScore += w.Length;

            float score = generator.ScoreBoardOnWords(board, gen.ActiveWords);
            if (score >= targetScore) validBoards++;

            PrintBoard($"Board {i + 1}", board);

            // print revealed (masked) words using the displayed masks for this board
            Console.WriteLine("Revealed words:");
            for (int wi = 0; wi < gen.ActiveWords.Length; wi++)
            {
                string w = gen.ActiveWords[wi];
                bool[] maskForWord = (gen.DisplayedMasks != null && wi < gen.DisplayedMasks.Length && gen.DisplayedMasks[wi] != null)
                    ? gen.DisplayedMasks[wi]
                    : new bool[w.Length];

                char[] mask = new char[w.Length];
                for (int m = 0; m < w.Length; m++) mask[m] = (m < maskForWord.Length && maskForWord[m]) ? w[m] : '_';
                Console.WriteLine($"  {new string(mask)} ({w})");
            }

            PrintWordCoverage(generator, board, gen.ActiveWords);

            // convert bool[] masks to ushort bitmasks for the updated API
            ushort[] displayMasksForCalls = new ushort[gen.ActiveWords.Length];
            for (int wi = 0; wi < gen.ActiveWords.Length; wi++)
            {
                bool[] m = (gen.DisplayedMasks != null && wi < gen.DisplayedMasks.Length && gen.DisplayedMasks[wi] != null)
                    ? gen.DisplayedMasks[wi]
                    : new bool[gen.ActiveWords[wi].Length];
                ushort mask = 0;
                for (int k = 0; k < Math.Min(m.Length, 16); k++) if (m[k]) mask |= (ushort)(1 << k);
                displayMasksForCalls[wi] = mask;
            }

            float altScore = generator.ScoreBoardOnAlternatives(board, gen.ActiveWords, displayMasksForCalls.ToList());
            Console.WriteLine($"ScoreBoardOnAlternatives: {altScore}");
            for (int wi = 0; wi < gen.ActiveWords.Length; wi++)
            {
                int altCount = board.FindAlternateWordsFromPosition(gen.ActiveWords[wi], displayMasksForCalls[wi]);
                Console.WriteLine($"  Alternatives for {gen.ActiveWords[wi]}: {altCount}");
            }

            Console.WriteLine();
        }

        Console.WriteLine($"Valid boards (all active words found): {validBoards}/{results.Count}");
        Console.WriteLine();
        Console.WriteLine("Generated boards and per-board active words:");
        foreach (var g in results)
        {
            Console.WriteLine($"  Board: {g.BoardString}");
            Console.WriteLine($"    ActiveWords: {string.Join(',', g.ActiveWords)}");
        }
    }
}
catch (Exception ex)
{
    stopwatch.Stop();
    Console.WriteLine($"GenerateBoards failed after {stopwatch.ElapsedMilliseconds} ms");
    Console.WriteLine(ex.Message);
}

static void PrintBoard(string title, BoardGenerator.Board board)
{
    Console.WriteLine($"{title}: {board}");
    char[,] arr = board.ToArray();
    for (int r = 0; r < 4; r++)
    {
        Console.Write("  ");
        for (int c = 0; c < 4; c++)
        {
            Console.Write(arr[r, c]);
            if (c < 3)
            {
                Console.Write(' ');
            }
        }
        Console.WriteLine();
    }
}

static void PrintWordCoverage(BoardGenerator generator, BoardGenerator.Board board, string[] words)
{
    float score = generator.ScoreBoardOnWords(board, words);
    int targetScore = 0;
    foreach (string w in words) targetScore += w.Length;
    int foundCount = 0;
    foreach (string word in words) if (board.TryFindWordPath(word, out _)) foundCount++;
    Console.WriteLine($"ScoreBoardOnWords (coverage): {score}/{targetScore}  (words found: {foundCount}/{words.Length})");
    foreach (string word in words)
    {
        bool found = board.TryFindWordPath(word, out _);
        Console.WriteLine($"  {(found ? "[x]" : "[ ]")} {word}");
    }
}
