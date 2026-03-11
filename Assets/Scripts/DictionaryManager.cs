using UnityEngine;
using System.Collections.Generic;

public class DictionaryManager : MonoBehaviour
{
    private HashSet<string> validWords = new HashSet<string>();
    
    void Start()
    {
        LoadDictionary();
    }
    
    void LoadDictionary()
    {
        // Put dictionary.txt in Resources folder
        TextAsset dictionaryFile = Resources.Load<TextAsset>("dictionary");
        string[] words = dictionaryFile.text.Split('\n');
        
        foreach (string word in words)
        {
            validWords.Add(word.Trim().ToUpper());
        }
        
        Debug.Log($"Loaded {validWords.Count} words");
    }
    
    public bool IsValidWord(string word)
    {
        return validWords.Contains(word.ToUpper());
    }
}
