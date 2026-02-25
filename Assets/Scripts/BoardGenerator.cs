using UnityEngine;

/*
    This script generates a 4x4 board of letters containing words.
*/

public class BoardGenerator : MonoBehaviour
{
    [SerializeField] GameObject letterPrefab;

    [SerializeField] bool usingTest;

    private char[,] boardArray;

    void Start()
    {
        boardArray = new char[4,4];

        char[] test = {'I', 'S', 'P', 'L', 'G', 'T', 'E', 'L', 'N', 'R', 'T', 'O', 'D', 'C', 'O', 'W'};

        for (int i = 0; i < 16; i++)
        {
            GameObject letter = Instantiate(letterPrefab);
            letter.transform.SetParent(gameObject.transform);

            if (usingTest)
            {
                letter.GetComponent<Letter>().changeLetter(test[i]);
                boardArray[i / 4, i % 4] = test[i];
            }
            else
            {
                // This will be updated
                letter.GetComponent<Letter>().changeLetter('A');
                boardArray[i / 4, i % 4] = 'A';
            }
        }
    }

    public char[,] GetBoardArray()
    {
        return boardArray;
    }
}
