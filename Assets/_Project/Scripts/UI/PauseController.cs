using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gestisce la pausa in modo sincronizzato su tutti i client.
/// Richiede un componente NetworkObject sullo stesso GameObject (scene object).
/// Quando un qualsiasi client preme ESC, la pausa si propaga a tutti tramite ServerRpc.
/// </summary>
public class PauseController : NetworkBehaviour
{
    public static GameState CurrentGameState { get; private set; } = GameState.Gameplay;
    public delegate void GameStateChangeHandler(GameState newGameState);
    public static event GameStateChangeHandler OnGameStateChanged;

    /// <summary>Assegnato a runtime da LocalPlayerHUD del player locale.</summary>
    public GameObject PauseUI;

    // Stato di pausa sincronizzato: il server lo scrive, tutti i client lo leggono
    private readonly NetworkVariable<bool> _networkPaused = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        _networkPaused.OnValueChanged += OnNetworkPausedChanged;
        // Applica lo stato iniziale (rilevante se ci si connette a partita già in pausa)
        ApplyPauseState(_networkPaused.Value);
    }

    public override void OnNetworkDespawn()
    {
        _networkPaused.OnValueChanged -= OnNetworkPausedChanged;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            TogglePause();
    }

    // ─── ServerRpc ────────────────────────────────────────────────────────────

    /// <summary>Chiunque può richiederlo — RequireOwnership=false perché è uno scene object.</summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestTogglePauseServerRpc()
    {
        _networkPaused.Value = !_networkPaused.Value;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSetPausedServerRpc(bool paused)
    {
        _networkPaused.Value = paused;
    }

    // ─── Callback NetworkVariable ─────────────────────────────────────────────

    private void OnNetworkPausedChanged(bool oldValue, bool newValue)
    {
        ApplyPauseState(newValue);
    }

    // ─── API pubblica (usata da PauseMenuActions) ─────────────────────────────

    /// <summary>Chiamato da Update (ESC) e da PauseMenuActions.</summary>
    public void TogglePause()
    {
        bool newPaused = CurrentGameState != GameState.Paused;
        if (IsSpawned)
            RequestTogglePauseServerRpc();   // sincronizza tutti i client
        else
            ApplyPauseState(newPaused);      // fallback offline / editor senza rete
    }

    /// <summary>
    /// Chiamato dai bottoni del PauseMenu (es. Continue).
    /// Propaga il nuovo stato a tutti i client via server.
    /// </summary>
    public void SetState(GameState newState)
    {
        bool paused = newState == GameState.Paused;
        if (IsSpawned)
            RequestSetPausedServerRpc(paused);
        else
            ApplyPauseState(paused);
    }

    // ─── Applicazione locale ──────────────────────────────────────────────────

    private void ApplyPauseState(bool isPaused)
    {
        CurrentGameState = isPaused ? GameState.Paused : GameState.Gameplay;
        OnGameStateChanged?.Invoke(CurrentGameState);

        Time.timeScale        = isPaused ? 0f : 1f;
        Cursor.lockState      = CursorLockMode.None;
        Cursor.visible        = true;
        if (PauseUI != null) PauseUI.SetActive(isPaused);
    }
}
