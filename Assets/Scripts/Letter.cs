using UnityEngine;
using TMPro;

/*
    Stores letter displayed in letter object.
*/

public class Letter : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI letterText;

    public char letter;

    void Start()
    {
        if (letter < 'A' || letter > 'Z')
        {
            letter = '-';
        }

        letterText.text = letter.ToString();
    }

    public void changeLetter(char newLetter)
    {
        letter = newLetter;

        if (letter < 'A' || letter > 'Z')
        {
            letter = '-';
        }

        letterText.text = letter.ToString();
    }
}
