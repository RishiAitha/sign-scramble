using UnityEngine;
using System.Linq;

/*
    This script generates a 4x4 board of letters containing words.
*/

public class BoardGenerator : MonoBehaviour
{
    [SerializeField] GameObject letterPrefab;

    [SerializeField] bool usingTest;

    private char[,] boardArray;
    private Letter[,] letterObjects; // Store references to Letter components
    private bool isInitialized;
    private static readonly int[] LetterCounts = { 118838, 28836, 64177, 53136, 180794, 19335, 42424, 36493, 141463, 2497, 13309, 83427, 44529, 107519, 103536, 46141, 2535, 111061, 149453, 104965, 51299, 15363, 11689, 4610, 25637, 7442 };
    private static readonly int TotalLetterCount = LetterCounts.Sum();
    private static readonly float[] Weights = LetterCounts.Select(count => count / (float)TotalLetterCount).ToArray();

    void Awake()
    {
        InitializeBoardObjects();
    }

    void Start()
    {
        if (usingTest)
        {
            SetBoardFromString("ISPLGTELNRTODCOW");
        }
    }

    public string GenerateBoardString()
    {
        char[] letters = new char[16];
        for (int i = 0; i < 16; i++)
        {
            int letterIndex = GetRandomLetter(Weights);
            letters[i] = (char)('A' + letterIndex);
        }
        return new string(letters);
    }

    public void SetBoardFromString(string board)
    {
        if (string.IsNullOrEmpty(board)) return;
        InitializeBoardObjects();

        for (int i = 0; i < 16; i++)
        {
            int row = i / 4;
            int col = i % 4;
            char c = i < board.Length ? char.ToUpperInvariant(board[i]) : 'A';
            boardArray[row, col] = c;
            letterObjects[row, col].changeLetter(c);
        }
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
            boardArray = new char[4, 4];
            letterObjects = new Letter[4, 4];

            for (int i = 0; i < 16; i++)
            {
                int row = i / 4;
                int col = i % 4;
                boardArray[row, col] = 'A';

                GameObject letterObject = Instantiate(letterPrefab);
                letterObject.transform.SetParent(gameObject.transform);
                letterObjects[row, col] = letterObject.GetComponent<Letter>();
                letterObjects[row, col].changeLetter('A');
                foreach (GameObject arrow in letterObjects[row, col].arrows)
                {
                    arrow.SetActive(false);
                }
            }

            isInitialized = true;
        }
    }

    private int GetRandomLetter(float[] weights)
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

    public char[,] GetBoardArray()
    {
        return boardArray;
    }

    public Letter GetLetterAt(int row, int col)
    {
        if (row >= 0 && row < 4 && col >= 0 && col < 4)
        {
            return letterObjects[row, col];
        }
        return null;
    }
}
