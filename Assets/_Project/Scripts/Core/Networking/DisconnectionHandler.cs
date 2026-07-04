using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gestisce le disconnessioni in sessione multiplayer.
///
/// Comportamento:
///   - Host si disconnette  → tutti i client vanno al MainMenu
///   - Joiner si disconnette → host vede "Waiting..." + timer → reconnect ripristina stato
///                          → timer scaduto o rinuncia → tutti al MainMenu
///
/// Setup scena:
///   1. Aggiungere questo script al GO "GameSetup" (o un GO dedicato) nella scena Cathedral.
///   2. I campi WaitingPanel e TimerText vengono assegnati a runtime da LocalPlayerHUD.
/// </summary>
public class DisconnectionHandler : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Secondi di attesa prima che l'host torni al MainMenu se il joiner non si riconnette.")]
    [SerializeField] private float reconnectTimeout = 60f;

    [SerializeField] private string mainMenuScene = "MainMenu";

    // Assegnati a runtime da LocalPlayerHUD del player locale (host)
    [HideInInspector] public GameObject WaitingPanel;
    [HideInInspector] public TMP_Text TimerText;

    private PlayerSpawnServer _spawnServer;
    private PauseController _pauseController;
    private Coroutine _timerCoroutine;
    private bool _waitingForReconnect;

    // ─── Unity ────────────────────────────────────────────────────────────────

    private void Start()
    {
        _spawnServer = FindFirstObjectByType<PlayerSpawnServer>();
        _pauseController = FindFirstObjectByType<PauseController>();

        if (_spawnServer != null)
            _spawnServer.OnPlayerReconnected += HandlePlayerReconnected;

        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
    }

    private void OnDestroy()
    {
        if (_spawnServer != null)
            _spawnServer.OnPlayerReconnected -= HandlePlayerReconnected;

        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
    }

    // ─── Callbacks ────────────────────────────────────────────────────────────

    private void OnClientDisconnect(ulong clientId)
    {
        // ── CLIENT: l'host è caduto ──────────────────────────────────────────
        // Null check necessario: quando l'host chiama Shutdown(), il Singleton
        // può già essere null sul client al momento del callback.
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            GoToMainMenu();
            return;
        }

        // ── SERVER: ignora la disconnessione del host stesso ─────────────────
        if (clientId == NetworkManager.Singleton.LocalClientId) return;

        // ── SERVER: il joiner si è disconnesso ───────────────────────────────
        if (_waitingForReconnect) return; // già in attesa, ignora duplicati

        _waitingForReconnect = true;

        // Salva lo stato del joiner in PlayerSpawnServer prima che il NetworkObject venga distrutto
        _spawnServer?.BeginWaitForReconnect(clientId);

        // Metti in pausa il gioco per l'host
        _pauseController?.SetState(GameState.Paused);

        if (WaitingPanel != null) WaitingPanel.SetActive(true);

        if (_timerCoroutine != null) StopCoroutine(_timerCoroutine);
        _timerCoroutine = StartCoroutine(ReconnectTimerRoutine());

        Debug.Log("[DisconnectionHandler] Joiner disconnesso. In attesa di reconnect...");
    }

    private void HandlePlayerReconnected()
    {
        if (!_waitingForReconnect) return;

        _waitingForReconnect = false;
        if (_timerCoroutine != null) { StopCoroutine(_timerCoroutine); _timerCoroutine = null; }

        if (WaitingPanel != null) WaitingPanel.SetActive(false);

        _pauseController?.SetState(GameState.Gameplay);

        Debug.Log("[DisconnectionHandler] Joiner riconnesso. Gioco ripreso.");
    }

    // ─── Timer ────────────────────────────────────────────────────────────────

    private IEnumerator ReconnectTimerRoutine()
    {
        float remaining = reconnectTimeout;
        while (remaining > 0f)
        {
            if (TimerText != null)
                TimerText.text = Mathf.CeilToInt(remaining) + "s";
            yield return new WaitForSecondsRealtime(1f); // non bloccato da timeScale = 0
            remaining -= 1f;
        }

        // Timeout scaduto
        _waitingForReconnect = false;
        _spawnServer?.CancelReconnectWait();
        if (WaitingPanel != null) WaitingPanel.SetActive(false);

        Debug.Log("[DisconnectionHandler] Timeout reconnect. Ritorno al MainMenu.");
        GoToMainMenu();
    }

    // ─── Bottone "Torna al MainMenu" nel WaitingPanel ─────────────────────────

    /// <summary>Chiamato dal bottone nel pannello di attesa se l'host decide di rinunciare.</summary>
    public void OnGiveUpClicked()
    {
        if (_timerCoroutine != null) { StopCoroutine(_timerCoroutine); _timerCoroutine = null; }
        _waitingForReconnect = false;
        _spawnServer?.CancelReconnectWait();
        GoToMainMenu();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void GoToMainMenu()
    {
        Time.timeScale = 1f;
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene(mainMenuScene);
    }


}
