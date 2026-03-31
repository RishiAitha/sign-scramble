using UnityEngine;
using TMPro;

/*
    Stores letter displayed in letter object.
*/

public class Letter : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI letterText;

    public GameObject[] arrows;

    public char letter;

    void Start()
    {
        if (letter < 'A' || letter > 'Z')
        {
            letter = '-';
        }

        letterText.text = letter.ToString();
        transform.localScale = Vector2.one;
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
