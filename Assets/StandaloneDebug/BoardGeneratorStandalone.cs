using System;
using System.Collections.Generic;
using System.Linq;

namespace SignScramble.StandaloneDebug;

/*
    This script generates a 4x4 board of letters containing words.
*/

public class BoardGenerator
{
    private readonly object? letterPrefab;

    private readonly bool usingTest;
    private Board currentBoard;
    private readonly Letter[,] letterObjects; // Store references to Letter components
    private bool isInitialized;
    private static readonly Random Random = new();

    public BoardGenerator(bool usingTest = true, object? letterPrefab = null)
    {
        this.usingTest = usingTest;
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
        if (usingTest)
        {
            GenerateBoards(new[] { "CLOCK", "DOOR", "ROD", "SIGN", "CROOKS" }, 10);
        }
        else
        {
            throw new Exception("Need to use test words for now."); // TODO: set up word sets
        }
    }

    public Tuple<string[], bool[][][]> GenerateBoards(string[] words, int numberOfBoards)
    {
        if (!usingTest) throw new Exception("Need to use test for now.");
        if (!ValidateWordSet(words))
        {
            throw new Exception("Invalid word set.");
        }

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

        // adaptive training1 limit: scale with number of target words to balance
        // quality vs runtime. For small sets (5 words) this yields ~2000 iterations;
        // for larger sets it scales up linearly.
        int training1Limit = Math.Max(500, 400 * words.Length);

        while (completeBoards.Count < numberOfBoards)
        {
            Board newBoard = new Board(letterSet.ToCharArray());

            Board? trainedBoard = TrainingStep1(newBoard, words);

            if (trainedBoard != null)
            {
                completeBoards.Push(trainedBoard);
                Console.WriteLine($"[BoardGenerator] TrainingStep1 produced board #{completeBoards.Count}: {trainedBoard}");
            }

            training1Limit -= 1;
            if (training1Limit <= 0)
            {
                throw new Exception("First training step timed out.");
            }
        }

        // For each word create a boolean mask (per-index) indicating which letters are displayed.
        List<bool[]> displayedPositionsPerWord = new List<bool[]>(words.Length);
        for (int i = 0; i < words.Length; i++)
        {
            // choose the rarest letter index as the initially revealed letter
            int rarestLetterIndex = Board.FindKthRarestLetter(words[i], 1);

            bool[] mask = new bool[words[i].Length];
            if (mask.Length > 0 && rarestLetterIndex >= 0 && rarestLetterIndex < mask.Length) mask[rarestLetterIndex] = true;
            displayedPositionsPerWord.Add(mask);
        }

        List<Board> optimizedBoards = new List<Board>(numberOfBoards);
        List<bool[][]> perBoardDisplayedMasks = new List<bool[][]>(numberOfBoards);

        while (completeBoards.Count > 0)
        {
            var b = completeBoards.Pop();
            // compute static protection mask from training step 1 (complete paths on this board)
            var protectedFromStep1 = GetProtectedCellsFromBestPaths(b, words);
            var result = TrainingStep2(b, words, displayedPositionsPerWord, protectedFromStep1);
            if (result != null && result.Item1 != null)
            {
                optimizedBoards.Add(result.Item1);
                perBoardDisplayedMasks.Add(result.Item2);
                Console.WriteLine($"[BoardGenerator] TrainingStep2 completed for board: {result.Item1}");
                Console.WriteLine($"[BoardGenerator] Final displayed masks: {FormatMasks(result.Item2.Select(m => m).ToList())}");
            }
        }

        // sort boards by increasing number of revealed letters (fewer reveals = better)
        var paired = optimizedBoards.Select((b, i) => new {
            board = b,
            masks = perBoardDisplayedMasks[i],
            revealed = perBoardDisplayedMasks[i].Sum(m => m.Count(x => x))
        }).OrderBy(p => p.revealed).ToList();

        string[] boardStrings = paired.Select(p => p.board.ToString()).ToArray();
        bool[][][] perBoardDisplayed = paired.Select(p => p.masks.Select(m => m.ToArray()).ToArray()).ToArray();

        return Tuple.Create(boardStrings, perBoardDisplayed);
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
        // start with protection discovered from previousBoard's complete paths
        bool[,] cumulativeProtected = GetProtectedCellsFromBestPaths(previousBoard, words);
        bool[] previousFoundFlags = new bool[words.Length];
        for (int i = 0; i < words.Length; i++) previousFoundFlags[i] = previousBoard.TryFindWordPath(words[i], out _);
        previousScore = ScoreBoardOnWordsAndProtect(previousBoard, words, cumulativeProtected, previousFoundFlags);
        previousFound = previousFoundFlags.Count(f => f);
        int targetScore = 0;
        foreach (string word in words) {
            targetScore += word.Length;
        }

        while (limit > 0)
        {
            // score and collect protected cells for current board in a single pass
            bool[] currentFoundFlags = new bool[words.Length];
            bool[,] tempProtected = new bool[4,4];
            float currentScore = ScoreBoardOnWordsAndProtect(currentBoard, words, tempProtected, currentFoundFlags);
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
                UnionProtected(cumulativeProtected, tempProtected);
            }

            // mutate while avoiding any cumulatively protected cells
            currentBoard = boardToMutate.Mutate(cumulativeProtected);

            limit -= 1;
        }

        Console.WriteLine($"[BoardGenerator] TrainingStep1 timed out. bestFound={previousFound} bestScore={previousScore} bestBoard={previousBoard}");
        return null;
    }

    public Tuple<Board, bool[][]>? TrainingStep2(Board board, string[] words, List<bool[]> displayedLetters, bool[,]? protectedFromStep1 = null)
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
        List<bool[]> currentDisplayedLetters = displayedLetters.Select(mask => mask.ToArray()).ToList();

        Console.WriteLine($"[BoardGenerator] TrainingStep2 start board={board} initialMasks={FormatMasks(currentDisplayedLetters)} initialLimit={initialPerDisplaySetLimit}");

        int currentWordToDisplayMore = 0;

        // simple strict loop: compute alternatives only, require zero alternatives for success
        while (perDisplaySetLimit > 0)
        {
            float currentScore = ScoreBoardOnAlternatives(currentBoard, words, currentDisplayedLetters);
            int iter = initialPerDisplaySetLimit - perDisplaySetLimit;
            if (iter - lastLogIter >= 50 || currentScore < previousScore)
            {
                Console.WriteLine($"[BoardGenerator] TrainingStep2 iter={iter} currentScore={currentScore} prevScore={previousScore} perDisplayLimit={perDisplaySetLimit} masks={FormatMasks(currentDisplayedLetters)}");
                lastLogIter = iter;
            }
            if (currentScore <= targetScore)
            {
                // return the board plus the final displayed masks
                bool[][] finalMasks = currentDisplayedLetters.Select(m => m.ToArray()).ToArray();
                Console.WriteLine($"[BoardGenerator] TrainingStep2 success finalScore={currentScore} masks={FormatMasks(currentDisplayedLetters)}");
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
                // reveal one more letter (the next-rarest undisplayed) for the next eligible word
                bool displayedAll = true;
                for (int wi = 0; wi < currentDisplayedLetters.Count; wi++)
                {
                    if (currentDisplayedLetters[wi].Any(b => !b))
                    {
                        displayedAll = false;
                        break;
                    }
                }

                if (displayedAll)
                {
                    throw new Exception("Training step 2 timed out after all letters displayed.");
                }

                // find next word (starting from currentWordToDisplayMore) that still has undisplayed letters
                int foundWord = -1;
                for (int offset = 0; offset < words.Length; offset++)
                {
                    int idx = (currentWordToDisplayMore + offset) % words.Length;
                    int displayedCount = currentDisplayedLetters[idx].Count(b => b);
                    if (displayedCount < words[idx].Length)
                    {
                        foundWord = idx;
                        break;
                    }
                }

                if (foundWord == -1)
                {
                    perDisplaySetLimit = initialPerDisplaySetLimit;
                    throw new Exception("Training step 2 failed.");
                }

                int currentlyShown = currentDisplayedLetters[foundWord].Count(b => b);
                int nextK = currentlyShown + 1;
                int revealIndex = Board.FindKthRarestLetter(words[foundWord], nextK);
                if (revealIndex >= 0 && revealIndex < currentDisplayedLetters[foundWord].Length)
                {
                    currentDisplayedLetters[foundWord][revealIndex] = true;
                    Console.WriteLine($"[BoardGenerator] Revealed letter for word #{foundWord} at index {revealIndex}; masks now: {FormatMasks(currentDisplayedLetters)}");
                    // Immediately evaluate alternatives after reveal so we see search activity tied to this reveal
                    float postRevealScore = ScoreBoardOnAlternatives(currentBoard, words, currentDisplayedLetters);
                    Console.WriteLine($"[BoardGenerator] Post-reveal alternative score={postRevealScore} masks={FormatMasks(currentDisplayedLetters)}");
                }

                // advance pointer for next time
                currentWordToDisplayMore = (foundWord + 1) % words.Length;

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

    public float ScoreBoardOnAlternatives(Board board, string[] words, List<bool[]> displayed)
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
    public static List<char> GetDisplayedCharactersFromMask(string word, bool[] displayedMask)
    {
        List<char> displayed = new List<char>();
        if (string.IsNullOrEmpty(word) || displayedMask == null) return displayed;
        int len = Math.Min(word.Length, displayedMask.Length);
        for (int i = 0; i < len; i++)
        {
            if (displayedMask[i]) displayed.Add(word[i]);
        }
        return displayed;
    }

    // Helper: format boolean masks for logging
    private static string FormatMasks(List<bool[]> masks)
    {
        if (masks == null || masks.Count == 0) return "[]";
        return string.Join(" | ", masks.Select(m => new string(m.Select(b => b ? '1' : '0').ToArray())));
    }

    // Collect cells used by the complete paths for each word (used after TrainingStep1)
    // This checks for full paths (uses TryFindWordPath) so protected cells are truly part of complete solutions.
    private static bool[,] GetProtectedCellsFromBestPaths(Board board, string[] words)
    {
        bool[,] prot = new bool[4,4];
        if (board == null || words == null) return prot;
        foreach (string w in words)
        {
            if (board.TryFindWordPath(w, out var path) && path != null)
            {
                foreach (var t in path)
                {
                    int r = t.Item1, c = t.Item2;
                    if (r >= 0 && r < 4 && c >= 0 && c < 4) prot[r,c] = true;
                }
            }
        }
        return prot;
    }

    // NOTE: removed redundant complete-path collector; GetProtectedCellsFromBestPaths handles full-path detection.

    private static void UnionProtected(bool[,] target, bool[,] source)
    {
        if (target == null || source == null) return;
        for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) target[r,c] = target[r,c] || source[r,c];
    }

    private static void AddPathToProtected(bool[,] prot, List<Tuple<int,int>> path)
    {
        if (prot == null || path == null) return;
        foreach (var t in path)
        {
            int r = t.Item1, c = t.Item2;
            if (r >= 0 && r < 4 && c >= 0 && c < 4) prot[r,c] = true;
        }
    }

    // Score words while also marking protected cells for successfully found complete paths.
    private static float ScoreBoardOnWordsAndProtect(Board board, string[] words, bool[,] protectedCells, bool[]? foundFlags)
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

            if (board.TryFindWordPath(words[i], out var path) && path != null && path.Count > 0)
            {
                if (foundFlags != null) foundFlags[i] = true;
                if (protectedCells != null) AddPathToProtected(protectedCells, path);
                score += Math.Min(path.Count, words[i].Length);
            }
        }

        return score;
    }

    // Score alternatives while also marking protected cells for any successfully found complete paths.
    private static float ScoreBoardOnAlternativesAndProtect(Board board, string[] words, List<bool[]> displayed, bool[,]? protectedCells, bool[]? foundFlags)
    {
        int score = 0;
        if (board == null || words == null || displayed == null) return score;
        for (int i = 0; i < words.Length; i++)
        {
            if (foundFlags != null && foundFlags[i])
            {
                // word already known to have a complete path, skip alternative calculation
                continue;
            }

            if (board.TryFindWordPath(words[i], out var path) && path != null && path.Count > 0)
            {
                if (foundFlags != null) foundFlags[i] = true;
                if (protectedCells != null) AddPathToProtected(protectedCells, path);
                continue;
            }

            int alternatives = board.FindAlternateWordsFromPosition(words[i], displayed[i]);
            score += alternatives;
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

        public int FindAlternateWordsFromPosition(string word, bool[] displayedPositions)
        {
            if (string.IsNullOrEmpty(word) || displayedPositions == null)
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
            for (int k = 0; k < Math.Min(word.Length, displayedPositions.Length); k++) if (displayedPositions[k]) displayedIndices.Add(k);

            // leftlength, rightlength, nextLeftIndex, nextRightIndex, pivotPos, usedMask, current path (linear indices)
            Queue<Tuple<int, int, int, int, int, int, List<int>>> queue = new();

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
                    List<int> candidatePath = new List<int> { linear };
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
                List<int> candidatePath = tuple.Item7;

                if (curLeft <= 0 && curRight <= 0)
                {
                    char[] buf = new char[candidatePath.Count];
                    for (int pi = 0; pi < candidatePath.Count; pi++)
                    {
                        int idx = candidatePath[pi];
                        buf[pi] = boardArray[idx/4, idx%4];
                    }
                    string candidateWord = new string(buf);

                    if (string.Equals(candidateWord, word, StringComparison.OrdinalIgnoreCase)) continue;

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
                int currentLinear = movingLeft ? candidatePath[0] : candidatePath[candidatePath.Count - 1];

                int newCurLeftBase = movingLeft ? curLeft - 1 : curLeft;
                int newCurRightBase = movingLeft ? curRight : curRight - 1;
                int newNextLeftIndexBase = movingLeft ? nextLeftIndex - 1 : nextLeftIndex;
                int newNextRightIndexBase = movingLeft ? nextRightIndex : nextRightIndex + 1;
                int newPivotPosBase = movingLeft ? pivotPos + 1 : pivotPos;

                foreach (int neighborLinear in NeighborIndices[currentLinear])
                {
                    if ((usedMask & (1 << neighborLinear)) != 0) continue;

                    int newUsed = usedMask | (1 << neighborLinear);
                    List<int> newPath = new List<int>(candidatePath);
                    if (movingLeft) newPath.Insert(0, neighborLinear); else newPath.Add(neighborLinear);

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

        public Board Mutate(bool[,]? protectedCells = null) // TODO: improve mutation
        {
            // try a number of random picks avoiding protected cells
            for (int attempt = 0; attempt < 64; attempt++)
            {
                int index = Random.Next(16);
                int row = index / 4;
                int col = index % 4;
                if (protectedCells != null)
                {
                    try
                    {
                        if (protectedCells[row, col]) continue;
                    }
                    catch { /* out-of-range: ignore protection */ }
                }
                boardArray[row, col] = GetRandomWeightedLetter();
                return this;
            }

            // if random attempts failed (maybe many protected), pick any unprotected by scanning
            List<int> candidates = new List<int>(16);
            for (int i = 0; i < 16; i++)
            {
                int r = i / 4, c = i % 4;
                if (protectedCells != null)
                {
                    try
                    {
                        if (protectedCells[r, c]) continue;
                    }
                    catch { }
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
