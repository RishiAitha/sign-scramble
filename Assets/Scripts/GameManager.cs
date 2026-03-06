using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Unity.Netcode;
using Unity.Collections;

public class GameManager : NetworkBehaviour
{
    [SerializeField] private TextMeshProUGUI playerDisplay;
    
    public NetworkVariable<bool> networkSetupComplete = new(false);

    // Game timer
    private Coroutine gameTimerCoroutine;

    // Game over / restart state
    public NetworkVariable<int> playAgainVotes = new(0);
    public NetworkVariable<bool> gameIsOver = new(false);
    private HashSet<ulong> votedPlayers = new HashSet<ulong>();
    private ConnectionManager connectionManagerRef;

    // Player assignments (clientId -> player number 0-2)
    private Dictionary<ulong, int> playerNumbers = new Dictionary<ulong, int>();
    private int myPlayerNumber = 0;

    public void StartGame()
    {
        networkSetupComplete.OnValueChanged += NetworkSetupComplete;
        if (IsServer)
        {
            // reset game over state
            gameIsOver.Value = false;
            playAgainVotes.Value = 0;
            votedPlayers.Clear();

            // signal clients that server setup is complete
            networkSetupComplete.Value = true;

            // Assign player numbers to all connected clients
            AssignPlayerNumbers();

            // Subscribe to disconnect events
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public void NetworkSetupComplete(bool oldValue, bool newValue)
    {
        if (networkSetupComplete.Value)
        {
            // find connection manager reference for showing game-over UI later
            connectionManagerRef = FindObjectsByType<ConnectionManager>(FindObjectsSortMode.None)[0];

            // update our UI to reflect server state
            UpdateLocalUI();

            // Start the game timer on server
            if (IsServer)
            {
                // Start the 60-second game timer
                gameTimerCoroutine = StartCoroutine(GameTimer());
            }
        }
    }

    public override void OnNetworkSpawn()
    {
        // subscribe to changes so clients update UI
        playAgainVotes.OnValueChanged += (oldV, newV) => UpdatePlayAgainUI();
        networkSetupComplete.OnValueChanged += NetworkSetupComplete;

        connectionManagerRef = FindObjectsByType<ConnectionManager>(FindObjectsSortMode.None)[0];

        // initial UI refresh
        UpdateLocalUI();
    }

    public override void OnNetworkDespawn()
    {
        // best-effort cleanup
        try { playAgainVotes.OnValueChanged -= (oldV, newV) => UpdatePlayAgainUI(); } catch { }
        try { networkSetupComplete.OnValueChanged -= NetworkSetupComplete; } catch { }

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

    // ClientRpc: instruct clients to show game over UI (win/lose)
    [ClientRpc]
    public void ShowGameOverClientRpc(bool win, ClientRpcParams rpcParams = default)
    {
        if (IsServer) gameIsOver.Value = true;

        var cm = connectionManagerRef != null ? connectionManagerRef : FindObjectsByType<ConnectionManager>(FindObjectsSortMode.None)[0];
        if (cm != null) cm.ShowGameOver(win);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;

        // If game is over and someone disconnects, abort the vote and return everyone to menu
        if (gameIsOver.Value)
        {
            Debug.Log($"Client {clientId} disconnected during game over. Returning all to menu.");
            ReturnToMenuClientRpc();
        }
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

    // Update the play-again UI counter
    private void UpdatePlayAgainUI()
    {
        var cm = connectionManagerRef != null ? connectionManagerRef : FindObjectsByType<ConnectionManager>(FindObjectsSortMode.None)[0];
        if (cm != null && NetworkManager.Singleton != null)
        {
            int total = NetworkManager.Singleton.ConnectedClientsIds.Count;
            cm.UpdatePlayAgainText(playAgainVotes.Value, total);
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
        var cm = connectionManagerRef != null ? connectionManagerRef : FindObjectsByType<ConnectionManager>(FindObjectsSortMode.None)[0];
        if (cm != null)
        {
            cm.ReturnToMainMenu();
        }
    }

    // ClientRpc: switch UI back to game mode when restarting
    [ClientRpc]
    private void RestartGameClientRpc(ClientRpcParams rpcParams = default)
    {
        var cm = connectionManagerRef != null ? connectionManagerRef : FindObjectsByType<ConnectionManager>(FindObjectsSortMode.None)[0];
        if (cm != null)
        {
            cm.RestartGame();
        }
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
        
        // Wait for 60 seconds
        yield return new WaitForSeconds(60f);
        
        Debug.Log("Time's up! Player 1 wins by default.");
        
        // Game over: Player 1 (index 0) wins by default
        // Get player 0's client ID
        ulong player0ClientId = GetClientIdAtIndex(0);
        
        // Send win to player 0
        if (player0ClientId != 0)
        {
            ShowGameOverClientRpc(true, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { player0ClientId }
                }
            });
        }
        
        // Send lose to all other players (player 1)
        ulong player1ClientId = GetClientIdAtIndex(1);
        if (player1ClientId != 0)
        {
            ShowGameOverClientRpc(false, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { player1ClientId }
                }
            });
        }
    }
}
