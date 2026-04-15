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
            var result = TrainingStep2(completeBoards.Pop(), words, displayedPositionsPerWord);
            if (result != null && result.Item1 != null)
            {
                optimizedBoards.Add(result.Item1);
                perBoardDisplayedMasks.Add(result.Item2);
            }
        }

        string[] boardStrings = optimizedBoards.Select(board => board.ToString()).ToArray();

        bool[][][] perBoardDisplayed = perBoardDisplayedMasks.Select(maskSet => maskSet.Select(m => m.ToArray()).ToArray()).ToArray();

        return Tuple.Create(boardStrings, perBoardDisplayed);
    }

    public Board? TrainingStep1(Board board, string[] words)
    {
        int limit = 2000;
        float previousScore = float.MinValue;
        int previousFound = 0;
        Board previousBoard = new Board(board.ToString());
        Board currentBoard = new Board(board.ToString());
        int targetScore = 0;
        foreach (string word in words) {
            targetScore += word.Length;
        }

        while (limit > 0)
        {
            float currentScore = ScoreBoardOnWords(currentBoard, words);

            int currentFound = 0;
            foreach (string w in words)
            {
                if (currentBoard.TryFindWordPath(w, out _)) currentFound += 1;
            }

            if (currentFound == words.Length)
            {
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
            }

            currentBoard = boardToMutate.Mutate();

            limit -= 1;
        }

        return null;
    }

    public Tuple<Board, bool[][]>? TrainingStep2(Board board, string[] words, List<bool[]> displayedLetters)
    {
        int initialPerDisplaySetLimit = Math.Max(100, 150 * words.Length);
        int perDisplaySetLimit = initialPerDisplaySetLimit;
        float previousScore = float.MaxValue;
        Board previousBoard = new Board(board.ToString());
        Board currentBoard = new Board(board.ToString());
        float targetScore = 0;
        // ensure we preserve word coverage: compute target word coverage score (sum of word lengths)
        int wordTargetScore = 0;
        foreach (string w in words) wordTargetScore += w.Length;
        
        // clone the displayed-positions masks so we can modify locally if needed
        List<bool[]> currentDisplayedLetters = displayedLetters.Select(mask => mask.ToArray()).ToList();

        int currentWordToDisplayMore = 0;

        while (perDisplaySetLimit > 0)
        {
            float currentScore = ScoreBoardOnAlternatives(currentBoard, words, currentDisplayedLetters);
            if (currentScore <= targetScore)
            {
                // return the board plus the final displayed masks
                bool[][] finalMasks = currentDisplayedLetters.Select(m => m.ToArray()).ToArray();
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
            }

            Board mutated = new Board(boardToMutate.ToString()).Mutate();
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

    public class Board
    {
        private readonly char[,] boardArray;
        private static readonly int[] LetterCounts = { 118838, 28836, 64177, 53136, 180794, 19335, 42424, 36493, 141463, 2497, 13309, 83427, 44529, 107519, 103536, 46141, 2535, 111061, 149453, 104965, 51299, 15363, 11689, 4610, 25637, 7442 };
        private static readonly int TotalLetterCount = LetterCounts.Sum();
        public static readonly float[] Weights = LetterCounts.Select(count => count / (float)TotalLetterCount).ToArray();

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

            // leftlength, rightlength, nextLeftIndex, nextRightIndex, pivotPos, usedarray, current path
            Queue<Tuple<int, int, int, int, int, int[,], List<Tuple<int, int>>>> queue = new();

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

            for (int i = 0; i < boardArray.GetLength(0); i++)
            {
                for (int j = 0; j < boardArray.GetLength(1); j++)
                {
                    if (boardArray[i, j] == word[seedDisplayIndex])
                    {
                        int[,] usedArray = new int[boardArray.GetLength(0), boardArray.GetLength(1)];
                        usedArray[i, j] = 1;
                        List<Tuple<int, int>> candidatePath = new();
                        candidatePath.Add(Tuple.Create(i, j));
                        int nextLeftIndex = seedDisplayIndex - 1;
                        int nextRightIndex = seedDisplayIndex + 1;
                        int leftLength = seedDisplayIndex;
                        int rightLength = word.Length - seedDisplayIndex - 1;
                        int pivotPos = 0; // pivot is at index 0 in candidatePath initially
                        queue.Enqueue(Tuple.Create(leftLength, rightLength, nextLeftIndex, nextRightIndex, pivotPos, usedArray, candidatePath));
                    }
                }
            }

            var neighborOffsets = new List<Tuple<int, int>> {
                Tuple.Create(-1, 0), Tuple.Create(-1, -1), Tuple.Create(-1, 1),
                Tuple.Create(1, 0), Tuple.Create(1, -1), Tuple.Create(1, 1),
                Tuple.Create(0, -1), Tuple.Create(0, 1)
            };

            while (queue.Count != 0)
            {
                Tuple<int, int, int, int, int, int[,], List<Tuple<int, int>>> tuple = queue.Dequeue();

                int curLeft = tuple.Item1;
                int curRight = tuple.Item2;
                int nextLeftIndex = tuple.Item3;
                int nextRightIndex = tuple.Item4;
                int pivotPos = tuple.Item5;
                int[,] usedArray = tuple.Item6;
                List<Tuple<int, int>> candidatePath = tuple.Item7;

                if (curLeft <= 0 && curRight <= 0)
                {
                    string candidateWord = "";
                    foreach (Tuple<int, int> coordinate in candidatePath)
                    {
                        candidateWord += boardArray[coordinate.Item1, coordinate.Item2];
                    }

                    // skip the original target word and duplicates
                    if (string.Equals(candidateWord, word, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // ensure candidate matches the displayed characters at displayed indices
                    bool matchesDisplayed = true;
                    foreach (int idx in displayedIndices)
                    {
                        if (idx < 0 || idx >= candidateWord.Length || candidateWord[idx] != word[idx])
                        {
                            matchesDisplayed = false;
                            break;
                        }
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

                int i = candidatePath[movingLeft ? 0 : candidatePath.Count - 1].Item1;
                int j = candidatePath[movingLeft ? 0 : candidatePath.Count - 1].Item2;

                // compute next-state bases based on direction (left vs right growth)
                int newCurLeftBase = movingLeft ? curLeft - 1 : curLeft;
                int newCurRightBase = movingLeft ? curRight : curRight - 1;
                int newNextLeftIndexBase = movingLeft ? nextLeftIndex - 1 : nextLeftIndex;
                int newNextRightIndexBase = movingLeft ? nextRightIndex : nextRightIndex + 1;
                int newPivotPosBase = movingLeft ? pivotPos + 1 : pivotPos;

                foreach (var offset in neighborOffsets)
                {
                    int neighborRow = i + offset.Item1;
                    int neighborCol = j + offset.Item2;

                    // bounds check
                    if (neighborRow < 0 || neighborRow >= boardArray.GetLength(0) || neighborCol < 0 || neighborCol >= boardArray.GetLength(1))
                        continue;

                    if (usedArray[neighborRow, neighborCol] == 1) continue;

                    int[,] newUsedArray = (int[,])usedArray.Clone();
                    newUsedArray[neighborRow, neighborCol] = 1;
                    List<Tuple<int, int>> newPath = new(candidatePath);
                    if (movingLeft)
                    {
                        newPath.Insert(0, Tuple.Create(neighborRow, neighborCol));
                    }
                    else
                    {
                        newPath.Add(Tuple.Create(neighborRow, neighborCol));
                    }

                    queue.Enqueue(Tuple.Create(newCurLeftBase, newCurRightBase, newNextLeftIndexBase, newNextRightIndexBase, newPivotPosBase, newUsedArray, newPath));
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

            Queue<Tuple<int, int, int, int[,], List<Tuple<int, int>>>> queue = new();

            for (int row = 0; row < boardArray.GetLength(0); row++)
            {
                for (int col = 0; col < boardArray.GetLength(1); col++)
                {
                    if (boardArray[row, col] == word[0])
                    {
                        int[,] usedArray = new int[boardArray.GetLength(0), boardArray.GetLength(1)];
                        usedArray[row, col] = 1;
                        List<Tuple<int, int>> candidatePath = new();
                        candidatePath.Add(Tuple.Create(row, col));
                        queue.Enqueue(Tuple.Create(row, col, 0, usedArray, candidatePath));
                    }
                }
            }

            int longestPathIndex = -1;

            var neighborOffsets = new List<Tuple<int, int>> {
                Tuple.Create(-1, 0), Tuple.Create(-1, -1), Tuple.Create(-1, 1),
                Tuple.Create(1, 0), Tuple.Create(1, -1), Tuple.Create(1, 1),
                Tuple.Create(0, -1), Tuple.Create(0, 1)
            };

            while (queue.Count != 0)
            {
                var tuple = queue.Dequeue();
                int curRow = tuple.Item1;
                int curCol = tuple.Item2;
                int guessIndex = tuple.Item3;
                int[,] usedArray = tuple.Item4;
                List<Tuple<int, int>> candidatePath = tuple.Item5;

                if (guessIndex > longestPathIndex)
                {
                    longestPathIndex = guessIndex;
                    path = candidatePath;
                }

                if (guessIndex == word.Length - 1)
                {
                    return candidatePath;
                }

                foreach (var offset in neighborOffsets)
                {
                    int neighborRow = curRow + offset.Item1;
                    int neighborCol = curCol + offset.Item2;

                    if (neighborRow < 0 || neighborRow >= boardArray.GetLength(0) || neighborCol < 0 || neighborCol >= boardArray.GetLength(1))
                        continue;

                    if (usedArray[neighborRow, neighborCol] != 1 && boardArray[neighborRow, neighborCol] == word[guessIndex + 1])
                    {
                        int[,] newUsedArray = (int[,])usedArray.Clone();
                        newUsedArray[neighborRow, neighborCol] = 1;
                        List<Tuple<int, int>> newPath = new(candidatePath);
                        newPath.Add(Tuple.Create(neighborRow, neighborCol));
                        queue.Enqueue(Tuple.Create(neighborRow, neighborCol, guessIndex + 1, newUsedArray, newPath));
                    }
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

            Queue<Tuple<int, int, int, int[,], List<Tuple<int, int>>>> queue = new();
            for (int row = 0; row < boardArray.GetLength(0); row++)
            {
                for (int col = 0; col < boardArray.GetLength(1); col++)
                {
                    if (boardArray[row, col] == word[0])
                    {
                        int[,] usedArray = new int[boardArray.GetLength(0), boardArray.GetLength(1)];
                        usedArray[row, col] = 1;
                        List<Tuple<int, int>> candidatePath = new();
                        candidatePath.Add(Tuple.Create(row, col));
                        queue.Enqueue(Tuple.Create(row, col, 0, usedArray, candidatePath));
                    }
                }
            }

            var neighborOffsets = new List<Tuple<int, int>> {
                Tuple.Create(-1, 0), Tuple.Create(-1, -1), Tuple.Create(-1, 1),
                Tuple.Create(1, 0), Tuple.Create(1, -1), Tuple.Create(1, 1),
                Tuple.Create(0, -1), Tuple.Create(0, 1)
            };

            while (queue.Count != 0)
            {
                var tuple = queue.Dequeue();
                int curRow = tuple.Item1;
                int curCol = tuple.Item2;
                int guessIndex = tuple.Item3;
                int[,] usedArray = tuple.Item4;
                List<Tuple<int, int>> candidatePath = tuple.Item5;

                if (guessIndex == word.Length - 1)
                {
                    path = candidatePath;
                    return true;
                }

                foreach (var offset in neighborOffsets)
                {
                    int neighborRow = curRow + offset.Item1;
                    int neighborCol = curCol + offset.Item2;

                    if (neighborRow < 0 || neighborRow >= boardArray.GetLength(0) || neighborCol < 0 || neighborCol >= boardArray.GetLength(1))
                        continue;

                    if (usedArray[neighborRow, neighborCol] != 1 && boardArray[neighborRow, neighborCol] == word[guessIndex + 1])
                    {
                        int[,] newUsedArray = (int[,])usedArray.Clone();
                        newUsedArray[neighborRow, neighborCol] = 1;
                        List<Tuple<int, int>> newPath = new(candidatePath);
                        newPath.Add(Tuple.Create(neighborRow, neighborCol));
                        queue.Enqueue(Tuple.Create(neighborRow, neighborCol, guessIndex + 1, newUsedArray, newPath));
                    }
                }
            }

            return false;
        }

        public Board Mutate() // TODO: improve mutation
        {
            int index = Random.Next(16);
            int row = index / 4;
            int col = index % 4;
            boardArray[row, col] = GetRandomWeightedLetter();

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
