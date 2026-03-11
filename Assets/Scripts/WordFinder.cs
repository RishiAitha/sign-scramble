using UnityEngine;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;

/*
    This script takes in user input for fingerspelling and finds words in grid.
*/

public class WordFinder : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI toggleText;
    [SerializeField] Button toggleButton;
    [SerializeField] TextMeshProUGUI wordDisplayText;
    [SerializeField] TextMeshProUGUI scoreText;
    [SerializeField] GameManager gameManager;
    [SerializeField] DictionaryManager dictionaryManager;

    private bool isListening = false;
    private string currentGuess;
    private int score = 0;
    private List<Tuple<int, int>> currentPath; // Stores the path of a valid word

    void Start()
    {
        ResetState();
    }
    
    // Public method to reset state (called on game start/restart)
    public void ResetState()
    {
        score = 0;
        currentGuess = "";
        isListening = false;
        
        if (scoreText != null)
        {
            scoreText.text = "0";
        }
        if (wordDisplayText != null)
        {
            wordDisplayText.text = "";
        }
        if (toggleText != null)
        {
            toggleText.text = "Start Signing";
        }
        if (toggleButton != null)
        {
            toggleButton.interactable = true;
        }
    }

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
            toggleText.text = "Start Signing";
            SubmitWord();
        }
        else
        {
            toggleText.text = "Stop Signing";
        }
    }

    private void SubmitWord()
    {
        Debug.Log("Submitted Guess: " + currentGuess);
        
        bool wordFound = false;
        currentPath = null;

        // First check if word exists in dictionary
        if (currentGuess.Length > 0 && dictionaryManager != null && !dictionaryManager.IsValidWord(currentGuess))
        {
            Debug.Log("Word not in dictionary: " + currentGuess);
            StartCoroutine(DisplayWordResult(currentGuess, false));
            return;
        }
        
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
                        List<Tuple<int, int>> path = new List<Tuple<int, int>>();
                        path.Add(Tuple.Create(i, j));
                        queue.Enqueue(Tuple.Create(i, j, 0, usedArray, path));
                    }
                }
            }

            while (queue.Count != 0)
            {
                Tuple<int, int, int, int[,], List<Tuple<int, int>>> tuple = (Tuple<int, int, int, int[,], List<Tuple<int, int>>>) queue.Dequeue();

                int i = tuple.Item1;
                int j = tuple.Item2;
                int guessIndex = tuple.Item3;
                int[,] usedArray = tuple.Item4;
                List<Tuple<int, int>> path = tuple.Item5;

                if (guessIndex == currentGuess.Length - 1)
                {
                    Debug.Log("Word Found!");
                    wordFound = true;
                    currentPath = path;
                    break;
                }
                else
                {
                    if (i > 0)
                    {
                        if (usedArray[i - 1, j] != 1 && boardArray[i - 1, j] == currentGuess[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,]) usedArray.Clone();
                            newUsedArray[i - 1, j] = 1;
                            List<Tuple<int, int>> newPath = new List<Tuple<int, int>>(path);
                            newPath.Add(Tuple.Create(i - 1, j));
                            queue.Enqueue(Tuple.Create(i - 1, j, guessIndex + 1, newUsedArray, newPath));
                        } 

                        if (j > 0)
                        {
                            if (usedArray[i - 1, j - 1] != 1 && boardArray[i - 1, j - 1] == currentGuess[guessIndex + 1])
                            {
                                int[,] newUsedArray = (int[,]) usedArray.Clone();
                                newUsedArray[i - 1, j - 1] = 1;
                                List<Tuple<int, int>> newPath = new List<Tuple<int, int>>(path);
                                newPath.Add(Tuple.Create(i - 1, j - 1));
                                queue.Enqueue(Tuple.Create(i - 1, j - 1, guessIndex + 1, newUsedArray, newPath));
                            }
                        }

                        if (j < boardArray.GetLength(1) - 1)
                        {
                            if (usedArray[i - 1, j + 1] != 1 && boardArray[i - 1, j + 1] == currentGuess[guessIndex + 1])
                            {
                                int[,] newUsedArray = (int[,]) usedArray.Clone();
                                newUsedArray[i - 1, j + 1] = 1;
                                List<Tuple<int, int>> newPath = new List<Tuple<int, int>>(path);
                                newPath.Add(Tuple.Create(i - 1, j + 1));
                                queue.Enqueue(Tuple.Create(i - 1, j + 1, guessIndex + 1, newUsedArray, newPath));
                            }
                        }
                    }

                    if (i < boardArray.GetLength(0) - 1)
                    {
                        if (usedArray[i + 1, j] != 1 && boardArray[i + 1, j] == currentGuess[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,]) usedArray.Clone();
                            newUsedArray[i + 1, j] = 1;
                            List<Tuple<int, int>> newPath = new List<Tuple<int, int>>(path);
                            newPath.Add(Tuple.Create(i + 1, j));
                            queue.Enqueue(Tuple.Create(i + 1, j, guessIndex + 1, newUsedArray, newPath));
                        }

                        if (j > 0)
                        {
                            if (usedArray[i + 1, j - 1] != 1 && boardArray[i + 1, j - 1] == currentGuess[guessIndex + 1])
                            {
                                int[,] newUsedArray = (int[,]) usedArray.Clone();
                                newUsedArray[i + 1, j - 1] = 1;
                                List<Tuple<int, int>> newPath = new List<Tuple<int, int>>(path);
                                newPath.Add(Tuple.Create(i + 1, j - 1));
                                queue.Enqueue(Tuple.Create(i + 1, j - 1, guessIndex + 1, newUsedArray, newPath));
                            }
                        }

                        if (j < boardArray.GetLength(1) - 1)
                        {
                            if (usedArray[i + 1, j + 1] != 1 && boardArray[i + 1, j + 1] == currentGuess[guessIndex + 1])
                            {
                                int[,] newUsedArray = (int[,]) usedArray.Clone();
                                newUsedArray[i + 1, j + 1] = 1;
                                List<Tuple<int, int>> newPath = new List<Tuple<int, int>>(path);
                                newPath.Add(Tuple.Create(i + 1, j + 1));
                                queue.Enqueue(Tuple.Create(i + 1, j + 1, guessIndex + 1, newUsedArray, newPath));
                            }
                        }
                    }

                    if (j > 0)
                    {
                        if (usedArray[i, j - 1] != 1 && boardArray[i, j - 1] == currentGuess[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,]) usedArray.Clone();
                            newUsedArray[i, j - 1] = 1;
                            List<Tuple<int, int>> newPath = new List<Tuple<int, int>>(path);
                            newPath.Add(Tuple.Create(i, j - 1));
                            queue.Enqueue(Tuple.Create(i, j - 1, guessIndex + 1, newUsedArray, newPath));
                        }
                    }

                    if (j < boardArray.GetLength(1) - 1)
                    {
                        if (usedArray[i, j + 1] != 1 && boardArray[i, j + 1] == currentGuess[guessIndex + 1])
                        {
                            int[,] newUsedArray = (int[,]) usedArray.Clone();
                            newUsedArray[i, j + 1] = 1;
                            List<Tuple<int, int>> newPath = new List<Tuple<int, int>>(path);
                            newPath.Add(Tuple.Create(i, j + 1));
                            queue.Enqueue(Tuple.Create(i, j + 1, guessIndex + 1, newUsedArray, newPath));
                        }
                    }
                }
            }
        }
        // Start the word display animation
        StartCoroutine(DisplayWordResult(currentGuess, wordFound));
    }

    private IEnumerator DisplayWordResult(string word, bool isValid)
    {
        // Disable the toggle button
        if (toggleButton != null)
        {
            toggleButton.interactable = false;
        }

        BoardGenerator boardGen = GetComponent<BoardGenerator>();

        // Display word letter by letter in neutral color
        if (wordDisplayText != null)
        {
            wordDisplayText.text = "";
            wordDisplayText.color = new Color32(0x66, 0x26, 0x00, 0xFF); // Neutral color #662600
            
            for (int i = 0; i < word.Length; i++)
            {
                wordDisplayText.text += word[i];
                
                // Show arrow if this is a valid word and not the last letter
                if (isValid && currentPath != null && i < word.Length - 1)
                {
                    int arrowIndex = GetArrowIndex(currentPath[i], currentPath[i + 1]);
                    Letter letter = boardGen.GetLetterAt(currentPath[i].Item1, currentPath[i].Item2);
                    if (letter != null && arrowIndex >= 0)
                    {
                        letter.arrows[arrowIndex].SetActive(true);
                    }
                }
                
                yield return new WaitForSeconds(0.1f); // Add each letter with a delay
            }
        }

        // Wait a moment before changing color
        yield return new WaitForSeconds(0.3f);

        // Change color based on validity and sync to network IMMEDIATELY
        if (wordDisplayText != null)
        {
            if (isValid)
            {
                // Green for valid word
                wordDisplayText.color = new Color32(0x37, 0xC4, 0x43, 0xFF); // #37C443
                
                // IMMEDIATELY sync score and word to network (before animation)
                int totalWordScore = CalculateWordScore(word.Length);
                if (gameManager != null)
                {
                    gameManager.AddScore(totalWordScore);
                    gameManager.AddCompletedWord(word);
                }
            }
            else
            {
                // Red for invalid word
                wordDisplayText.color = new Color32(0xD7, 0x09, 0x08, 0xFF); // #D70908
            }
        }

        // Wait before clearing
        yield return new WaitForSeconds(0.5f);

        // If valid, play inverse animation and score points
        if (isValid && wordDisplayText != null && currentPath != null)
        {
            // Remove letters right to left with scoring
            // Scoring based on Boggle system, scaled up for engagement:
            // 3-letter: 9,000 | 4-letter: 20,000 | 5-letter: 40,000
            // 6-letter: 78,000 | 7-letter: 140,000 | 8+: 280,000+
            int totalWordScore = CalculateWordScore(word.Length);
            int pointsPerLetter = totalWordScore / word.Length;
            
            for (int i = word.Length - 1; i >= 0; i--)
            {
                // Remove the letter from display first
                wordDisplayText.text = word.Substring(0, i);
                
                // Hide arrow for this position if not the first letter
                if (i > 0)
                {
                    int arrowIndex = GetArrowIndex(currentPath[i - 1], currentPath[i]);
                    Letter letter = boardGen.GetLetterAt(currentPath[i - 1].Item1, currentPath[i - 1].Item2);
                    if (letter != null && arrowIndex >= 0)
                    {
                        letter.arrows[arrowIndex].SetActive(false);
                    }
                }
                
                yield return new WaitForSeconds(0.1f);
                
                // Then animate score counting up for this letter (fixed duration)
                int targetScore = score + pointsPerLetter;
                yield return StartCoroutine(AnimateScore(targetScore, 0.4f));
            }
        }
        else if (!isValid)
        {
            // For invalid words, just wait and clear
            yield return new WaitForSeconds(1.0f);
            
            // Clear the word display
            if (wordDisplayText != null)
            {
                wordDisplayText.text = "";
            }
        }

        // Re-enable the toggle button
        if (toggleButton != null)
        {
            toggleButton.interactable = true;
        }

        // Clear current guess for next word
        if (!isValid)
        {
            Debug.Log("Word not found.");
        }

        currentGuess = "";
    }

    // Calculate total score for a word based on its length (Boggle-inspired, scaled up)
    private int CalculateWordScore(int wordLength)
    {
        switch (wordLength)
        {
            case 3: return 9000;    // 3000 per letter
            case 4: return 20000;   // 5000 per letter
            case 5: return 40000;   // 8000 per letter
            case 6: return 78000;   // 13000 per letter
            case 7: return 140000;  // 20000 per letter
            case 8: return 280000;  // 35000 per letter
            case 9: return 450000;  // 50000 per letter
            default: return wordLength >= 10 ? 1000000 : 9000; // 10+ letters = 1M!
        }
    }

    // Animate the score counting up to the target value over a fixed duration
    private IEnumerator AnimateScore(int targetScore, float duration)
    {
        if (scoreText == null) 
        {
            score = targetScore;
            yield break;
        }

        int difference = targetScore - score;
        if (difference <= 0)
        {
            score = targetScore;
            scoreText.text = score.ToString();
            yield break;
        }

        // Calculate increment size based on duration
        // We want smooth increments divisible by 10
        int totalIncrements = Mathf.Max(1, (int)(duration / 0.02f)); // 0.02s per frame
        int increment = Mathf.Max(10, (difference / totalIncrements / 10) * 10); // Round to nearest 10
        float delay = duration / (difference / (float)increment);
        
        while (score < targetScore)
        {
            score += increment;
            if (score > targetScore) score = targetScore;
            
            scoreText.text = score.ToString();
            yield return new WaitForSeconds(delay);
        }
    }

    // Calculate which arrow to show based on direction from pos1 to pos2
    // Arrow indices: 0=right, 1=up-right, 2=up, 3=up-left, 4=left, 5=down-left, 6=down, 7=down-right
    private int GetArrowIndex(Tuple<int, int> pos1, Tuple<int, int> pos2)
    {
        int di = pos2.Item1 - pos1.Item1; // row difference
        int dj = pos2.Item2 - pos1.Item2; // column difference

        if (di == 0 && dj == 1) return 0;      // Right
        if (di == -1 && dj == 1) return 1;     // Up-Right
        if (di == -1 && dj == 0) return 2;     // Up
        if (di == -1 && dj == -1) return 3;    // Up-Left
        if (di == 0 && dj == -1) return 4;     // Left
        if (di == 1 && dj == -1) return 5;     // Down-Left
        if (di == 1 && dj == 0) return 6;      // Down
        if (di == 1 && dj == 1) return 7;      // Down-Right

        return -1; // Invalid direction
    }
}
