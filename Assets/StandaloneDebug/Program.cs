using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using SignScramble.StandaloneDebug;

#pragma warning disable CS8600, CS8602, CS8604, CS8618

// Output DTO for serialization
class OutputBoard
{
    public string BoardString { get; set; }
    public string[] GridRows { get; set; }
    public string[] ActiveWords { get; set; }
    public int[] DisplayedMasks { get; set; }
    public string[] DisplayedMasksString { get; set; }
    public float AltScore { get; set; }
    public int[] AltCounts { get; set; }
    public float CoverageScore { get; set; }
    public int TargetScore { get; set; }
}

class Program
{
    static int Main(string[] args)
    {
        int numberOfBoards = 100;
        string outputJsonPath = "../Resources/generated_boards.json";
        string outputTxtPath = "../Resources/generated_boards.txt";

        if (args.Length > 0 && int.TryParse(args[0], out var parsed)) numberOfBoards = parsed;
        if (args.Length > 1) outputJsonPath = args[1];
        if (args.Length > 2) outputTxtPath = args[2];

        BoardGenerator generator = new();

        Console.WriteLine($"Generating {numberOfBoards} boards and writing to '{outputJsonPath}' (text summary: '{outputTxtPath}')");

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            var results = generator.GenerateBoards(numberOfBoards);
            stopwatch.Stop();

            if (results == null || results.Count == 0)
            {
                Console.WriteLine("GenerateBoards returned no boards.");
                return 1;
            }

            Console.WriteLine($"GenerateBoards produced {results.Count} board(s) in {stopwatch.ElapsedMilliseconds} ms.");
            Console.WriteLine();

            List<OutputBoard> outputs = new List<OutputBoard>(results.Count);
            var sb = new StringBuilder();
            sb.AppendLine($"Generated {results.Count} boards in {stopwatch.ElapsedMilliseconds} ms.");

            int validBoards = 0;

            for (int i = 0; i < results.Count; i++)
            {
                var gen = results[i];
                BoardGenerator.Board board = new(gen.BoardString);

                int targetScore = 0;
                foreach (string w in gen.ActiveWords) targetScore += w.Length;

                float score = generator.ScoreBoardOnWords(board, gen.ActiveWords);
                if (score >= targetScore) validBoards++;

                // convert bool[] masks to ushort bitmasks for the updated API and build masked strings
                ushort[] displayMasksForCalls = new ushort[Math.Max(0, gen.ActiveWords.Length)];
                string[] maskStrings = new string[Math.Max(0, gen.ActiveWords.Length)];
                int[] altCounts = new int[Math.Max(0, gen.ActiveWords.Length)];

                for (int wi = 0; wi < gen.ActiveWords.Length; wi++)
                {
                    bool[] m = (gen.DisplayedMasks != null && wi < gen.DisplayedMasks.Length && gen.DisplayedMasks[wi] != null)
                        ? gen.DisplayedMasks[wi]
                        : new bool[gen.ActiveWords[wi].Length];
                    ushort mask = 0;
                    for (int k = 0; k < Math.Min(m.Length, 16); k++) if (m[k]) mask |= (ushort)(1 << k);
                    displayMasksForCalls[wi] = mask;

                    string w = gen.ActiveWords[wi];
                    char[] masked = new char[w.Length];
                    for (int mindex = 0; mindex < w.Length; mindex++) masked[mindex] = (mindex < m.Length && m[mindex]) ? w[mindex] : '_';
                    maskStrings[wi] = new string(masked);

                    altCounts[wi] = board.FindAlternateWordsFromPosition(gen.ActiveWords[wi], displayMasksForCalls[wi]);
                }

                float altScore = generator.ScoreBoardOnAlternatives(board, gen.ActiveWords, displayMasksForCalls.ToList());

                outputs.Add(new OutputBoard
                {
                    BoardString = gen.BoardString,
                    GridRows = BoardToRows(board),
                    ActiveWords = gen.ActiveWords,
                    DisplayedMasks = displayMasksForCalls.Select(x => (int)x).ToArray(),
                    DisplayedMasksString = maskStrings,
                    AltScore = altScore,
                    AltCounts = altCounts,
                    CoverageScore = score,
                    TargetScore = targetScore
                });

                sb.AppendLine($"Board {i + 1}: {gen.BoardString}");
                sb.AppendLine($"  ActiveWords: {string.Join(',', gen.ActiveWords)}");
                sb.AppendLine($"  DisplayedMasks: {string.Join(',', outputs.Last().DisplayedMasks)}");
                sb.AppendLine($"  MaskedWords: {string.Join(", ", outputs.Last().DisplayedMasksString)}");
                sb.AppendLine($"  AltScore: {altScore}, AltCounts: {string.Join(',', altCounts)}");
                sb.AppendLine($"  Coverage: {score}/{targetScore}");
                sb.AppendLine();
            }

            sb.AppendLine($"Valid boards (all active words found): {validBoards}/{results.Count}");

            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(outputJsonPath, JsonSerializer.Serialize(outputs, opts));
            File.WriteAllText(outputTxtPath, sb.ToString());

            Console.WriteLine($"Wrote JSON to {outputJsonPath}");
            Console.WriteLine($"Wrote text summary to {outputTxtPath}");
            return 0;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Console.WriteLine($"GenerateBoards failed after {stopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string[] BoardToRows(BoardGenerator.Board board)
    {
        char[,] arr = board.ToArray();
        string[] rows = new string[4];
        for (int r = 0; r < 4; r++)
        {
            char[] row = new char[4];
            for (int c = 0; c < 4; c++) row[c] = arr[r, c];
            rows[r] = new string(row);
        }
        return rows;
    }
}
