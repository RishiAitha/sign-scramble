using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Services.Matchmaker;
using Unity.Services.Matchmaker.Models;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System.Threading.Tasks;

public class ConnectionManager : MonoBehaviour
{
    // Matchmaker Configuration
    private readonly string QUEUE_NAME = "ScrambleQueue";
    private readonly string POOL_NAME = "ScramblePool";

    // UI Elements
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI gameOverText;
    [SerializeField] private TextMeshProUGUI playAgainText;
    [SerializeField] private Button quickPlayButton;
    [SerializeField] private Button playAgainButton;
    [SerializeField] private TMP_InputField codeInput;
    
    // Game Over Score Display
    [SerializeField] private TextMeshProUGUI player1ScoreText;
    [SerializeField] private TextMeshProUGUI player2ScoreText;
    [SerializeField] private TextMeshProUGUI player1WordsText;
    [SerializeField] private TextMeshProUGUI player2WordsText;

    // UI Sections
    [SerializeField] private GameObject connectionUI;
    [SerializeField] private GameObject gameUI;
    [SerializeField] private GameObject gameOverUI;

    // Connection Management
    private bool isMatchmaking = false;
    private bool errorDisplaying = false;
    private bool servicesReady = false;
    private bool gameStarted = false;
    private bool isDisconnecting = false;
    private string hostCode = "";
    private Lobby currentLobby;

    // Cleanup
    private Coroutine heartbeat = null;

    // Other Scripts
    [SerializeField] private GameManager gameManager;

    private async void Start()
    {
        // Choose UI Elements to Activate
        connectionUI.SetActive(true);
        gameUI.SetActive(false);
        gameOverUI.SetActive(false);

        // Authenticate player for matchmaking/lobby services
        try
        {
            statusText.text = "Initializing...";

            // Default profile name
            string profileName = "Main";

            #if UNITY_EDITOR
            
            // ParrelSync setup
            string projectPath = Application.dataPath;
            if (projectPath.Contains("_clone_"))
            {
                int cloneIndex = projectPath.IndexOf("_clone_");
                if (cloneIndex >= 0 && projectPath.Length > cloneIndex + 7)
                {
                    profileName = "Clone" + projectPath.Substring(cloneIndex + 7, 1);
                }
            }

            #else

            // Built version: use a per-process profile so two local builds can sign in separately.
            profileName = BuildRuntimeProfileName();

            #endif

            // Sign in to authentication service with new profile name
            var options = new InitializationOptions();
            options.SetProfile(profileName);
            await UnityServices.InitializeAsync(options);

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            servicesReady = true;
            statusText.text = "Connection services ready.";
        }
        catch (Exception e)
        {
            Debug.LogError($"Authentication Error: {e.Message}");
            errorDisplaying = true;
            statusText.text = $"Authentication Error: {e.Message}";
        }
    }

    private void Update()
    {
        // Keep checking to see if lobby is finally ready
        if (NetworkManager.Singleton != null && !errorDisplaying && servicesReady)
        {
            // Get number of players
            int playerCount = NetworkManager.Singleton.ConnectedClientsIds.Count;

            // Display appropriate status message based on connection type
            if (NetworkManager.Singleton.IsHost)
            {
                statusText.text = $"Connected as Host\nJoin Code: {hostCode}\nPlayers {playerCount}/2";
            }
            else if (NetworkManager.Singleton.IsClient && NetworkManager.Singleton.IsConnectedClient)
            {
                statusText.text = $"Connected as Client";
            }

            // Start game once two players have connected
            // Only start if we're on the connection UI (not during disconnect or game over)
            if (playerCount == 2 && !gameStarted && !isDisconnecting && connectionUI.activeSelf)
            {
                gameStarted = true;
                StartGame();
            }
        }
    }

    // Host and client lobby connection system

    // Used for manual hosting
    public async void StartHost()
    {
        try
        {
            hostCode = await StartHostWithRelay(1, "dtls");
        }
        catch (Exception e)
        {
            Debug.LogError($"Relay Error: {e.Message}");
            errorDisplaying = true;
            if (statusText != null) statusText.text = $"Error: {e.Message}";
        }
    }

    // Used for matchmaker host relay setup
    private async Task SetupAsHost()
    {
        statusText.text = "Setting up as host...";

        try
        {
            // Get relay code
            hostCode = await StartHostWithRelay(1, "dtls");

            // Share relay codes in lobby
            var updateOptions = new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { "RelayJoinCode", new DataObject(DataObject.VisibilityOptions.Member, hostCode) }
                }
            };

            currentLobby = await LobbyService.Instance.UpdateLobbyAsync(currentLobby.Id, updateOptions);

            heartbeat = StartCoroutine(LobbyHeartbeat());
            statusText.text = "Hosting game...";
        }
        catch (Exception e)
        {
            Debug.LogError($"Host setup error: {e.Message}");
            statusText.text = $"Host error: {e.Message}";
            errorDisplaying = true;
        }
    }

    // Used for host relay setup
    private async Task<string> StartHostWithRelay(int maxConnections, string connectionType)
    {
        // create two relay allocations for other players
        var allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
        
        // set up netcode transport for relay
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(
            AllocationUtils.ToRelayServerData(allocation, connectionType)
        );
        
        // get join code
        var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        
        return NetworkManager.Singleton.StartHost() ? joinCode : null;
    }

    // Used for manual joining
    public async void StartClient()
    {
        try
        {
            string joinCode = codeInput.text;
            bool success = await StartClientWithRelay(joinCode, "dtls");
            
            if (!success)
            {
                throw new Exception("Failed to join game");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Join Error: {e.Message}");
            errorDisplaying = true;
            if (statusText != null) statusText.text = $"Error: {e.Message}";
        }
    }

    // Used for client relay setup
    private async Task<bool> StartClientWithRelay(string joinCode, string connectionType)
    {
        // join host relay with code
        var allocation = await RelayService.Instance.JoinAllocationAsync(joinCode: joinCode);
        
        // set up netcode transport to use relay
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(
            AllocationUtils.ToRelayServerData(allocation, connectionType)
        );
        
        return !string.IsNullOrEmpty(joinCode) && NetworkManager.Singleton.StartClient();
    }

    // Used for matchmaker client relay setup
    private async Task SetupAsClient()
    {
        statusText.text = "Setting up as client...";

        try
        {
            // Poll lobby until host shares the Relay join code
            string relayJoinCode = null;
            int maxAttempts = 30;
            int attempts = 0;

            while (string.IsNullOrEmpty(relayJoinCode) && attempts < maxAttempts)
            {
                await Task.Delay(2000);
                attempts++;

                // Refresh lobby data
                currentLobby = await LobbyService.Instance.GetLobbyAsync(currentLobby.Id);

                // Check if host has shared the Relay join code
                if (currentLobby.Data != null && currentLobby.Data.ContainsKey("RelayJoinCode"))
                {
                    relayJoinCode = currentLobby.Data["RelayJoinCode"].Value;
                }
                else
                {
                    statusText.text = $"Waiting for host... {(maxAttempts - attempts) * 2}s";
                }
            }

            if (string.IsNullOrEmpty(relayJoinCode))
            {
                throw new Exception("Timeout waiting for host");
            }

            // start client with relay code
            bool success = await StartClientWithRelay(relayJoinCode, "dtls");
            
            if (!success)
            {
                throw new Exception("Failed to connect to host");
            }
            
            statusText.text = "Connected";
        }
        catch (Exception e)
        {
            Debug.LogError($"Client setup error: {e.Message}");
            if (e.Message != null && e.Message.ToLower().Contains("not found"))
            {
                // Lobby disappeared; make sure we clean up local state
                CleanupLobby();
                statusText.text = "Host left or lobby not found.";
            }
            else
            {
                statusText.text = $"Connection error: {e.Message}";
            }
            errorDisplaying = true;
        }
    }

    // Sends ticket for matchmaker to find a lobby
    public async void StartMatchmaking()
    {
        if (!servicesReady)
        {
            statusText.text = "Services not ready, please wait...";
            return;
        }

        if (isMatchmaking)
        {
            return;
        }

        try
        {
            isMatchmaking = true;
            quickPlayButton.interactable = false;

            
            statusText.text = "Finding match...";
            var players = new List<Unity.Services.Matchmaker.Models.Player>
            {
                new (AuthenticationService.Instance.PlayerId, new Dictionary<string, object>())
            };

            // Set matchmaking options
            var options = new CreateTicketOptions(
                QUEUE_NAME, // The name of the queue defined in the previous step,
                new Dictionary<string, object>());

            // Create ticket
            var ticketResponse = await MatchmakerService.Instance.CreateTicketAsync(players, options);


            await PollTicketStatus(ticketResponse.Id);
        }
        catch (Exception e)
        {
            Debug.LogError($"Matchmaking Error: {e.Message}");
            errorDisplaying = true;
            if (statusText != null) statusText.text = $"Matchmaking Error: {e.Message}";
        }
        finally
        {
            isMatchmaking = false;
            quickPlayButton.interactable = true;
        }
    }

    // Checks for ticket status updates to find lobby
    private async Task PollTicketStatus(string ticketId)
    {
        float timeout = 60f;
        float elapsed = 0f;

        // Polling ticket status

        while (elapsed < timeout)
        {
            var ticketStatus = await MatchmakerService.Instance.GetTicketAsync(ticketId);

            if (ticketStatus == null)
            {
                Debug.LogWarning("[Matchmaking] Ticket status is null");
            }
            else
            {
                // Some queues return a full MultiplayAssignment, others a lightweight MatchIdAssignment.
                if (ticketStatus.Type == typeof(MultiplayAssignment))
                {
                    var assignment = ticketStatus.Value as MultiplayAssignment;
                    if (assignment != null && assignment.Status == MultiplayAssignment.StatusOptions.Found && !string.IsNullOrEmpty(assignment.MatchId))
                    {
                        if (statusText != null) statusText.text = "Match found! Connecting...";
                        await HandleMatchAssignment(assignment.MatchId);
                        return;
                    }
                }
                else if (ticketStatus.Type == typeof(MatchIdAssignment))
                {
                    var matchIdAssign = ticketStatus.Value as MatchIdAssignment;
                    if (!string.IsNullOrEmpty(matchIdAssign?.MatchId))
                    {
                        if (statusText != null) statusText.text = "Match found! Connecting...";
                        await HandleMatchAssignment(matchIdAssign.MatchId);
                        return;
                    }
                }
            }

            // wait and update countdown
            for (int i = 0; i < 6; i++)
            {
                if (statusText != null) statusText.text = $"Finding match... {(int)(timeout - elapsed - (i * 0.5f))}s";
                await Task.Delay(500);
            }
            elapsed += 3f;
        }

        Debug.LogWarning($"[Matchmaking] Ticket {ticketId} timed out after {timeout}s");
        statusText.text = "Matchmaking timed out. Try again?";
        await MatchmakerService.Instance.DeleteTicketAsync(ticketId); 
        errorDisplaying = false;
    }

    // Handles lobby assignment from ticket
    private async Task HandleMatchAssignment(string matchId)
    {
        try
        {
            if (statusText != null) statusText.text = "Creating/joining lobby...";

            var createOptions = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>()
            };

            // create lobby if it doesn't exist, join it otherwise
            currentLobby = await LobbyService.Instance.CreateOrJoinLobbyAsync(
                lobbyId: matchId,
                lobbyName: $"Hangman_{matchId.Substring(0, 8)}",
                maxPlayers: 2,
                options: createOptions
            );


            // check if you are host
            bool isHost = currentLobby.HostId == AuthenticationService.Instance.PlayerId;

            if (isHost)
            {
                await SetupAsHost();
            }
            else
            {
                await SetupAsClient();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Match assignment error: {e.Message}");
            Debug.LogError($"[Lobby] Stack trace: {e.StackTrace}");
            if (statusText != null) statusText.text = $"Connection error: {e.Message}";
            errorDisplaying = true;
        }
    }

    private string BuildRuntimeProfileName()
    {
        // Optional override for testing multiple local builds: -profile YourName
        string[] args = Environment.GetCommandLineArgs();
        int profileArgIndex = Array.IndexOf(args, "-profile");
        if (profileArgIndex >= 0 && profileArgIndex + 1 < args.Length)
        {
            string explicitProfile = args[profileArgIndex + 1];
            if (!string.IsNullOrWhiteSpace(explicitProfile))
            {
                return explicitProfile.Trim();
            }
        }

        return $"Build_{System.Diagnostics.Process.GetCurrentProcess().Id}_{Guid.NewGuid().ToString("N").Substring(0, 6)}";
    }

    // Ensures lobby stays active with heartbeat
    private IEnumerator LobbyHeartbeat()
    {
        while (currentLobby != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.Id); 
            yield return new WaitForSeconds(15f);
        }
    }

    private void StartGame()
    {
        Debug.Log("Starting game!");
        connectionUI.SetActive(false);
        gameUI.SetActive(true);
        gameManager.StartGame();
    }

    // Show the game over UI and message. Called by GameManager when game ends.
    public void ShowGameOver(int winnerPlayer, int player0Score, int player1Score, string player0Words, string player1Words)
    {
        connectionUI.SetActive(false);
        gameUI.SetActive(false);
        gameOverUI.SetActive(true);
        
        if (gameOverText != null)
        {
            if (winnerPlayer != -1)
            {
                gameOverText.text = $"Player {winnerPlayer + 1} Victory";
            }
            else
            {
                gameOverText.text = "It's a Tie";
            }
        }
        
        // Display scores
        if (player1ScoreText != null)
        {
            player1ScoreText.text = player0Score.ToString();
        }
        if (player2ScoreText != null)
        {
            player2ScoreText.text = player1Score.ToString();
        }
        
        // Display word lists
        if (player1WordsText != null)
        {
            player1WordsText.text = string.IsNullOrEmpty(player0Words) ? "No words" : player0Words;
        }
        if (player2WordsText != null)
        {
            player2WordsText.text = string.IsNullOrEmpty(player1Words) ? "No words" : player1Words;
        }
        
        if (playAgainText != null)
        {
            playAgainText.text = "0/2 Accepted";
        }
        
        // Re-enable the play again button
        if (playAgainButton != null)
        {
            playAgainButton.interactable = true;
        }
    }

    // Update the play-again vote counter text
    public void UpdatePlayAgainText(int votes, int total)
    {
        if (playAgainText != null)
        {
            playAgainText.text = $"{votes}/2 Accepted";
        }
    }

    // Called when a player clicks the Play Again button
    public void OnPlayAgainClicked()
    {
        // Disable button so user can't click multiple times
        if (playAgainButton != null)
        {
            playAgainButton.interactable = false;
        }
        
        if (gameManager != null)
        {
            gameManager.VotePlayAgain();
        }
    }

    // Called when a player clicks the Disconnect button
    public void OnDisconnectClicked()
    {
        // Request coordinated disconnect via GameManager
        if (gameManager != null)
        {
            gameManager.RequestDisconnect();
        }
        else
        {
            // Fallback if GameManager not found
            ReturnToMainMenu();
        }
    }

    // Return to main menu and clean up connection
    public void ReturnToMainMenu()
    {
        // Set disconnecting flag to prevent auto-start
        isDisconnecting = true;
        
        // Switch UI immediately
        gameOverUI.SetActive(false);
        gameUI.SetActive(false);
        connectionUI.SetActive(true);
        
        gameStarted = false;
        
        // Delay shutdown to allow RPCs to complete
        StartCoroutine(DelayedShutdown());
    }

    // Coroutine to delay network shutdown
    private IEnumerator DelayedShutdown()
    {
        // Wait a frame to ensure UI changes and RPCs are processed
        yield return new WaitForSeconds(0.1f);
        
        // Shutdown network and clean up
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }
        
        CleanupLobby();
        
        // Reset disconnecting flag after cleanup
        yield return new WaitForSeconds(0.1f);
        isDisconnecting = false;
    }

    // Switch UI from game over back to game mode (called when restarting)
    public void RestartGame()
    {
        gameOverUI.SetActive(false);
        connectionUI.SetActive(false);
        gameUI.SetActive(true);
    }


    // Handles cleanup if this object is destroyed
    private void OnDestroy()
    {
        // clean up lobby when leaving
        if (currentLobby != null)
        {
            CleanupLobby();
        }
    }

    // Cleans up old connection setup
    public async void CleanupLobby()
    {
        if (heartbeat != null)
        {
            try { StopCoroutine(heartbeat); } catch { }
            heartbeat = null;
        }

        if (currentLobby != null)
        {
            try
            {
                if (currentLobby.HostId == AuthenticationService.Instance.PlayerId)
                {
                    await LobbyService.Instance.DeleteLobbyAsync(currentLobby.Id);
                }
                else
                {
                    await LobbyService.Instance.RemovePlayerAsync(currentLobby.Id, AuthenticationService.Instance.PlayerId);
                }
            }
            catch (Exception e)
            {
                // Ignore lobby-not-found during cleanup, but log other issues
                if (e.Message != null && e.Message.ToLower().Contains("not found"))
                {
                    Debug.LogWarning($"CleanupLobby: lobby not found (may have been deleted): {e.Message}");
                }
                else
                {
                    Debug.LogError($"Cleanup error: {e.Message}");
                }
            }
            finally
            {
                currentLobby = null;
                gameStarted = false;
                isMatchmaking = false;
                isDisconnecting = false;
                errorDisplaying = false;
                statusText.text = "Disconnected";
                quickPlayButton.interactable = servicesReady;
            }
        }
    }
}