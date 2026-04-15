using System;
using System.Diagnostics;
using SignScramble.StandaloneDebug;

string[] words = { "CLOCK", "DOOR", "ROD", "SIGN", "CROOKS" };

BoardGenerator generator = new(usingTest: true);

Console.WriteLine("=== Full Step-1 Generation Debug Run ===");
Console.WriteLine($"Word set valid: {generator.ValidateWordSet(words)}");
Console.WriteLine($"Target words: {string.Join(", ", words)}");
Console.WriteLine();

Stopwatch stopwatch = Stopwatch.StartNew();
try
{
    var result = generator.GenerateBoards(words, 10);
    stopwatch.Stop();

    string[] generatedBoards = result.Item1;
    Tuple<char, int>[][] displayedPerBoard = result.Item2;

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
            // print revealed (masked) words using the displayed pivot letters for this board
            Tuple<char, int>[] displayedLetters = displayedPerBoard[i];
            Console.WriteLine("Revealed words:");
            for (int wi = 0; wi < words.Length; wi++)
            {
                string w = words[wi];
                var disp = displayedLetters[wi];
                char pivot = disp.Item1;
                int pidx = disp.Item2;
                char[] mask = new char[w.Length];
                for (int m = 0; m < w.Length; m++) mask[m] = (m == pidx) ? pivot : '_';
                Console.WriteLine($"  {new string(mask)}");
            }

            PrintWordCoverage(generator, board, words);

            // Use displayed letters returned by GenerateBoards for this board
            Tuple<char, int>[] displayedLetters = displayedPerBoard[i];
            float altScore = generator.ScoreBoardOnAlternatives(board, words, displayedLetters.ToList());
            Console.WriteLine($"ScoreBoardOnAlternatives: {altScore}");
            for (int wi = 0; wi < words.Length; wi++)
            {
                int altCount = board.FindAlternateWordsFromPosition(words[wi], displayedLetters[wi]);
                Console.WriteLine($"  Alternatives for {words[wi]} (pivot {displayedLetters[wi].Item1}@{displayedLetters[wi].Item2}): {altCount}");
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
    Console.WriteLine($"ScoreBoardOnWords: {score}/{words.Length}");
    foreach (string word in words)
    {
        bool found = board.TryFindWordPath(word, out _);
        Console.WriteLine($"  {(found ? "[x]" : "[ ]")} {word}");
    }
}
