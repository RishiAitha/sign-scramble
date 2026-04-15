using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

/*
    This script generates a 4x4 board of letters containing words.
*/

public class BoardGenerator : MonoBehaviour
{
    [SerializeField] GameObject letterPrefab;

    [SerializeField] bool usingTest;
    private Board currentBoard;
    private Letter[,] letterObjects; // Store references to Letter components
    private bool isInitialized;

    void Awake()
    {
        InitializeBoardObjects();
    }

    void Start()
    {
        if (usingTest)
        {
            GenerateBoards(new[] { "CLOCK", "DOOR", "ROD", "SIGN", "CROOKS" });
        }
        else
        {
            throw new Exception("Need to use test words for now."); // TODO: set up word sets
        }
    }

    public string[] GenerateBoards(string[] words)
    {
        if (!usingTest) throw new Exception("Need to use test for now."); // TODO: allow word sets
        if (!ValidateWordSet(words))
        {
            throw new Exception("Invalid word set.");
        }

        // TODO: create boards until a bunch have all the words in the set

        // TODO: find rarest letters and mark them as displayed

        // TODO: loop condition

            // TODO: make board generation respond to scoring

            // TODO: find where to start displaying more letters if stuck

        return null;
    }

    public float ScoreBoardOnWords(Board board, string[] words)
    {
        // TODO: check if every word exists in board

        return -1;
    }

    public float ScoreBoardOnAlternatives(Board board, string[] words, char[] displayed)
    {
        // TODO: search for alternative words of same length in dictionary from every displayed letter

        // TODO: decide scoring function

        return -1;
    }

    public bool ValidateWordSet(string[] words)
    {
        int uniqueLetters = 0;
        HashSet<char> chars = new HashSet<>();
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
                foreach (GameObject arrow in letterObjects[row,col].arrows)
                {
                    arrow.SetActive(false);
                }
            }
        }
        else
        {
            currentBoard = new Board();
            letterObjects = new Letter[4, 4];

            for (int i = 0; i < 16; i++)
            {
                int row = i / 4;
                int col = i % 4;

                GameObject letterObject = Instantiate(letterPrefab);
                letterObject.transform.SetParent(gameObject.transform);
                letterObjects[row, col] = letterObject.GetComponent<Letter>();
                letterObjects[row, col].changeLetter(currentBoard.get(row, col));
                foreach (GameObject arrow in letterObjects[row, col].arrows)
                {
                    arrow.SetActive(false);
                }
            }

            isInitialized = true;
        }
    }

    public Letter GetLetterAt(int row, int col)
    {
        if (row >= 0 && row < 4 && col >= 0 && col < 4)
        {
            return letterObjects[row, col];
        }
        return null;
    }

    public class Board
    {
        private char[,] boardArray;
        private static readonly (int dr, int dc)[] NeighborDirections =
        {
            (-1, -1), (-1, 0), (-1, 1),
            (0, -1),           (0, 1),
            (1, -1),  (1, 0),  (1, 1),
        };
        private static readonly int[] LetterCounts = { 118838, 28836, 64177, 53136, 180794, 19335, 42424, 36493, 141463, 2497, 13309, 83427, 44529, 107519, 103536, 46141, 2535, 111061, 149453, 104965, 51299, 15363, 11689, 4610, 25637, 7442 };
        private static readonly int TotalLetterCount = LetterCounts.Sum();
        private static readonly float[] Weights = LetterCounts.Select(count => count / (float)TotalLetterCount).ToArray();

        public static string GenerateRandomWeightedBoardString()
        {
            char[] letters = new char[16];
            for (int i = 0; i < 16; i++)
            {
                int letterIndex = GetRandomWeightedLetter(Weights);
                letters[i] = (char)('A' + letterIndex);
            }
            return new string(letters);
        }

        public static Board GenerateRandomWeightedBoard()
        {
            return new Board(GenerateRandomWeightedBoardString());
        }

        private static int GetRandomWeightedLetter(float[] weights)
        {
            float roll = Random.value;
            float runningTotal = 0f;

            for (int i = 0; i < weights.Length; i++)
            {
                runningTotal += weights[i];
                if (roll <= runningTotal)
                {
                    return i;
                }
            }

            return weights.Length - 1;
        }

        public bool TryFindWordPath(string word, out List<Tuple<int, int>> path)
        {
            path = null;
            if (string.IsNullOrEmpty(word))
            {
                return false;
            }

            Queue<Tuple<int, int, int, int[,], List<Tuple<int, int>>>> queue = new Queue<Tuple<int, int, int, int[,], List<Tuple<int, int>>>>();
            for (int i = 0; i < boardArray.GetLength(0); i++)
            {
                for (int j = 0; j < boardArray.GetLength(1); j++)
                {
                    if (boardArray[i, j] == word[0])
                    {
                        int[,] usedArray = new int[boardArray.GetLength(0), boardArray.GetLength(1)];
                        usedArray[i, j] = 1;
                        List<Tuple<int, int>> candidatePath = new List<Tuple<int, int>>();
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
                        List<Tuple<int, int>> newPath = new List<Tuple<int, int>>(candidatePath);
                        newPath.Add(Tuple.Create(i - 1, j));
                        queue.Enqueue(Tuple.Create(i - 1, j, guessIndex + 1, newUsedArray, newPath));
                    }

                    if (j > 0)
                    {
                        if (usedArray[i - 1, j - 1] != 1 && boardArray[i - 1, j - 1] == word[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,])usedArray.Clone();
                            newUsedArray[i - 1, j - 1] = 1;
                            List<Tuple<int, int>> newPath = new List<Tuple<int, int>>(candidatePath);
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
                            List<Tuple<int, int>> newPath = new List<Tuple<int, int>>(candidatePath);
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
                        List<Tuple<int, int>> newPath = new List<Tuple<int, int>>(candidatePath);
                        newPath.Add(Tuple.Create(i + 1, j));
                        queue.Enqueue(Tuple.Create(i + 1, j, guessIndex + 1, newUsedArray, newPath));
                    }

                    if (j > 0)
                    {
                        if (usedArray[i + 1, j - 1] != 1 && boardArray[i + 1, j - 1] == word[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,])usedArray.Clone();
                            newUsedArray[i + 1, j - 1] = 1;
                            List<Tuple<int, int>> newPath = new List<Tuple<int, int>>(candidatePath);
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
                            List<Tuple<int, int>> newPath = new List<Tuple<int, int>>(candidatePath);
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
                        List<Tuple<int, int>> newPath = new List<Tuple<int, int>>(candidatePath);
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
                        List<Tuple<int, int>> newPath = new List<Tuple<int, int>>(candidatePath);
                        newPath.Add(Tuple.Create(i, j + 1));
                        queue.Enqueue(Tuple.Create(i, j + 1, guessIndex + 1, newUsedArray, newPath));
                    }
                }
            }

            return false;
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

        public Board()
        {
            this("AAAAAAAAAAAAAAAA");
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
