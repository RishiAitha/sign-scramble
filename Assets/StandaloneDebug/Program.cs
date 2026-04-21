using System;
using System.Diagnostics;
using SignScramble.StandaloneDebug;

#pragma warning disable CS8600, CS8602, CS8604, CS8618

BoardGenerator generator = new();

Console.WriteLine("=== Full Step-1 Generation Debug Run ===");
Console.WriteLine("GenerateBoards will choose candidate word sets and run training.");

Stopwatch stopwatch = Stopwatch.StartNew();
try
{
    var result = generator.GenerateBoards(3);
    stopwatch.Stop();
    string[] generatedBoards = result.Item1;
    bool[][][] displayedPerBoard = result.Item2;
    string[] words = generator.LastUsedWordSet;

    if (generatedBoards == null || generatedBoards.Length == 0)
    {
        Console.WriteLine("GenerateBoards returned no boards.");
    }
    else
    {
        Console.WriteLine($"GenerateBoards produced {generatedBoards.Length} board(s) in {stopwatch.ElapsedMilliseconds} ms.");
        Console.WriteLine();

        int validBoards = 0;

        // compute true target score (sum of word lengths)
        int targetScore = 0;
        foreach (string w in words) targetScore += w.Length;

            for (int i = 0; i < generatedBoards.Length; i++)
        {
            BoardGenerator.Board board = new(generatedBoards[i]);
            float score = generator.ScoreBoardOnWords(board, words);
            if (score >= targetScore)
            {
                validBoards++;
            }
            PrintBoard($"Board {i + 1}", board);
            // print revealed (masked) words using the displayed masks for this board
            bool[][] displayedMasksForThisBoard = null;
            if (displayedPerBoard != null && i >= 0 && i < displayedPerBoard.Length) displayedMasksForThisBoard = displayedPerBoard[i];

            // prepare safe default masks if data missing
            bool[][] defaultMasks = new bool[words.Length][];
            for (int wi = 0; wi < words.Length; wi++) defaultMasks[wi] = new bool[words[wi].Length];

            Console.WriteLine("Revealed words:");
            for (int wi = 0; wi < words.Length; wi++)
            {
                string w = words[wi];
                bool[] maskForWord = (displayedMasksForThisBoard != null && wi < displayedMasksForThisBoard.Length && displayedMasksForThisBoard[wi] != null)
                    ? displayedMasksForThisBoard[wi]
                    : defaultMasks[wi];

                char[] mask = new char[w.Length];
                for (int m = 0; m < w.Length; m++) mask[m] = (m < maskForWord.Length && maskForWord[m]) ? w[m] : '_';
                Console.WriteLine($"  {new string(mask)}");
            }

            PrintWordCoverage(generator, board, words);

            // Use displayed masks returned by GenerateBoards for this board (safely)
            bool[][] displayMasksForPrints = (displayedPerBoard != null && i >= 0 && i < displayedPerBoard.Length && displayedPerBoard[i] != null)
                ? displayedPerBoard[i]
                : defaultMasks;

            // convert bool[] masks to ushort bitmasks for the updated API
            ushort[] displayMasksForCalls = new ushort[words.Length];
            for (int wi = 0; wi < words.Length; wi++)
            {
                bool[] m = (displayMasksForPrints != null && wi < displayMasksForPrints.Length && displayMasksForPrints[wi] != null)
                    ? displayMasksForPrints[wi]
                    : defaultMasks[wi];
                ushort mask = 0;
                for (int k = 0; k < Math.Min(m.Length, 16); k++) if (m[k]) mask |= (ushort)(1 << k);
                displayMasksForCalls[wi] = mask;
            }

            float altScore = generator.ScoreBoardOnAlternatives(board, words, displayMasksForCalls.ToList());
            Console.WriteLine($"ScoreBoardOnAlternatives: {altScore}");
            for (int wi = 0; wi < words.Length; wi++)
            {
                int altCount = board.FindAlternateWordsFromPosition(words[wi], displayMasksForCalls[wi]);
                Console.WriteLine($"  Alternatives for {words[wi]}: {altCount}");
            }

            Console.WriteLine();
        }

        Console.WriteLine($"Valid boards (all words found): {validBoards}/{generatedBoards.Length}");
        Console.WriteLine();
        Console.WriteLine("Actual words:");
        foreach (string w in words) Console.WriteLine($"  {w}");
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
