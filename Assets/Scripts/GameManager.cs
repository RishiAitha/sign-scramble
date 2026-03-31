using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Unity.Netcode;
using Unity.Collections;
using System.Linq;

public class GameManager : NetworkBehaviour
{
    [SerializeField] private TextMeshProUGUI playerDisplay;
    [SerializeField] private TextMeshProUGUI timeDisplay;
    
    public NetworkVariable<bool> networkSetupComplete = new(false);
    public NetworkVariable<int> timeRemaining = new(60);

    // Game timer
    private Coroutine gameTimerCoroutine;

    // References to other managers (set in Inspector)
    [SerializeField] private ConnectionManager connectionManager;
    [SerializeField] private WordFinder wordFinder;
    [SerializeField] private BoardGenerator boardGenerator;
    
    // Game over / restart state
    public NetworkVariable<int> playAgainVotes = new(0);
    public NetworkVariable<bool> gameIsOver = new(false);
    private HashSet<ulong> votedPlayers = new HashSet<ulong>();

    // Player assignments (clientId -> player number 0-2)
    private Dictionary<ulong, int> playerNumbers = new Dictionary<ulong, int>();
    private int myPlayerNumber = 0;

    // Scores and word lists for each player
    public NetworkVariable<int> player0Score = new(0);
    public NetworkVariable<int> player1Score = new(0);
    private NetworkVariable<FixedString4096Bytes> player0Words = new(new FixedString4096Bytes(""));
    private NetworkVariable<FixedString4096Bytes> player1Words = new(new FixedString4096Bytes(""));
    private List<string> myCompletedWords = new List<string>();

    public void StartGame()
    {
        if (IsServer)
        {
            // reset game over state
            gameIsOver.Value = false;
            playAgainVotes.Value = 0;
            votedPlayers.Clear();
            timeRemaining.Value = 60;
            
            // Reset scores and word lists
            player0Score.Value = 0;
            player1Score.Value = 0;
            player0Words.Value = new FixedString4096Bytes("");
            player1Words.Value = new FixedString4096Bytes("");

            // Stop any existing timer
            if (gameTimerCoroutine != null)
            {
                StopCoroutine(gameTimerCoroutine);
                gameTimerCoroutine = null;
            }

            // signal clients that server setup is complete
            networkSetupComplete.Value = true;

            // Assign player numbers to all connected clients
            AssignPlayerNumbers();

            // Subscribe to disconnect events
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            // Host generates one board and sends it to everyone.
            if (boardGenerator != null)
            {
                string boardString = boardGenerator.GenerateBoardString();
                boardGenerator.SetBoardFromString(boardString);
                SyncBoardClientRpc(new FixedString32Bytes(boardString));
            }
            
            // Tell all clients to reset their local state
            ResetClientStateClientRpc();

            // Start the game timer
            gameTimerCoroutine = StartCoroutine(GameTimer());
        }
    }

    public void NetworkSetupComplete(bool oldValue, bool newValue)
    {
        if (networkSetupComplete.Value)
        {
            // Clear local word list when game starts
            myCompletedWords.Clear();

            // update our UI to reflect server state
            UpdateLocalUI();
        }
    }

    public override void OnNetworkSpawn()
    {
        // subscribe to changes so clients update UI
        playAgainVotes.OnValueChanged += (oldV, newV) => UpdatePlayAgainUI();
        networkSetupComplete.OnValueChanged += NetworkSetupComplete;
        timeRemaining.OnValueChanged += (oldV, newV) => UpdateTimeDisplay();

        // initial UI refresh
        UpdateLocalUI();
        UpdateTimeDisplay();
    }

    public override void OnNetworkDespawn()
    {
        // best-effort cleanup
        try { playAgainVotes.OnValueChanged -= (oldV, newV) => UpdatePlayAgainUI(); } catch { }
        try { networkSetupComplete.OnValueChanged -= NetworkSetupComplete; } catch { }
        try { timeRemaining.OnValueChanged -= (oldV, newV) => UpdateTimeDisplay(); } catch { }

        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    // Server assigns player numbers based on connection order
    private void AssignPlayerNumbers()
    {
        if (!IsServer) return;

        var ids = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        ids.Sort();

        for (int i = 0; i < ids.Count; i++)
        {
            playerNumbers[ids[i]] = i;
            SetPlayerNumberClientRpc(i, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { ids[i] }
                }
            });
        }
    }

    // ClientRpc: server tells this specific client their player number
    [ClientRpc]
    private void SetPlayerNumberClientRpc(int playerNum, ClientRpcParams rpcParams = default)
    {
        myPlayerNumber = playerNum;
        UpdateLocalUI();
    }

    // ClientRpc: instruct clients to show game over UI with all game data
    [ClientRpc]
    public void ShowGameOverClientRpc(int winnerPlayer, int p0Score, int p1Score, FixedString4096Bytes p0Words, FixedString4096Bytes p1Words, ClientRpcParams rpcParams = default)
    {
        connectionManager.ShowGameOver(winnerPlayer, p0Score, p1Score, p0Words.ToString(), p1Words.ToString());
    }
    
    // ClientRpc: reset all client-side state when game starts/restarts
    [ClientRpc]
    private void ResetClientStateClientRpc(ClientRpcParams rpcParams = default)
    {
        // Clear local word list
        myCompletedWords.Clear();
        
        // Reset WordFinder state
        wordFinder.ResetState();
    }

    [ClientRpc]
    private void SyncBoardClientRpc(FixedString32Bytes boardString, ClientRpcParams rpcParams = default)
    {
        if (boardGenerator == null) return;
        boardGenerator.SetBoardFromString(boardString.ToString());
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;

        Debug.Log($"Client {clientId} disconnected.");
        
        // When someone disconnects, everyone should return to menu
        // Check if NetworkManager is still active before sending RPCs
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            // Send RPC to remaining connected clients
            ReturnToMenuClientRpc();
        }
        
        // Host also needs to clean up locally
        connectionManager.ReturnToMainMenu();
    }

    // Public method: client votes to play again
    public void VotePlayAgain()
    {
        if (!NetworkManager.Singleton.IsClient) return;
        VotePlayAgainServerRpc();
    }

    // ServerRpc: handle play-again vote
    [Rpc(SendTo.Server)]
    private void VotePlayAgainServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer) return;

        ulong voter = rpcParams.Receive.SenderClientId;
        
        // Only count each client once
        if (votedPlayers.Contains(voter)) return;
        
        votedPlayers.Add(voter);
        playAgainVotes.Value = votedPlayers.Count;

        Debug.Log($"Play again vote: {playAgainVotes.Value}/{NetworkManager.Singleton.ConnectedClientsIds.Count}");

        // If all players voted, restart the game
        if (playAgainVotes.Value >= NetworkManager.Singleton.ConnectedClientsIds.Count)
        {
            Debug.Log("All players voted to play again. Restarting game...");
            // First, tell all clients to switch UI panels back to game mode
            RestartGameClientRpc();
            // Then start the game (this will trigger NetworkVariable changes)
            StartGame();
        }
    }

    // Update UI elements on this client from current NetworkVariable values
    private void UpdateLocalUI()
    {
        // show which player this client is (1-based)
        if (playerDisplay != null)
        {
            playerDisplay.text = (myPlayerNumber + 1).ToString();
        }
    }

    // Update the time display
    private void UpdateTimeDisplay()
    {
        if (timeDisplay != null)
        {
            timeDisplay.text = timeRemaining.Value.ToString();
        }
    }

    // Update the play-again UI counter
    private void UpdatePlayAgainUI()
    {
        if (NetworkManager.Singleton != null)
        {
            int total = NetworkManager.Singleton.ConnectedClientsIds.Count;
            connectionManager.UpdatePlayAgainText(playAgainVotes.Value, total);
        }
    }

    // Public method: client requests to disconnect from game over screen
    public void RequestDisconnect()
    {
        if (!NetworkManager.Singleton.IsClient) return;
        RequestDisconnectServerRpc();
    }

    // ServerRpc: handle disconnect request - broadcast to all clients
    [Rpc(SendTo.Server)]
    private void RequestDisconnectServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer) return;

        Debug.Log("Disconnect requested. Returning all players to menu...");
        
        // Tell all clients to return to menu
        ReturnToMenuClientRpc();
    }

    // ClientRpc: return all players to main menu (e.g., on disconnect)
    [ClientRpc]
    private void ReturnToMenuClientRpc(ClientRpcParams rpcParams = default)
    {
        Debug.Log("ReturnToMenuClientRpc called");
        connectionManager.ReturnToMainMenu();
    }

    // ClientRpc: switch UI back to game mode when restarting
    [ClientRpc]
    private void RestartGameClientRpc(ClientRpcParams rpcParams = default)
    {
        connectionManager.RestartGame();
    }
    
    // helper: map a player-index to the currently-connected client id (best-effort)
    private ulong GetClientIdAtIndex(int idx)
    {
        if (!IsServer) return 0;
        
        // Use the assigned player numbers
        foreach (var kvp in playerNumbers)
        {
            if (kvp.Value == idx) return kvp.Key;
        }
        
        // fallback to sorted list
        var ids = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        ids.Sort();
        return (idx >= 0 && idx < ids.Count) ? ids[idx] : 0;
    }

    // Coroutine: Game timer (60 seconds)
    private IEnumerator GameTimer()
    {
        Debug.Log("Game started! 60 second timer begins...");
        
        // Reset and count down from 60
        timeRemaining.Value = 60;
        
        while (timeRemaining.Value > 0)
        {
            yield return new WaitForSeconds(1f);
            timeRemaining.Value--;
        }
        
        Debug.Log("Time's up! Determining winner...");
        
        // Set game over state on server first
        gameIsOver.Value = true;
        
        // Determine winner based on scores
        int winnerPlayer = -1;
        if (player0Score.Value > player1Score.Value)
        {
            winnerPlayer = 0;
        }
        else if (player0Score.Value < player1Score.Value)
        {
            winnerPlayer = 1;
        }
        
        // Send game over info to all clients
        ShowGameOverClientRpc(winnerPlayer, player0Score.Value, player1Score.Value, player0Words.Value, player1Words.Value);

        // Clear the coroutine reference
        gameTimerCoroutine = null;
    }
    
    // Public method: Get player number for external scripts
    public int GetMyPlayerNumber()
    {
        return myPlayerNumber;
    }
    
    // Public method: Add score for current player
    public void AddScore(int points)
    {
        AddScoreServerRpc(points);
    }
    
    // ServerRpc: Update score on server
    [Rpc(SendTo.Server)]
    private void AddScoreServerRpc(int points, RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        
        ulong clientId = rpcParams.Receive.SenderClientId;
        int playerNum = playerNumbers.ContainsKey(clientId) ? playerNumbers[clientId] : 0;
        
        if (playerNum == 0)
        {
            player0Score.Value += points;
        }
        else
        {
            player1Score.Value += points;
        }
    }
    
    // Public method: Add completed word for current player
    public void AddCompletedWord(string word)
    {
        myCompletedWords.Add(word.ToUpper());
        
        // Sort: longest to shortest, then alphabetically
        var sortedWords = myCompletedWords.OrderByDescending(w => w.Length).ThenBy(w => w).ToList();
        myCompletedWords = sortedWords;
        
        // Create formatted string with line breaks
        string wordList = string.Join("\n", sortedWords);
        
        AddCompletedWordServerRpc(wordList);
    }
    
    // ServerRpc: Update word list on server
    [Rpc(SendTo.Server)]
    private void AddCompletedWordServerRpc(string wordList, RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        
        ulong clientId = rpcParams.Receive.SenderClientId;
        int playerNum = playerNumbers.ContainsKey(clientId) ? playerNumbers[clientId] : 0;
        
        if (playerNum == 0)
        {
            player0Words.Value = new FixedString4096Bytes(wordList);
        }
        else
        {
            player1Words.Value = new FixedString4096Bytes(wordList);
        }
    }
}
