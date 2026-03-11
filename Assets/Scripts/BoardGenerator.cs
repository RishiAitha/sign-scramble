using UnityEngine;

/*
    This script generates a 4x4 board of letters containing words.
*/

public class BoardGenerator : MonoBehaviour
{
    [SerializeField] GameObject letterPrefab;

    [SerializeField] bool usingTest;

    private char[,] boardArray;
    private Letter[,] letterObjects; // Store references to Letter components

    void Start()
    {
        boardArray = new char[4,4];
        letterObjects = new Letter[4,4];

        char[] test = {'I', 'S', 'P', 'L', 'G', 'T', 'E', 'L', 'N', 'R', 'T', 'O', 'D', 'C', 'O', 'W'};

        for (int i = 0; i < 16; i++)
        {
            GameObject letter = Instantiate(letterPrefab);
            letter.transform.SetParent(gameObject.transform);

            int row = i / 4;
            int col = i % 4;
            letterObjects[row, col] = letter.GetComponent<Letter>();

            if (usingTest)
            {
                letter.GetComponent<Letter>().changeLetter(test[i]);
                boardArray[row, col] = test[i];
            }
            else
            {
                // This will be updated
                letter.GetComponent<Letter>().changeLetter('A');
                boardArray[row, col] = 'A';
            }
        }
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
