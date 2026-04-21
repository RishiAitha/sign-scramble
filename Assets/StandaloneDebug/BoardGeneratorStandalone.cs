using System;
using System.Collections.Generic;
using System.Numerics;
using System.IO;
using System.Linq;

namespace SignScramble.StandaloneDebug;

/*
    This script generates a 4x4 board of letters containing words.
*/

public class BoardGenerator
{
    private readonly object? letterPrefab;

    private Board currentBoard;
    private readonly Letter[,] letterObjects; // Store references to Letter components
    private bool isInitialized;
    private static readonly Random Random = new();

    public BoardGenerator(object? letterPrefab = null)
    {
        this.letterPrefab = letterPrefab;
        currentBoard = new Board();
        letterObjects = new Letter[4, 4];
        InitializeBoardObjects();
    }

    public void Awake()
    {
        InitializeBoardObjects();
    }

    public void Start()
    {
        // Let GenerateBoards choose word sets itself and drive the full process.
        GenerateBoards(10);
    }

    // Load a word list and pick `count` words with decent Jaccard similarity.
    public string[] GetRandomSimilarWordSet(int count = 5)
    {
        const string fileName = "fingerspellingwords.txt";

        string[] allWords = File.ReadAllLines("../Resources/fingerspellingwords.txt")
                    .Select(w => w?.Trim().ToUpperInvariant())
                    .Where(w => !string.IsNullOrEmpty(w))
                    .Where(w => w.Length <= 6)
                    .ToArray();

        var candidates = allWords
            .Select(w => new string(w.Where(char.IsLetter).ToArray()))
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length <= count) return candidates.Take(count).ToArray();

        static double Jaccard(string a, string b)
        {
            var sa = new HashSet<char>(a.ToUpperInvariant());
            var sb = new HashSet<char>(b.ToUpperInvariant());
            var inter = sa.Intersect(sb).Count();
            var uni = sa.Union(sb).Count();
            return uni == 0 ? 0.0 : (double)inter / uni;
        }

        string[] bestSet = candidates.Take(count).ToArray();
        double bestScore = -1.0;
        double targetMin = 0.18;
        double targetMax = 0.55;

        int attempts = 2000;
        for (int a = 0; a < attempts; a++)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (set.Count < count)
            {
                set.Add(candidates[Random.Next(candidates.Length)]);
            }
            var arr = set.ToArray();

            double sum = 0; int pairs = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                for (int j = i + 1; j < arr.Length; j++)
                {
                    sum += Jaccard(arr[i], arr[j]);
                    pairs++;
                }
            }
            double avg = pairs > 0 ? sum / pairs : 0.0;

            if (avg >= targetMin && avg <= targetMax)
            {
                return arr.Select(s => s.ToUpperInvariant()).ToArray();
            }

            double mid = (targetMin + targetMax) / 2.0;
            double score = -Math.Abs(avg - mid);
            if (score > bestScore)
            {
                bestScore = score;
                bestSet = arr.Select(s => s.ToUpperInvariant()).ToArray();
            }
        }

        return bestSet;
    }

    public string[] LastUsedWordSet { get; private set; } = Array.Empty<string>();

    public Tuple<string[], bool[][][]> GenerateBoards(int numberOfBoards)
    {
        // Try multiple candidate word sets until we find one that yields `numberOfBoards`
        int wordSetAttempts = 0;
        while (true)
        {
            wordSetAttempts++;
            string[] words = GetRandomSimilarWordSet(6);
            if (!ValidateWordSet(words))
            {
                Console.WriteLine($"[BoardGenerator] Skipping invalid word set: {string.Join(", ", words)}");
                continue;
            }

            Console.WriteLine($"[BoardGenerator] Trying word set #{wordSetAttempts}: {string.Join(", ", words)}");

            Stack<Board> completeBoards = new Stack<Board>();

            string letterSet = "";
            foreach (string word in words)
            {
                letterSet += word.ToUpperInvariant();
            }

            letterSet += "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            char[] shuffledLetterSet = letterSet.ToCharArray();
            Random.Shuffle(shuffledLetterSet);
            letterSet = new string(shuffledLetterSet);

            int training1Limit = Math.Max(500, 400 * words.Length);

            while (completeBoards.Count < numberOfBoards && training1Limit > 0)
            {
                Board newBoard = new Board(letterSet.ToCharArray());
                Board? trainedBoard = TrainingStep1(newBoard, words);
                if (trainedBoard != null)
                {
                    completeBoards.Push(trainedBoard);
                    Console.WriteLine($"[BoardGenerator] TrainingStep1 produced board #{completeBoards.Count} for words {string.Join(", ", words)}: {trainedBoard}");
                }

                training1Limit -= 1;
            }

            if (completeBoards.Count < numberOfBoards)
            {
                Console.WriteLine($"[BoardGenerator] Word set failed to produce {numberOfBoards} boards; trying next word set.");
                continue;
            }

            // For each word create a bitmask (per-index) indicating which letters are displayed.
            List<ushort> displayedPositionsPerWord = new List<ushort>(words.Length);
            for (int i = 0; i < words.Length; i++)
            {
                int rarestLetterIndex = Board.FindKthRarestLetter(words[i], 1);
                ushort mask = 0;
                if (words[i].Length > 0 && rarestLetterIndex >= 0 && rarestLetterIndex < words[i].Length) mask = (ushort)(1 << rarestLetterIndex);
                displayedPositionsPerWord.Add(mask);
            }

            List<Board> optimizedBoards = new List<Board>(numberOfBoards);
            List<ushort[]> perBoardDisplayedMasks = new List<ushort[]>(numberOfBoards);

            while (completeBoards.Count > 0)
            {
                var b = completeBoards.Pop();
                ushort protectedFromStep1 = GetProtectedMaskFromBestPaths(b, words);
                var result = TrainingStep2(b, words, displayedPositionsPerWord, protectedFromStep1);
                if (result != null && result.Item1 != null)
                {
                    optimizedBoards.Add(result.Item1);
                    perBoardDisplayedMasks.Add(result.Item2);
                    Console.WriteLine($"[BoardGenerator] TrainingStep2 completed for board: {result.Item1}");
                    Console.WriteLine($"[BoardGenerator] Final displayed masks: {FormatMasks(result.Item2.ToList(), words)}");
                }
            }

            // sort boards by increasing number of revealed letters (fewer reveals = better)
            var paired = optimizedBoards.Select((b, i) => new {
                board = b,
                masks = perBoardDisplayedMasks[i],
                revealed = perBoardDisplayedMasks[i].Sum(m => System.Numerics.BitOperations.PopCount((uint)m))
            }).OrderBy(p => p.revealed).ToList();

            string[] boardStrings = paired.Select(p => p.board.ToString()).ToArray();
            bool[][][] perBoardDisplayed = paired.Select(p => p.masks.Select((m, wi) => {
                int len = words[wi].Length;
                bool[] arr = new bool[len];
                for (int k = 0; k < len; k++) arr[k] = ((m >> k) & 1) != 0;
                return arr;
            }).ToArray()).ToArray();

            // remember which word set produced these boards
            LastUsedWordSet = words;

            return Tuple.Create(boardStrings, perBoardDisplayed);
        }
    }

    public Board? TrainingStep1(Board board, string[] words)
    {
        int limit = 2000;
        int initialLimit = limit;
        int lastLogIter = -1;
        Console.WriteLine($"[BoardGenerator] TrainingStep1 start targetScore={words.Sum(w=>w.Length)} initialBoard={board} initialLimit={limit}");
        float previousScore = float.MinValue;
        int previousFound = 0;
        Board previousBoard = new Board(board.ToString());
        Board currentBoard = new Board(board.ToString());
        // start with protection discovered from previousBoard's complete paths (bitmask)
        ushort cumulativeProtected = GetProtectedMaskFromBestPaths(previousBoard, words);
        bool[] previousFoundFlags = new bool[words.Length];
        for (int i = 0; i < words.Length; i++) previousFoundFlags[i] = previousBoard.TryFindWordPath(words[i], out _);
        previousScore = ScoreBoardOnWordsAndProtect(previousBoard, words, ref cumulativeProtected, previousFoundFlags);
        previousFound = previousFoundFlags.Count(f => f);
        int targetScore = 0;
        foreach (string word in words) {
            targetScore += word.Length;
        }

        while (limit > 0)
        {
            // score and collect protected cells for current board in a single pass
            bool[] currentFoundFlags = new bool[words.Length];
            ushort tempProtected = 0;
            float currentScore = ScoreBoardOnWordsAndProtect(currentBoard, words, ref tempProtected, currentFoundFlags);
            int currentFound = currentFoundFlags.Count(f => f);

            int iter = initialLimit - limit;
            if (iter - lastLogIter >= 250 || currentFound > previousFound)
            {
                Console.WriteLine($"[BoardGenerator] TrainingStep1 iter={iter} coverage={currentScore}/{targetScore} found={currentFound}/{words.Length} bestFound={previousFound} bestScore={previousScore} board={currentBoard}");
                lastLogIter = iter;
            }

            if (currentFound == words.Length)
            {
                Console.WriteLine($"[BoardGenerator] TrainingStep1 success coverage={currentScore}/{targetScore} found={currentFound}/{words.Length} board={currentBoard}");
                return currentBoard;
            }

            Board boardToMutate;

            if (currentFound < previousFound || (currentFound == previousFound && (currentScore < previousScore || (currentScore == previousScore && Random.Next(2) == 0))))
            {
                boardToMutate = new Board(previousBoard.ToString());
            }
            else
            {
                boardToMutate = new Board(currentBoard.ToString());
                previousBoard = currentBoard;
                previousScore = currentScore;
                previousFound = currentFound;
                // union protected cells from newly accepted best (best paths)
                cumulativeProtected |= tempProtected;
            }

            // mutate while avoiding any cumulatively protected cells
            currentBoard = boardToMutate.Mutate(cumulativeProtected);

            limit -= 1;
        }

        Console.WriteLine($"[BoardGenerator] TrainingStep1 timed out. bestFound={previousFound} bestScore={previousScore} bestBoard={previousBoard}");
        return null;
    }

    public Tuple<Board, ushort[]>? TrainingStep2(Board board, string[] words, List<ushort> displayedLetters, ushort? protectedFromStep1 = null)
    {
        int initialPerDisplaySetLimit = Math.Max(100, 150 * words.Length);
        int perDisplaySetLimit = initialPerDisplaySetLimit;
        int lastLogIter = -1;
        float previousScore = float.MaxValue;
        Board previousBoard = new Board(board.ToString());
        Board currentBoard = new Board(board.ToString());
        float targetScore = 0;
        // ensure we preserve word coverage: compute target word coverage score (sum of word lengths)
        int wordTargetScore = 0;
        foreach (string w in words) wordTargetScore += w.Length;
        
        // clone the displayed-positions masks so we can modify locally if needed
        List<ushort> currentDisplayedLetters = displayedLetters.Select(mask => mask).ToList();

        Console.WriteLine($"[BoardGenerator] TrainingStep2 start board={board} initialMasks={FormatMasks(currentDisplayedLetters, words)} initialLimit={initialPerDisplaySetLimit}");

        int currentWordToDisplayMore = 0;

        // simple strict loop: compute alternatives only, require zero alternatives for success
        while (perDisplaySetLimit > 0)
        {
            float currentScore = ScoreBoardOnAlternatives(currentBoard, words, currentDisplayedLetters);
            int iter = initialPerDisplaySetLimit - perDisplaySetLimit;
            if (iter - lastLogIter >= 50 || currentScore < previousScore)
            {
                Console.WriteLine($"[BoardGenerator] TrainingStep2 iter={iter} currentScore={currentScore} prevScore={previousScore} perDisplayLimit={perDisplaySetLimit} masks={FormatMasks(currentDisplayedLetters, words)}");
                lastLogIter = iter;
            }
            if (currentScore <= targetScore)
            {
                // return the board plus the final displayed masks (ushort per-word masks)
                ushort[] finalMasks = currentDisplayedLetters.ToArray();
                Console.WriteLine($"[BoardGenerator] TrainingStep2 success finalScore={currentScore} masks={FormatMasks(currentDisplayedLetters, words)}");
                return Tuple.Create(currentBoard, finalMasks);
            }

            Board boardToMutate;

            if (currentScore > previousScore || (currentScore == previousScore && Random.Next(2) == 0))
            {
                boardToMutate = new Board(previousBoard.ToString());
            }
            else
            {
                boardToMutate = new Board(currentBoard.ToString());
                previousBoard = currentBoard;
                previousScore = currentScore;
                // note: do not modify `protectedFromStep1` here (static filter only).
            }
            // mutate while avoiding statically protected cells from step1
            Board mutated = new Board(boardToMutate.ToString()).Mutate(protectedFromStep1);
            // verify mutated preserves coverage (all words found)
            if (ScoreBoardOnWords(mutated, words) >= wordTargetScore)
            {
                currentBoard = mutated;
            }
            else
            {
                currentBoard = boardToMutate;
            }

            perDisplaySetLimit -= 1;
            if (perDisplaySetLimit <= 0)
            {
                // reveal additional letters using lightweight randomized trials.
                // Try candidates in random order, accept immediately on improvement,
                // otherwise pick the best candidate after a few trials.
                bool displayedAll = true;
                for (int wi = 0; wi < currentDisplayedLetters.Count; wi++)
                {
                    int shown = System.Numerics.BitOperations.PopCount(currentDisplayedLetters[wi]);
                    if (shown < words[wi].Length)
                    {
                        displayedAll = false;
                        break;
                    }
                }

                if (displayedAll)
                {
                    throw new Exception("Training step 2 timed out after all letters displayed.");
                }

                List<Tuple<int, int>> eligible = new List<Tuple<int, int>>();
                for (int wi = 0; wi < words.Length; wi++)
                {
                    int displayedCount = System.Numerics.BitOperations.PopCount((uint)currentDisplayedLetters[wi]);
                    if (displayedCount < words[wi].Length)
                    {
                        int nextK = displayedCount + 1;
                        int revealIdx = Board.FindKthRarestLetter(words[wi], nextK);
                        if (revealIdx >= 0 && revealIdx < words[wi].Length)
                        {
                            eligible.Add(Tuple.Create(wi, revealIdx));
                        }
                    }
                }

                if (eligible.Count == 0)
                {
                    perDisplaySetLimit = initialPerDisplaySetLimit;
                    throw new Exception("Training step 2 failed.");
                }

                float currentScoreBeforeReveal = ScoreBoardOnAlternatives(currentBoard, words, currentDisplayedLetters);

                // shuffle eligible reveals and try them sequentially (early accept)
                var rndOrder = eligible.OrderBy(_ => Random.Next()).ToList();
                int trials = Math.Min(6, rndOrder.Count);
                List<ushort> bestMasks = null;
                float bestScore = float.MaxValue;
                Tuple<int,int> bestPair = null;
                bool accepted = false;

                for (int t = 0; t < trials; t++)
                {
                    var pair = rndOrder[t];
                    int wi = pair.Item1;
                    int revealIdx = pair.Item2;

                    List<ushort> tempMasks = currentDisplayedLetters.Select(m => m).ToList();
                    tempMasks[wi] = (ushort)(tempMasks[wi] | (1 << revealIdx));
                    float postScore = ScoreBoardOnAlternatives(currentBoard, words, tempMasks);

                    if (postScore < currentScoreBeforeReveal)
                    {
                        currentDisplayedLetters = tempMasks;
                        accepted = true;
                        Console.WriteLine($"[BoardGenerator] Accepted reveal (improved) for word #{wi} at index {revealIdx}; masks now: {FormatMasks(currentDisplayedLetters, words)} postScore={postScore}");
                        break;
                    }

                    if (postScore < bestScore)
                    {
                        bestScore = postScore;
                        bestMasks = tempMasks;
                        bestPair = pair;
                    }
                }

                if (!accepted && bestMasks != null)
                {
                    // no immediate improvement found; accept the best candidate to make progress
                    currentDisplayedLetters = bestMasks;
                    Console.WriteLine($"[BoardGenerator] No immediate improvement; choosing best candidate for word #{bestPair.Item1} idx {bestPair.Item2} with postScore={bestScore}");
                }

                perDisplaySetLimit = initialPerDisplaySetLimit;
            }
        }

        return null;
    }

    public float ScoreBoardOnWords(Board board, string[] words)
    {
        int score = 0;
    
        foreach (string word in words)
        {
            List<Tuple<int, int>> path = board.FindBestWordPath(word);
            if (path != null && path.Count > 0)
            {
                score += Math.Min(path.Count, word.Length);
            }
        }

        return score;
    }

    public float ScoreBoardOnAlternatives(Board board, string[] words, List<ushort> displayed)
    {
        int score = 0;
        for (int i = 0; i < words.Length; i++)
        {
            int alternatives = board.FindAlternateWordsFromPosition(words[i], displayed[i]);
            score += alternatives;
        }

        return score;
    }

    public bool ValidateWordSet(string[] words)
    {
        int uniqueLetters = 0;
        HashSet<char> chars = new();
        foreach (string word in words)
        {
            foreach (char letter in word)
            {
                if (!chars.Contains(letter))
                {
                    chars.Add(letter);
                    uniqueLetters++;
                }
            }
        }
        return uniqueLetters <= 16;
    }

    public string GenerateRandomWeightedBoardString()
    {
        return Board.GenerateRandomWeightedBoardString();
    }

    public Board GenerateRandomWeightedBoard()
    {
        return Board.GenerateRandomWeightedBoard();
    }

    public Board GetBoard()
    {
        if (currentBoard == null)
        {
            currentBoard = new Board();
        }
        return currentBoard;
    }

    public char[,] GetBoardArray()
    {
        return GetBoard().ToArray();
    }

    public void SetBoard(Board board)
    {
        if (board == null) return;
        InitializeBoardObjects();

        currentBoard = new Board(board.ToString());

        for (int i = 0; i < 16; i++)
        {
            int row = i / 4;
            int col = i % 4;
            char c = currentBoard.get(row, col);
            letterObjects[row, col].changeLetter(c);
        }
    }

    public void SetBoardFromString(string board)
    {
        if (string.IsNullOrEmpty(board)) return;
        SetBoard(new Board(board));
    }

    private void InitializeBoardObjects()
    {
        if (isInitialized)
        {
            for (int i = 0; i < 16; i++)
            {
                int row = i / 4;
                int col = i % 4;
                foreach (Arrow arrow in letterObjects[row, col].arrows)
                {
                    arrow.SetActive(false);
                }
            }
        }
        else
        {
            currentBoard = new Board();

            for (int i = 0; i < 16; i++)
            {
                int row = i / 4;
                int col = i % 4;

                Letter letterObject = new Letter();
                letterObjects[row, col] = letterObject;
                letterObjects[row, col].changeLetter(currentBoard.get(row, col));
                foreach (Arrow arrow in letterObjects[row, col].arrows)
                {
                    arrow.SetActive(false);
                }
            }

            isInitialized = true;
        }
    }

    public Letter? GetLetterAt(int row, int col)
    {
        if (row >= 0 && row < 4 && col >= 0 && col < 4)
        {
            return letterObjects[row, col];
        }
        return null;
    }

    // Helper: returns the list of characters from `word` that are marked as displayed by `displayedMask`.
    public static List<char> GetDisplayedCharactersFromMask(string word, ushort displayedMask)
    {
        List<char> displayed = new List<char>();
        if (string.IsNullOrEmpty(word)) return displayed;
        int len = Math.Min(word.Length, 16);
        for (int i = 0; i < len; i++)
        {
            if (((displayedMask >> i) & 1) != 0) displayed.Add(word[i]);
        }
        return displayed;
    }

    // Helper: format boolean masks for logging
    private static string FormatMasks(List<bool[]> masks)
    {
        if (masks == null || masks.Count == 0) return "[]";
        return string.Join(" | ", masks.Select(m => new string(m.Select(b => b ? '1' : '0').ToArray())));
    }

    // Helper: format ushort bitmasks for logging given the corresponding words
    private static string FormatMasks(List<ushort> masks, string[] words)
    {
        if (masks == null || masks.Count == 0) return "[]";
        return string.Join(" | ", masks.Select((m, i) => {
            int len = (i < words.Length) ? words[i].Length : 16;
            char[] chars = new char[len];
            for (int k = 0; k < len; k++) chars[k] = (((m >> k) & 1) != 0) ? '1' : '0';
            return new string(chars);
        }));
    }

    // Collect cells used by the complete paths for each word (used after TrainingStep1)
    // Return a 16-bit mask where bit (r*4 + c) is set for protected cells.
    private static ushort GetProtectedMaskFromBestPaths(Board board, string[] words)
    {
        ushort prot = 0;
        if (board == null || words == null) return prot;
        foreach (string w in words)
        {
            if (board.TryFindWordPath(w, out var path) && path != null)
            {
                foreach (var t in path)
                {
                    int r = t.Item1, c = t.Item2;
                    if (r >= 0 && r < 4 && c >= 0 && c < 4) prot |= (ushort)(1 << (r * 4 + c));
                }
            }
        }
        return prot;
    }

    private static void AddPathToProtected(ref ushort prot, List<Tuple<int,int>> path)
    {
        if (path == null) return;
        foreach (var t in path)
        {
            int r = t.Item1, c = t.Item2;
            if (r >= 0 && r < 4 && c >= 0 && c < 4) prot |= (ushort)(1 << (r * 4 + c));
        }
    }

    // Score words while also marking protected cells (bitmask) for successfully found complete paths.
    private static float ScoreBoardOnWordsAndProtect(Board board, string[] words, ref ushort protectedCells, bool[]? foundFlags)
    {
        int score = 0;
        if (board == null || words == null) return score;
        for (int i = 0; i < words.Length; i++)
        {
            if (foundFlags != null && foundFlags[i])
            {
                score += words[i].Length;
                continue;
            }

            var bestPath = board.FindBestWordPath(words[i]);
            if (bestPath != null && bestPath.Count > 0)
            {
                if (bestPath.Count == words[i].Length)
                {
                    if (foundFlags != null) foundFlags[i] = true;
                    AddPathToProtected(ref protectedCells, bestPath);
                }
                score += Math.Min(bestPath.Count, words[i].Length);
            }
        }

        return score;
    }

    public class Board
    {
        private readonly char[,] boardArray;
        private static readonly int[] LetterCounts = { 118838, 28836, 64177, 53136, 180794, 19335, 42424, 36493, 141463, 2497, 13309, 83427, 44529, 107519, 103536, 46141, 2535, 111061, 149453, 104965, 51299, 15363, 11689, 4610, 25637, 7442 };
        private static readonly int TotalLetterCount = LetterCounts.Sum();
        public static readonly float[] Weights = LetterCounts.Select(count => count / (float)TotalLetterCount).ToArray();

        // precomputed neighbor indices for each linear cell
        private static readonly int[][] NeighborIndices;

        static Board()
        {
            NeighborIndices = new int[16][];
            for (int idx = 0; idx < 16; idx++)
            {
                int r = idx / 4;
                int c = idx % 4;
                List<int> neigh = new List<int>();
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (dr == 0 && dc == 0) continue;
                        int nr = r + dr;
                        int nc = c + dc;
                        if (nr >= 0 && nr < 4 && nc >= 0 && nc < 4)
                        {
                            neigh.Add(nr * 4 + nc);
                        }
                    }
                }
                NeighborIndices[idx] = neigh.ToArray();
            }
        }

        public static string GenerateRandomWeightedBoardString()
        {
            char[] letters = new char[16];
            for (int i = 0; i < 16; i++)
            {
                letters[i] = GetRandomWeightedLetter();
            }
            return new string(letters);
        }

        public static Board GenerateRandomWeightedBoard()
        {
            return new Board(GenerateRandomWeightedBoardString());
        }

        private static char GetRandomWeightedLetter()
        {
            float roll = (float)Random.NextDouble();
            float runningTotal = 0f;

            for (int i = 0; i < Weights.Length; i++)
            {
                runningTotal += Weights[i];
                if (roll <= runningTotal)
                {
                    return (char)('A' + i);
                }
            }

            return 'Z';
        }

        private static readonly SignScramble.StandaloneDebug.DictionaryManager dictionaryManager =
            new SignScramble.StandaloneDebug.DictionaryManager("dictionary.txt");

        public int FindAlternateWordsFromPosition(string word, ushort displayedPositions)
        {
            if (string.IsNullOrEmpty(word))
            {
                throw new Exception("Illegal argument in find alternate words from position.");
            }
            List<char> displayedChars = BoardGenerator.GetDisplayedCharactersFromMask(word, displayedPositions);
            if (displayedChars.Count == 0)
            {
                // nothing displayed -> no anchored alternates
                return 0;
            }

            int alternateWordCount = 0;
            HashSet<string> seenWords = new(StringComparer.OrdinalIgnoreCase);

            // collect displayed indices for verification at completion
            List<int> displayedIndices = new List<int>();
            for (int k = 0; k < Math.Min(word.Length, 16); k++) if (((displayedPositions >> k) & 1) != 0) displayedIndices.Add(k);

            // leftlength, rightlength, nextLeftIndex, nextRightIndex, pivotPos, usedMask, current path (linear indices)
            // use LinkedList<int> as a deque to avoid O(n) inserts at the head
            Queue<Tuple<int, int, int, int, int, int, LinkedList<int>>> queue = new();

            // choose a single seed displayed index to start BFS from: pick the rarest displayed letter
            int seedDisplayIndex = displayedIndices[0];
            if (displayedIndices.Count > 1)
            {
                // find the kth-rarest letter in the word that is also one of the displayed indices
                for (int k = 1; k <= word.Length; k++)
                {
                    int candidateIdx = FindKthRarestLetter(word, k);
                    if (displayedIndices.Contains(candidateIdx))
                    {
                        seedDisplayIndex = candidateIdx;
                        break;
                    }
                }
            }

            for (int linear = 0; linear < 16; linear++)
            {
                int rr = linear / 4, rc = linear % 4;
                if (boardArray[rr, rc] == word[seedDisplayIndex])
                {
                    int usedMask = 1 << linear;
                    LinkedList<int> candidatePath = new LinkedList<int>(new[] { linear });
                    int nextLeftIndex = seedDisplayIndex - 1;
                    int nextRightIndex = seedDisplayIndex + 1;
                    int leftLength = seedDisplayIndex;
                    int rightLength = word.Length - seedDisplayIndex - 1;
                    int pivotPos = 0; // pivot is at index 0 in candidatePath initially
                    queue.Enqueue(Tuple.Create(leftLength, rightLength, nextLeftIndex, nextRightIndex, pivotPos, usedMask, candidatePath));
                }
            }

            while (queue.Count != 0)
            {
                var tuple = queue.Dequeue();

                int curLeft = tuple.Item1;
                int curRight = tuple.Item2;
                int nextLeftIndex = tuple.Item3;
                int nextRightIndex = tuple.Item4;
                int pivotPos = tuple.Item5;
                int usedMask = tuple.Item6;
                LinkedList<int> candidatePath = tuple.Item7;

                if (curLeft <= 0 && curRight <= 0)
                {
                    char[] buf = new char[candidatePath.Count];
                    int pi = 0;
                    foreach (int idx in candidatePath)
                    {
                        buf[pi++] = boardArray[idx / 4, idx % 4];
                    }
                    string candidateWord = new string(buf);

                    if (string.Equals(candidateWord, word, StringComparison.OrdinalIgnoreCase)) continue;

                    if (candidateWord.Length != word.Length) continue;

                    bool matchesDisplayed = true;
                    foreach (int idx in displayedIndices)
                    {
                        if (idx < 0 || idx >= candidateWord.Length || candidateWord[idx] != word[idx]) { matchesDisplayed = false; break; }
                    }
                    if (!matchesDisplayed) continue;

                    if (!seenWords.Contains(candidateWord) && dictionaryManager.IsValidWord(candidateWord))
                    {
                        seenWords.Add(candidateWord);
                        alternateWordCount++;
                    }

                    continue;
                }

                bool movingLeft = curLeft > 0;
                int currentLinear = movingLeft ? candidatePath.First.Value : candidatePath.Last.Value;

                int newCurLeftBase = movingLeft ? curLeft - 1 : curLeft;
                int newCurRightBase = movingLeft ? curRight : curRight - 1;
                int newNextLeftIndexBase = movingLeft ? nextLeftIndex - 1 : nextLeftIndex;
                int newNextRightIndexBase = movingLeft ? nextRightIndex : nextRightIndex + 1;
                int newPivotPosBase = movingLeft ? pivotPos + 1 : pivotPos;

                foreach (int neighborLinear in NeighborIndices[currentLinear])
                {
                    if ((usedMask & (1 << neighborLinear)) != 0) continue;

                    int newUsed = usedMask | (1 << neighborLinear);
                    LinkedList<int> newPath = new LinkedList<int>(candidatePath);
                    if (movingLeft) newPath.AddFirst(neighborLinear); else newPath.AddLast(neighborLinear);

                    queue.Enqueue(Tuple.Create(newCurLeftBase, newCurRightBase, newNextLeftIndexBase, newNextRightIndexBase, newPivotPosBase, newUsed, newPath));
                }
            }

            return alternateWordCount;
        }

        public List<Tuple<int, int>> FindBestWordPath(string word)
        {
            List<Tuple<int, int>> path = new List<Tuple<int, int>>();

            if (string.IsNullOrEmpty(word))
            {
                return path;
            }

            // chatgpt optimized: use linear indices and bitmask for used cells
            Queue<Tuple<int, int, int, List<int>>> queue = new(); // curLinear, guessIndex, usedMask, pathIndices
            for (int linear = 0; linear < 16; linear++)
            {
                int rr = linear / 4, rc = linear % 4;
                if (boardArray[rr, rc] == word[0])
                {
                    int usedMask = 1 << linear;
                    queue.Enqueue(Tuple.Create(linear, 0, usedMask, new List<int> { linear }));
                }
            }

            int longestGuess = -1;

            while (queue.Count != 0)
            {
                var t = queue.Dequeue();
                int curLinear = t.Item1;
                int guessIndex = t.Item2;
                int usedMask = t.Item3;
                List<int> pathIndices = t.Item4;

                if (guessIndex > longestGuess)
                {
                    longestGuess = guessIndex;
                    path = pathIndices.Select(li => Tuple.Create(li / 4, li % 4)).ToList();
                }

                if (guessIndex == word.Length - 1)
                {
                    return pathIndices.Select(li => Tuple.Create(li / 4, li % 4)).ToList();
                }

                int nextCharIdx = guessIndex + 1;
                foreach (int n in NeighborIndices[curLinear])
                {
                    if ((usedMask & (1 << n)) != 0) continue;
                    int nr = n / 4, nc = n % 4;
                    if (boardArray[nr, nc] != word[nextCharIdx]) continue;
                    int newMask = usedMask | (1 << n);
                    List<int> newPath = new List<int>(pathIndices) { n };
                    queue.Enqueue(Tuple.Create(n, nextCharIdx, newMask, newPath));
                }
            }

            return path;
        }

        public bool TryFindWordPath(string word, out List<Tuple<int, int>> path)
        {
            path = new List<Tuple<int, int>>();

            if (string.IsNullOrEmpty(word))
            {
                return false;
            }

            // optimized: linear indices + bitmask
            Queue<Tuple<int, int, int, List<int>>> queue = new();
            for (int linear = 0; linear < 16; linear++)
            {
                int rr = linear / 4, rc = linear % 4;
                if (boardArray[rr, rc] == word[0]) queue.Enqueue(Tuple.Create(linear, 0, 1 << linear, new List<int> { linear }));
            }

            while (queue.Count != 0)
            {
                var t = queue.Dequeue();
                int curLinear = t.Item1;
                int guessIndex = t.Item2;
                int usedMask = t.Item3;
                List<int> pathIndices = t.Item4;

                if (guessIndex == word.Length - 1)
                {
                    path = pathIndices.Select(li => Tuple.Create(li / 4, li % 4)).ToList();
                    return true;
                }

                int nextIndex = guessIndex + 1;
                foreach (int n in NeighborIndices[curLinear])
                {
                    if ((usedMask & (1 << n)) != 0) continue;
                    int nr = n / 4, nc = n % 4;
                    if (boardArray[nr, nc] != word[nextIndex]) continue;
                    int newMask = usedMask | (1 << n);
                    List<int> newPath = new List<int>(pathIndices) { n };
                    queue.Enqueue(Tuple.Create(n, nextIndex, newMask, newPath));
                }
            }

            return false;
        }

        public Board Mutate(ushort? protectedMask = null) // TODO: improve mutation
        {
            // try a number of random picks avoiding protected cells
            for (int attempt = 0; attempt < 64; attempt++)
            {
                int index = Random.Next(16);
                int row = index / 4;
                int col = index % 4;
                if (protectedMask.HasValue)
                {
                    int maskBit = 1 << index;
                    if ((protectedMask.Value & maskBit) != 0) continue;
                }
                boardArray[row, col] = GetRandomWeightedLetter();
                return this;
            }

            // if random attempts failed (maybe many protected), pick any unprotected by scanning
            List<int> candidates = new List<int>(16);
            for (int i = 0; i < 16; i++)
            {
                int r = i / 4, c = i % 4;
                if (protectedMask.HasValue)
                {
                    int maskBit = 1 << i;
                    if ((protectedMask.Value & maskBit) != 0) continue;
                }
                candidates.Add(i);
            }

            if (candidates.Count > 0)
            {
                int pick = candidates[Random.Next(candidates.Count)];
                int rr = pick / 4, cc = pick % 4;
                boardArray[rr, cc] = GetRandomWeightedLetter();
                return this;
            }

            // last resort: everything protected, mutate a random tile anyway
            int fallback = Random.Next(16);
            boardArray[fallback / 4, fallback % 4] = GetRandomWeightedLetter();
            return this;
        }

        public static int FindKthRarestLetter(string word, int k)
        {
            if (string.IsNullOrEmpty(word)) return 0;
            int n = word.Length;
            if (k <= 1) k = 1;
            if (k > n) k = n;

            // build list of (weight, index)
            List<Tuple<float, int>> weightsWithIndex = new List<Tuple<float, int>>(n);
            for (int i = 0; i < n; i++)
            {
                char c = char.ToUpperInvariant(word[i]);
                int idx = c - 'A';
                float w = 1.0f;
                if (idx >= 0 && idx < Weights.Length) w = Weights[idx];
                weightsWithIndex.Add(Tuple.Create(w, i));
            }

            // sort ascending by weight (rarest first)
            weightsWithIndex.Sort((a, b) => a.Item1.CompareTo(b.Item1));

            return weightsWithIndex[k - 1].Item2;
        }

        public Board(char[] letterSet)
        {
            boardArray = new char[4,4];
            for (int i = 0; i < 16; i++)
            {
                int row = i / 4;
                int col = i % 4;
                boardArray[row, col] = letterSet[Random.Next(letterSet.Length)];
            }
            // no linear cache
        }

        public Board(string board)
        {
            boardArray = new char[4, 4];
            for (int i = 0; i < 16; i++)
            {
                int row = i / 4;
                int col = i % 4;
                boardArray[row, col] = i < board.Length ? char.ToUpperInvariant(board[i]) : 'A';
            }
            // no linear cache
        }

        public Board() : this("AAAAAAAAAAAAAAAA")
        {
        }

        public char get(int r, int c)
        {
            return boardArray[r, c];
        }

        public char set(char value, int r, int c)
        {
            char old = boardArray[r, c];
            boardArray[r, c] = value;
            return old;
        }

        public char[,] ToArray()
        {
            return (char[,])boardArray.Clone();
        }

        public override string ToString()
        {
            char[] letters = new char[16];
            for (int i = 0; i < 16; i++)
            {
                int row = i / 4;
                int col = i % 4;
                letters[i] = boardArray[row, col];
            }
            return new string(letters);
        }
    }
}

public class Letter
{
    private char value;
    public List<Arrow> arrows { get; } = Enumerable.Range(0, 8).Select(_ => new Arrow()).ToList();

    public void changeLetter(char c)
    {
        value = c;
    }

    public char getLetter()
    {
        return value;
    }
}

public class Arrow
{
    public bool IsActive { get; private set; }

    public void SetActive(bool active)
    {
        IsActive = active;
    }
}
