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
        int totalBoards = 100;
        string outputJsonPath = "../Resources/generated_boards.json";
        string outputTxtPath = "../Resources/generated_boards.txt";
        int chunkSize = 10; // generate in smaller chunks so runs can be resumed or scheduled

        if (args.Length > 0 && int.TryParse(args[0], out var parsed)) totalBoards = parsed;
        if (args.Length > 1) outputJsonPath = args[1];
        if (args.Length > 2) outputTxtPath = args[2];
        if (args.Length > 3 && int.TryParse(args[3], out var parsedChunk)) chunkSize = parsedChunk;

        BoardGenerator generator = new();

        Console.WriteLine($"Generating {totalBoards} boards (chunk {chunkSize}) and writing to '{outputJsonPath}' (text summary: '{outputTxtPath}')");

        var allOutputs = LoadExistingOutputs(outputJsonPath);

        int done = allOutputs.Count;
        if (done >= totalBoards)
        {
            Console.WriteLine($"Already have {done} boards in {outputJsonPath}. Nothing to do.");
            return 0;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            while (done < totalBoards)
            {
                int toGenerate = Math.Min(chunkSize, totalBoards - done);
                Console.WriteLine($"Starting chunk: generate {toGenerate} boards (progress {done}/{totalBoards})");

                var results = generator.GenerateBoards(toGenerate);
                if (results == null || results.Count == 0)
                {
                    Console.WriteLine("GenerateBoards returned no boards for this chunk; aborting further attempts.");
                    break;
                }

                foreach (var gen in results)
                {
                    BoardGenerator.Board board = new(gen.BoardString);

                    int targetScore = 0;
                    foreach (string w in gen.ActiveWords) targetScore += w.Length;

                    float score = generator.ScoreBoardOnWords(board, gen.ActiveWords);

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

                    allOutputs.Add(new OutputBoard
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
                }

                done = allOutputs.Count;
                SaveOutputs(outputJsonPath, outputTxtPath, allOutputs, stopwatch.ElapsedMilliseconds);
                Console.WriteLine($"Chunk complete; saved progress. {done}/{totalBoards} boards total.");
            }

            stopwatch.Stop();
            Console.WriteLine($"Finished. Generated {allOutputs.Count} boards in {stopwatch.ElapsedMilliseconds} ms.");
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

    private static List<OutputBoard> LoadExistingOutputs(string jsonPath)
    {
        try
        {
            if (!File.Exists(jsonPath)) return new List<OutputBoard>();
            string text = File.ReadAllText(jsonPath);
            if (string.IsNullOrWhiteSpace(text)) return new List<OutputBoard>();
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var list = JsonSerializer.Deserialize<List<OutputBoard>>(text, opts);
            return list ?? new List<OutputBoard>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: failed to load existing outputs from {jsonPath}: {ex.Message}");
            return new List<OutputBoard>();
        }
    }

    private static void SaveOutputs(string jsonPath, string txtPath, List<OutputBoard> outputs, long elapsedMs)
    {
        try
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            string tmpJson = jsonPath + ".tmp";
            File.WriteAllText(tmpJson, JsonSerializer.Serialize(outputs, opts));
            File.Copy(tmpJson, jsonPath, true);
            File.Delete(tmpJson);

            var sb = new StringBuilder();
            sb.AppendLine($"Generated {outputs.Count} boards in {elapsedMs} ms.");

            int validBoards = 0;
            foreach (var o in outputs)
            {
                sb.AppendLine($"Board: {o.BoardString}");
                sb.AppendLine($"  ActiveWords: {string.Join(',', o.ActiveWords ?? Array.Empty<string>())}");
                sb.AppendLine($"  DisplayedMasks: {string.Join(',', o.DisplayedMasks ?? Array.Empty<int>())}");
                sb.AppendLine($"  MaskedWords: {string.Join(", ", o.DisplayedMasksString ?? Array.Empty<string>())}");
                sb.AppendLine($"  AltScore: {o.AltScore}, AltCounts: {string.Join(',', o.AltCounts ?? Array.Empty<int>())}");
                sb.AppendLine($"  Coverage: {o.CoverageScore}/{o.TargetScore}");
                sb.AppendLine();
                if (o.CoverageScore >= o.TargetScore) validBoards++;
            }

            sb.AppendLine($"Valid boards (all active words found): {validBoards}/{outputs.Count}");
            File.WriteAllText(txtPath, sb.ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: failed to save outputs: {ex.Message}");
        }
    }
}
