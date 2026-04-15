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

    public Tuple<string[], Tuple<char, int>[][]> GenerateBoards(string[] words, int numberOfBoards)
    {
        if (!usingTest) throw new Exception("Need to use test for now."); // TODO: allow word sets
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

        int training1Limit = 2000;

        while (completeBoards.Count < numberOfBoards)
        {
            Board newBoard = new Board(letterSet.ToCharArray());

            Board trainedBoard = TrainingStep1(newBoard, words);

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

        List<Tuple<char, int>> displayedLetters = new List<Tuple<char, int>>(words.Length);

        for (int i = 0; i < words.Length; i++)
        {
            float lowestWeight = float.MaxValue;
            char rarestLetter = words[i].Length > 0 ? words[i][0] : 'A';
            int letterIndex = 0;
            for (int j = 0; j < words[i].Length; j++) {
                char letter = words[i][j];
                float w = Board.Weights[letter - 'A'];
                if (w < lowestWeight)
                {
                    lowestWeight = w;
                    rarestLetter = letter;
                    letterIndex = j;
                }
            }
            displayedLetters.Add(Tuple.Create(rarestLetter, letterIndex));
        }

        List<Board> optimizedBoards = new List<Board>(numberOfBoards);

        while (completeBoards.Count > 0)
        {
            Board optimizedBoard = TrainingStep2(completeBoards.Pop(), words, displayedLetters);

            optimizedBoards.Add(optimizedBoard);
        }

        string[] boardStrings = optimizedBoards.Select(board => board.ToString()).ToArray();

        Tuple<char, int>[] displayedArray = displayedLetters.ToArray();
        Tuple<char, int>[][] perBoardDisplayed = Enumerable.Range(0, boardStrings.Length)
            .Select(_ => displayedArray)
            .ToArray();

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

            // compute how many distinct target words are currently present
            int currentFound = 0;
            foreach (string w in words)
            {
                if (currentBoard.TryFindWordPath(w, out _)) currentFound += 1;
            }

            // primary objective: coverage of all words
            if (currentFound == words.Length)
            {
                return currentBoard;
            }

            // prefer mutations that increase distinct-word coverage; break ties using the length-based score
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

    public Board? TrainingStep2(Board board, string[] words, List<Tuple<char, int>> displayedLetters)
    {
        int limit = 500;
        float previousScore = float.MaxValue;
        Board previousBoard = new Board(board.ToString());
        Board currentBoard = new Board(board.ToString());
        float targetScore = 0;
        // ensure we preserve word coverage: compute target word coverage score (sum of word lengths)
        int wordTargetScore = 0;
        foreach (string w in words) wordTargetScore += w.Length;

        while (limit > 0)
        {
            float currentScore = ScoreBoardOnAlternatives(currentBoard, words, displayedLetters);
            if (currentScore <= targetScore)
            {
                return currentBoard;
            }

            // TODO: improve mutation

            // TODO: if stuck, add more displayed letters

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

            limit -= 1;
            if (limit <= 0) {
                throw new Exception("Training step 2 failed. Need to change displayed letters probably.");
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

    public float ScoreBoardOnAlternatives(Board board, string[] words, List<Tuple<char, int>> displayed)
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

        public int FindAlternateWordsFromPosition(string word, Tuple<char, int> displayed)
        {
            if (string.IsNullOrEmpty(word) || displayed == null)
            {
                throw new Exception("Illegal argument in find alternate words from position.");
            }

            int alternateWordCount = 0;
            HashSet<string> seenWords = new(StringComparer.OrdinalIgnoreCase);

            int leftLength = displayed.Item2;
            int rightLength = word.Length - displayed.Item2 - 1;

            // leftlength, rightlength, nextLeftIndex, nextRightIndex, pivotPos, usedarray, current path
            Queue<Tuple<int, int, int, int, int, int[,], List<Tuple<int, int>>>> queue = new();
            for (int i = 0; i < boardArray.GetLength(0); i++)
            {
                for (int j = 0; j < boardArray.GetLength(1); j++)
                {
                    if (boardArray[i, j] == displayed.Item1)
                    {
                        int[,] usedArray = new int[boardArray.GetLength(0), boardArray.GetLength(1)];
                        usedArray[i, j] = 1;
                        List<Tuple<int, int>> candidatePath = new();
                        candidatePath.Add(Tuple.Create(i, j));
                        int nextLeftIndex = displayed.Item2 - 1;
                        int nextRightIndex = displayed.Item2 + 1;
                        int pivotPos = 0; // pivot is at index 0 in candidatePath initially
                        queue.Enqueue(Tuple.Create(leftLength, rightLength, nextLeftIndex, nextRightIndex, pivotPos, usedArray, candidatePath));
                    }
                }
            }

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

                // expected character index for the neighbor based on direction
                int expectedIndex = movingLeft ? nextLeftIndex : nextRightIndex;
                char expectedChar = (expectedIndex >= 0 && expectedIndex < word.Length) ? word[expectedIndex] : '\0';

                if (i > 0)
                {
                    if (usedArray[i - 1, j] != 1)
                    {
                        int[,] newUsedArray = (int[,])usedArray.Clone();
                        newUsedArray[i - 1, j] = 1;
                        List<Tuple<int, int>> newPath = new(candidatePath);
                        if (movingLeft)
                        {
                            newPath.Insert(0, Tuple.Create(i - 1, j));
                        }
                        else
                        {
                            newPath.Add(Tuple.Create(i - 1, j));
                        }
                        queue.Enqueue(Tuple.Create(movingLeft ? curLeft - 1 : curLeft,
                            movingLeft ? curRight : curRight - 1,
                            movingLeft ? nextLeftIndex - 1 : nextLeftIndex,
                            movingLeft ? nextRightIndex : nextRightIndex + 1,
                            movingLeft ? pivotPos + 1 : pivotPos,
                            newUsedArray, newPath));
                    }

                    if (j > 0)
                    {
                        if (usedArray[i - 1, j - 1] != 1)
                        {
                            int[,] newUsedArray = (int[,])usedArray.Clone();
                            newUsedArray[i - 1, j - 1] = 1;
                            List<Tuple<int, int>> newPath = new(candidatePath);
                            if (movingLeft)
                            {
                                newPath.Insert(0, Tuple.Create(i - 1, j - 1));
                            }
                            else
                            {
                                newPath.Add(Tuple.Create(i - 1, j - 1));
                            }
                            queue.Enqueue(Tuple.Create(movingLeft ? curLeft - 1 : curLeft,
                                movingLeft ? curRight : curRight - 1,
                                movingLeft ? nextLeftIndex - 1 : nextLeftIndex,
                                movingLeft ? nextRightIndex : nextRightIndex + 1,
                                movingLeft ? pivotPos + 1 : pivotPos,
                                newUsedArray, newPath));
                        }
                    }

                    if (j < boardArray.GetLength(1) - 1)
                    {
                        if (usedArray[i - 1, j + 1] != 1)
                        {
                            int[,] newUsedArray = (int[,])usedArray.Clone();
                            newUsedArray[i - 1, j + 1] = 1;
                            List<Tuple<int, int>> newPath = new(candidatePath);
                            if (movingLeft)
                            {
                                newPath.Insert(0, Tuple.Create(i - 1, j + 1));
                            }
                            else
                            {
                                newPath.Add(Tuple.Create(i - 1, j + 1));
                            }
                            queue.Enqueue(Tuple.Create(movingLeft ? curLeft - 1 : curLeft,
                                movingLeft ? curRight : curRight - 1,
                                movingLeft ? nextLeftIndex - 1 : nextLeftIndex,
                                movingLeft ? nextRightIndex : nextRightIndex + 1,
                                movingLeft ? pivotPos + 1 : pivotPos,
                                newUsedArray, newPath));
                        }
                    }
                }

                if (i < boardArray.GetLength(0) - 1)
                {
                    if (usedArray[i + 1, j] != 1)
                    {
                        int[,] newUsedArray = (int[,])usedArray.Clone();
                        newUsedArray[i + 1, j] = 1;
                        List<Tuple<int, int>> newPath = new(candidatePath);
                        if (movingLeft)
                        {
                            newPath.Insert(0, Tuple.Create(i + 1, j));
                        }
                        else
                        {
                            newPath.Add(Tuple.Create(i + 1, j));
                        }
                        queue.Enqueue(Tuple.Create(movingLeft ? curLeft - 1 : curLeft,
                            movingLeft ? curRight : curRight - 1,
                            movingLeft ? nextLeftIndex - 1 : nextLeftIndex,
                            movingLeft ? nextRightIndex : nextRightIndex + 1,
                            movingLeft ? pivotPos + 1 : pivotPos,
                            newUsedArray, newPath));
                    }

                    if (j > 0)
                    {
                        if (usedArray[i + 1, j - 1] != 1)
                        {
                            int[,] newUsedArray = (int[,])usedArray.Clone();
                            newUsedArray[i + 1, j - 1] = 1;
                            List<Tuple<int, int>> newPath = new(candidatePath);
                            if (movingLeft)
                            {
                                newPath.Insert(0, Tuple.Create(i + 1, j - 1));
                            }
                            else
                            {
                                newPath.Add(Tuple.Create(i + 1, j - 1));
                            }
                            queue.Enqueue(Tuple.Create(movingLeft ? curLeft - 1 : curLeft,
                                movingLeft ? curRight : curRight - 1,
                                movingLeft ? nextLeftIndex - 1 : nextLeftIndex,
                                movingLeft ? nextRightIndex : nextRightIndex + 1,
                                movingLeft ? pivotPos + 1 : pivotPos,
                                newUsedArray, newPath));
                        }
                    }

                    if (j < boardArray.GetLength(1) - 1)
                    {
                        if (usedArray[i + 1, j + 1] != 1)
                        {
                            int[,] newUsedArray = (int[,])usedArray.Clone();
                            newUsedArray[i + 1, j + 1] = 1;
                            List<Tuple<int, int>> newPath = new(candidatePath);
                            if (movingLeft)
                            {
                                newPath.Insert(0, Tuple.Create(i + 1, j + 1));
                            }
                            else
                            {
                                newPath.Add(Tuple.Create(i + 1, j + 1));
                            }
                            queue.Enqueue(Tuple.Create(movingLeft ? curLeft - 1 : curLeft,
                                movingLeft ? curRight : curRight - 1,
                                movingLeft ? nextLeftIndex - 1 : nextLeftIndex,
                                movingLeft ? nextRightIndex : nextRightIndex + 1,
                                movingLeft ? pivotPos + 1 : pivotPos,
                                newUsedArray, newPath));
                        }
                    }
                }

                if (j > 0)
                {
                    if (usedArray[i, j - 1] != 1)
                    {
                        int[,] newUsedArray = (int[,])usedArray.Clone();
                        newUsedArray[i, j - 1] = 1;
                        List<Tuple<int, int>> newPath = new(candidatePath);
                        if (movingLeft)
                        {
                            newPath.Insert(0, Tuple.Create(i, j - 1));
                        }
                        else
                        {
                            newPath.Add(Tuple.Create(i, j - 1));
                        }
                        queue.Enqueue(Tuple.Create(movingLeft ? curLeft - 1 : curLeft,
                            movingLeft ? curRight : curRight - 1,
                            movingLeft ? nextLeftIndex - 1 : nextLeftIndex,
                            movingLeft ? nextRightIndex : nextRightIndex + 1,
                            movingLeft ? pivotPos + 1 : pivotPos,
                            newUsedArray, newPath));
                    }
                }

                if (j < boardArray.GetLength(1) - 1)
                {
                    if (usedArray[i, j + 1] != 1)
                    {
                        int[,] newUsedArray = (int[,])usedArray.Clone();
                        newUsedArray[i, j + 1] = 1;
                        List<Tuple<int, int>> newPath = new(candidatePath);
                        if (movingLeft)
                        {
                            newPath.Insert(0, Tuple.Create(i, j + 1));
                        }
                        else
                        {
                            newPath.Add(Tuple.Create(i, j + 1));
                        }
                        queue.Enqueue(Tuple.Create(movingLeft ? curLeft - 1 : curLeft,
                            movingLeft ? curRight : curRight - 1,
                            movingLeft ? nextLeftIndex - 1 : nextLeftIndex,
                            movingLeft ? nextRightIndex : nextRightIndex + 1,
                            movingLeft ? pivotPos + 1 : pivotPos,
                            newUsedArray, newPath));
                    }
                }
            }

            return alternateWordCount;
        }

        public List<Tuple<int, int>> FindBestWordPath(string word)
        {
            List<Tuple<int, int>> path = new List<Tuple<int, int>>();

            if (string.IsNullOrEmpty(word)) {
                return path;
            }

            Queue<Tuple<int, int, int, int[,], List<Tuple<int, int>>>> queue = new();

            for (int i = 0; i < boardArray.GetLength(0); i++)
            {
                for (int j = 0; j < boardArray.GetLength(1); j++)
                {
                    if (boardArray[i, j] == word[0])
                    {
                        int[,] usedArray = new int[boardArray.GetLength(0), boardArray.GetLength(1)];
                        usedArray[i, j] = 1;
                        List<Tuple<int, int>> candidatePath = new();
                        candidatePath.Add(Tuple.Create(i, j));
                        queue.Enqueue(Tuple.Create(i, j, 0, usedArray, candidatePath));
                    }
                }
            }

            int longestPathIndex = -1;

            while (queue.Count != 0)
            {
                Tuple<int, int, int, int[,], List<Tuple<int, int>>> tuple = queue.Dequeue();

                int i = tuple.Item1;
                int j = tuple.Item2;
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
                    path = candidatePath;
                    return path;
                }

                if (i > 0)
                {
                    if (usedArray[i - 1, j] != 1 && boardArray[i - 1, j] == word[guessIndex + 1])
                    {
                        int[,] newUsedArray = (int[,])usedArray.Clone();
                        newUsedArray[i - 1, j] = 1;
                        List<Tuple<int, int>> newPath = new(candidatePath);
                        newPath.Add(Tuple.Create(i - 1, j));
                        queue.Enqueue(Tuple.Create(i - 1, j, guessIndex + 1, newUsedArray, newPath));
                    }

                    if (j > 0)
                    {
                        if (usedArray[i - 1, j - 1] != 1 && boardArray[i - 1, j - 1] == word[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,])usedArray.Clone();
                            newUsedArray[i - 1, j - 1] = 1;
                            List<Tuple<int, int>> newPath = new(candidatePath);
                            newPath.Add(Tuple.Create(i - 1, j - 1));
                            queue.Enqueue(Tuple.Create(i - 1, j - 1, guessIndex + 1, newUsedArray, newPath));
                        }
                    }

                    if (j < boardArray.GetLength(1) - 1)
                    {
                        if (usedArray[i - 1, j + 1] != 1 && boardArray[i - 1, j + 1] == word[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,])usedArray.Clone();
                            newUsedArray[i - 1, j + 1] = 1;
                            List<Tuple<int, int>> newPath = new(candidatePath);
                            newPath.Add(Tuple.Create(i - 1, j + 1));
                            queue.Enqueue(Tuple.Create(i - 1, j + 1, guessIndex + 1, newUsedArray, newPath));
                        }
                    }
                }

                if (i < boardArray.GetLength(0) - 1)
                {
                    if (usedArray[i + 1, j] != 1 && boardArray[i + 1, j] == word[guessIndex + 1])
                    {
                        int[,] newUsedArray = (int[,])usedArray.Clone();
                        newUsedArray[i + 1, j] = 1;
                        List<Tuple<int, int>> newPath = new(candidatePath);
                        newPath.Add(Tuple.Create(i + 1, j));
                        queue.Enqueue(Tuple.Create(i + 1, j, guessIndex + 1, newUsedArray, newPath));
                    }

                    if (j > 0)
                    {
                        if (usedArray[i + 1, j - 1] != 1 && boardArray[i + 1, j - 1] == word[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,])usedArray.Clone();
                            newUsedArray[i + 1, j - 1] = 1;
                            List<Tuple<int, int>> newPath = new(candidatePath);
                            newPath.Add(Tuple.Create(i + 1, j - 1));
                            queue.Enqueue(Tuple.Create(i + 1, j - 1, guessIndex + 1, newUsedArray, newPath));
                        }
                    }

                    if (j < boardArray.GetLength(1) - 1)
                    {
                        if (usedArray[i + 1, j + 1] != 1 && boardArray[i + 1, j + 1] == word[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,])usedArray.Clone();
                            newUsedArray[i + 1, j + 1] = 1;
                            List<Tuple<int, int>> newPath = new(candidatePath);
                            newPath.Add(Tuple.Create(i + 1, j + 1));
                            queue.Enqueue(Tuple.Create(i + 1, j + 1, guessIndex + 1, newUsedArray, newPath));
                        }
                    }
                }

                if (j > 0)
                {
                    if (usedArray[i, j - 1] != 1 && boardArray[i, j - 1] == word[guessIndex + 1])
                    {
                        int[,] newUsedArray = (int[,])usedArray.Clone();
                        newUsedArray[i, j - 1] = 1;
                        List<Tuple<int, int>> newPath = new(candidatePath);
                        newPath.Add(Tuple.Create(i, j - 1));
                        queue.Enqueue(Tuple.Create(i, j - 1, guessIndex + 1, newUsedArray, newPath));
                    }
                }

                if (j < boardArray.GetLength(1) - 1)
                {
                    if (usedArray[i, j + 1] != 1 && boardArray[i, j + 1] == word[guessIndex + 1])
                    {
                        int[,] newUsedArray = (int[,])usedArray.Clone();
                        newUsedArray[i, j + 1] = 1;
                        List<Tuple<int, int>> newPath = new(candidatePath);
                        newPath.Add(Tuple.Create(i, j + 1));
                        queue.Enqueue(Tuple.Create(i, j + 1, guessIndex + 1, newUsedArray, newPath));
                    }
                }
            }

            return path;
        }

        public bool TryFindWordPath(string word, out List<Tuple<int, int>> path)
        {
            path = null;
            if (string.IsNullOrEmpty(word))
            {
                return false;
            }

            Queue<Tuple<int, int, int, int[,], List<Tuple<int, int>>>> queue = new();
            for (int i = 0; i < boardArray.GetLength(0); i++)
            {
                for (int j = 0; j < boardArray.GetLength(1); j++)
                {
                    if (boardArray[i, j] == word[0])
                    {
                        int[,] usedArray = new int[boardArray.GetLength(0), boardArray.GetLength(1)];
                        usedArray[i, j] = 1;
                        List<Tuple<int, int>> candidatePath = new();
                        candidatePath.Add(Tuple.Create(i, j));
                        queue.Enqueue(Tuple.Create(i, j, 0, usedArray, candidatePath));
                    }
                }
            }

            while (queue.Count != 0)
            {
                Tuple<int, int, int, int[,], List<Tuple<int, int>>> tuple = queue.Dequeue();

                int i = tuple.Item1;
                int j = tuple.Item2;
                int guessIndex = tuple.Item3;
                int[,] usedArray = tuple.Item4;
                List<Tuple<int, int>> candidatePath = tuple.Item5;

                if (guessIndex == word.Length - 1)
                {
                    path = candidatePath;
                    return true;
                }

                if (i > 0)
                {
                    if (usedArray[i - 1, j] != 1 && boardArray[i - 1, j] == word[guessIndex + 1])
                    {
                        int[,] newUsedArray = (int[,])usedArray.Clone();
                        newUsedArray[i - 1, j] = 1;
                        List<Tuple<int, int>> newPath = new(candidatePath);
                        newPath.Add(Tuple.Create(i - 1, j));
                        queue.Enqueue(Tuple.Create(i - 1, j, guessIndex + 1, newUsedArray, newPath));
                    }

                    if (j > 0)
                    {
                        if (usedArray[i - 1, j - 1] != 1 && boardArray[i - 1, j - 1] == word[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,])usedArray.Clone();
                            newUsedArray[i - 1, j - 1] = 1;
                            List<Tuple<int, int>> newPath = new(candidatePath);
                            newPath.Add(Tuple.Create(i - 1, j - 1));
                            queue.Enqueue(Tuple.Create(i - 1, j - 1, guessIndex + 1, newUsedArray, newPath));
                        }
                    }

                    if (j < boardArray.GetLength(1) - 1)
                    {
                        if (usedArray[i - 1, j + 1] != 1 && boardArray[i - 1, j + 1] == word[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,])usedArray.Clone();
                            newUsedArray[i - 1, j + 1] = 1;
                            List<Tuple<int, int>> newPath = new(candidatePath);
                            newPath.Add(Tuple.Create(i - 1, j + 1));
                            queue.Enqueue(Tuple.Create(i - 1, j + 1, guessIndex + 1, newUsedArray, newPath));
                        }
                    }
                }

                if (i < boardArray.GetLength(0) - 1)
                {
                    if (usedArray[i + 1, j] != 1 && boardArray[i + 1, j] == word[guessIndex + 1])
                    {
                        int[,] newUsedArray = (int[,])usedArray.Clone();
                        newUsedArray[i + 1, j] = 1;
                        List<Tuple<int, int>> newPath = new(candidatePath);
                        newPath.Add(Tuple.Create(i + 1, j));
                        queue.Enqueue(Tuple.Create(i + 1, j, guessIndex + 1, newUsedArray, newPath));
                    }

                    if (j > 0)
                    {
                        if (usedArray[i + 1, j - 1] != 1 && boardArray[i + 1, j - 1] == word[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,])usedArray.Clone();
                            newUsedArray[i + 1, j - 1] = 1;
                            List<Tuple<int, int>> newPath = new(candidatePath);
                            newPath.Add(Tuple.Create(i + 1, j - 1));
                            queue.Enqueue(Tuple.Create(i + 1, j - 1, guessIndex + 1, newUsedArray, newPath));
                        }
                    }

                    if (j < boardArray.GetLength(1) - 1)
                    {
                        if (usedArray[i + 1, j + 1] != 1 && boardArray[i + 1, j + 1] == word[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,])usedArray.Clone();
                            newUsedArray[i + 1, j + 1] = 1;
                            List<Tuple<int, int>> newPath = new(candidatePath);
                            newPath.Add(Tuple.Create(i + 1, j + 1));
                            queue.Enqueue(Tuple.Create(i + 1, j + 1, guessIndex + 1, newUsedArray, newPath));
                        }
                    }
                }

                if (j > 0)
                {
                    if (usedArray[i, j - 1] != 1 && boardArray[i, j - 1] == word[guessIndex + 1])
                    {
                        int[,] newUsedArray = (int[,])usedArray.Clone();
                        newUsedArray[i, j - 1] = 1;
                        List<Tuple<int, int>> newPath = new(candidatePath);
                        newPath.Add(Tuple.Create(i, j - 1));
                        queue.Enqueue(Tuple.Create(i, j - 1, guessIndex + 1, newUsedArray, newPath));
                    }
                }

                if (j < boardArray.GetLength(1) - 1)
                {
                    if (usedArray[i, j + 1] != 1 && boardArray[i, j + 1] == word[guessIndex + 1])
                    {
                        int[,] newUsedArray = (int[,])usedArray.Clone();
                        newUsedArray[i, j + 1] = 1;
                        List<Tuple<int, int>> newPath = new(candidatePath);
                        newPath.Add(Tuple.Create(i, j + 1));
                        queue.Enqueue(Tuple.Create(i, j + 1, guessIndex + 1, newUsedArray, newPath));
                    }
                }
            }

            return false;
        }

        public Board Mutate()
        {
            int index = Random.Next(16);
            int row = index / 4;
            int col = index % 4;
            boardArray[row, col] = GetRandomWeightedLetter();

            return this;
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
