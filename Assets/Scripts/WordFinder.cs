using UnityEngine;
using TMPro;
using System;
using System.Collections;

/*
    This script takes in user input for fingerspelling and finds words in grid.
*/

public class WordFinder : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI toggleText;

    private Boolean isListening = false;
    private string currentGuess;

    void Update()
    {
        if (isListening)
        {
            currentGuess += Input.inputString.ToUpper();
        }
    }

    public void ToggleListening()
    {
        isListening = !isListening;

        if (!isListening)
        {
            toggleText.text = "Start";
            SubmitWord();
        }
        else
        {
            toggleText.text = "Stop";
        }
    }

    private void SubmitWord()
    {
        Debug.Log("Submitted Guess: " + currentGuess);
        char[,] boardArray = GetComponent<BoardGenerator>().GetBoardArray();

        if (currentGuess.Length > 0)
        {
            Queue queue = new Queue();
            for (int i = 0; i < boardArray.GetLength(0); i++)
            {
                for (int j = 0; j < boardArray.GetLength(1); j++)
                {
                    if (boardArray[i, j] == currentGuess[0])
                    {
                        int[,] usedArray = new int[boardArray.GetLength(0), boardArray.GetLength(1)];
                        usedArray[i, j] = 1;
                        queue.Enqueue(Tuple.Create(i, j, 0, usedArray));
                    }
                }
            }

            while (queue.Count != 0)
            {
                Tuple<int, int, int, int[,]> tuple = (Tuple<int, int, int, int[,]>) queue.Dequeue();

                int i = tuple.Item1;
                int j = tuple.Item2;
                int guessIndex = tuple.Item3;
                int[,] usedArray = tuple.Item4;

                if (guessIndex == currentGuess.Length - 1)
                {
                    Debug.Log("Word Found!");
                    currentGuess = "";
                    return;
                }
                else
                {
                    if (i > 0)
                    {
                        if (usedArray[i - 1, j] != 1 && boardArray[i - 1, j] == currentGuess[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,]) usedArray.Clone();
                            newUsedArray[i - 1, j] = 1;
                            queue.Enqueue(Tuple.Create(i - 1, j, guessIndex + 1, newUsedArray));
                        } 

                        if (j > 0)
                        {
                            if (usedArray[i - 1, j - 1] != 1 && boardArray[i - 1, j - 1] == currentGuess[guessIndex + 1])
                            {
                                int[,] newUsedArray = (int[,]) usedArray.Clone();
                                newUsedArray[i - 1, j - 1] = 1;
                                queue.Enqueue(Tuple.Create(i - 1, j - 1, guessIndex + 1, newUsedArray));
                            }
                        }

                        if (j < boardArray.GetLength(1) - 1)
                        {
                            if (usedArray[i - 1, j + 1] != 1 && boardArray[i - 1, j + 1] == currentGuess[guessIndex + 1])
                            {
                                int[,] newUsedArray = (int[,]) usedArray.Clone();
                                newUsedArray[i - 1, j + 1] = 1;
                                queue.Enqueue(Tuple.Create(i - 1, j + 1, guessIndex + 1, newUsedArray));
                            }
                        }
                    }

                    if (i < boardArray.GetLength(0) - 1)
                    {
                        if (usedArray[i + 1, j] != 1 && boardArray[i + 1, j] == currentGuess[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,]) usedArray.Clone();
                            newUsedArray[i + 1, j] = 1;
                            queue.Enqueue(Tuple.Create(i + 1, j, guessIndex + 1, newUsedArray));
                        }

                        if (j > 0)
                        {
                            if (usedArray[i + 1, j - 1] != 1 && boardArray[i + 1, j - 1] == currentGuess[guessIndex + 1])
                            {
                                int[,] newUsedArray = (int[,]) usedArray.Clone();
                                newUsedArray[i + 1, j - 1] = 1;
                                queue.Enqueue(Tuple.Create(i + 1, j - 1, guessIndex + 1, newUsedArray));
                            }
                        }

                        if (j < boardArray.GetLength(1) - 1)
                        {
                            if (usedArray[i + 1, j + 1] != 1 && boardArray[i + 1, j + 1] == currentGuess[guessIndex + 1])
                            {
                                int[,] newUsedArray = (int[,]) usedArray.Clone();
                                newUsedArray[i + 1, j + 1] = 1;
                                queue.Enqueue(Tuple.Create(i + 1, j + 1, guessIndex + 1, newUsedArray));
                            }
                        }
                    }

                    if (j > 0)
                    {
                        if (usedArray[i, j - 1] != 1 && boardArray[i, j - 1] == currentGuess[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,]) usedArray.Clone();
                            newUsedArray[i, j - 1] = 1;
                            queue.Enqueue(Tuple.Create(i, j - 1, guessIndex + 1, newUsedArray));
                        }
                    }

                    if (j < boardArray.GetLength(1) - 1)
                    {
                        if (usedArray[i, j + 1] != 1 && boardArray[i, j + 1] == currentGuess[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,]) usedArray.Clone();
                            newUsedArray[i, j + 1] = 1;
                            queue.Enqueue(Tuple.Create(i, j + 1, guessIndex + 1, newUsedArray));
                        }
                    }
                }
            }
        }
        else
        {
            Debug.Log("No guess submitted.");
        }

        Debug.Log("Word not found.");

        currentGuess = "";
    }
}
