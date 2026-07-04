using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Stato di una stanza nel livello.
/// </summary>
public enum RoomState
{
    /// <summary>Mai visitata. Porte d'ingresso chiuse, wave non avviate.</summary>
    Sealed,

    /// <summary>I player sono entrati. Wave in corso, porte d'ingresso bloccate.</summary>
    Active,

    /// <summary>Tutte le wave completate (o stanza senza nemici). Porte sbloccate.</summary>
    Cleared
}

/// <summary>
/// Componente principale di una stanza.
/// Gestisce lo stato (Sealed → Active → Cleared), coordina il RoomWaveController,
/// blocca/sblocca le porte d'ingresso e notifica il LevelManager.
///
/// Setup in scena:
///   1. Crea un GameObject vuoto "Room_[NomeStanza]" e aggiungi questo componente.
///   2. Assegna roomId e roomDisplayName nell'Inspector.
///   3. Collega waveController, entryDoors, exitDoors.
///   4. Aggiungi un figlio con RoomEntryTrigger (BoxCollider2D trigger) che copra l'area della stanza.
///   5. Se la stanza è lo spawn iniziale, spunta isStartRoom.
/// </summary>
public class Room : NetworkBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Identificazione")]
    [Tooltip("ID univoco della stanza (es. 'crossing', 'apse_choir'). Usato da LevelManager.")]
    [SerializeField] private string roomId;

    [Tooltip("Nome leggibile per debug e UI.")]
    [SerializeField] private string roomDisplayName;

    [Header("Componenti")]
    [Tooltip("Controller delle wave nemici. Null = stanza senza combattimento.")]
    [SerializeField] private RoomWaveController waveController;

    [Tooltip("Puzzle manager opzionale (stanza con puzzle luce).")]
    [SerializeField] private PuzzleRoomManager puzzleManager;

    [Header("Porte")]
    [Tooltip("Porte di ingresso: si chiudono quando la stanza diventa Active, " +
             "si riaprono al Cleared.")]
    [SerializeField] private List<RoomDoor> entryDoors = new();

    [Tooltip("Porte di uscita gestite da questa stanza (auto-open al Cleared).")]
    [SerializeField] private List<RoomDoor> exitDoors = new();

    [Header("Spawn")]
    [Tooltip("Punto in cui spawnano i player alla prima entrata (usato solo per la start room).")]
    [SerializeField] private Transform playerSpawnPoint;

    [Tooltip("Se true, questa è la stanza di partenza. " +
             "LevelManager vi spawnerà i player e la marcherà subito come Cleared.")]
    [SerializeField] private bool isStartRoom = false;

    [Header("Camera bounds")]
    [Tooltip("BoxCollider2D (NON trigger) che definisce i confini della stanza " +
             "per il clamping della camera. Opzionale.")]
    [SerializeField] private BoxCollider2D roomBounds;

    // ─── NetworkVariable ──────────────────────────────────────────────────────

    /// <summary>Stato replicato su tutti i client.</summary>
    private readonly NetworkVariable<RoomState> _state = new(
        RoomState.Sealed,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ─── Proprietà ────────────────────────────────────────────────────────────

    public string RoomId          => roomId;
    public string RoomDisplayName => roomDisplayName;
    public RoomState State        => _state.Value;
    public bool IsStartRoom       => isStartRoom;
    public Transform PlayerSpawnPoint => playerSpawnPoint;
    public Bounds RoomBoundsWorld => roomBounds != null
        ? new Bounds(roomBounds.bounds.center, roomBounds.bounds.size)
        : new Bounds(transform.position, Vector3.one * 20f);

    // ─── Eventi ───────────────────────────────────────────────────────────────

    /// <summary>Sparato sul server (e propagato via RPC ai client) quando la stanza diventa Active.</summary>
    public event Action<Room> OnRoomActivated;

    /// <summary>Sparato sul server quando tutte le wave sono completate.</summary>
    public event Action<Room> OnRoomCleared;

    /// <summary>
    /// Sparato sul server quando la stanza viene riportata a Sealed (reset al respawn).
    /// Usato dai trigger d'ingresso per riarmarsi e poter riattivare la stanza.
    /// </summary>
    public event Action<Room> OnRoomReset;

    // ─── NGO ─────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        _state.OnValueChanged += HandleStateChanged;

        // Applica lo stato iniziale (importante per client che entrano a partita in corso)
        ApplyState(_state.Value);

        if (IsServer && isStartRoom)
            SetCleared();
    }

    public override void OnNetworkDespawn()
    {
        _state.OnValueChanged -= HandleStateChanged;

        if (waveController != null)
            waveController.OnAllWavesCleared -= HandleWavesCleared;
    }

    // ─── API Pubblica ─────────────────────────────────────────────────────────

    /// <summary>
    /// Attiva la stanza: chiude le porte d'ingresso, avvia le wave.
    /// Chiamato da RoomEntryTrigger quando i player entrano.
    /// Solo server.
    /// </summary>
    public void Activate()
    {
        if (!IsServer) return;
        if (_state.Value != RoomState.Sealed) return;

        _state.Value = RoomState.Active;

        if (waveController != null)
        {
            waveController.OnAllWavesCleared += HandleWavesCleared;
            waveController.StartWaves();
        }
        else
        {
            // Nessun nemico → cleared immediatamente
            SetCleared();
        }
    }

    /// <summary>
    /// Resetta la stanza a Sealed: nemici despawnati, wave azzerate.
    /// Chiamato da CheckpointManager al respawn.
    /// Solo server.
    /// </summary>
    public void ResetToSealed()
    {
        if (!IsServer) return;
        if (_state.Value == RoomState.Sealed) return;

        _state.Value = RoomState.Sealed;
        waveController?.ForceReset();
        OnRoomReset?.Invoke(this);
    }

    /// <summary>
    /// Forza la stanza allo stato Cleared (es. stanza start, stanza puzzle già risolta).
    /// Solo server.
    /// </summary>
    public void SetCleared()
    {
        if (!IsServer) return;
        if (_state.Value == RoomState.Cleared) return;

        _state.Value = RoomState.Cleared;
    }

    // ─── Logica interna ───────────────────────────────────────────────────────

    private void HandleStateChanged(RoomState previous, RoomState current) => ApplyState(current);

    private void ApplyState(RoomState state)
    {
        switch (state)
        {
            case RoomState.Sealed:
                // Stato iniziale: tutto chiuso
                SetEntryDoorsLocked(true);
                break;

            case RoomState.Active:
                // Blocca le porte d'ingresso mentre i nemici sono vivi
                SetEntryDoorsLocked(true);
                NotifyActivatedClientRpc();
                break;

            case RoomState.Cleared:
                // Apri porte d'ingresso (permettono retreat) e porte di uscita
                SetEntryDoorsLocked(false);
                OpenExitDoors();
                NotifyClearedClientRpc();
                break;
        }
    }

    private void HandleWavesCleared()
    {
        if (waveController != null)
            waveController.OnAllWavesCleared -= HandleWavesCleared;

        SetCleared();
        // NOTA: NON invocare OnRoomCleared qui.
        // SetCleared() → _state.Value = Cleared → HandleStateChanged → ApplyState → NotifyClearedClientRpc
        // NotifyClearedClientRpc chiama OnRoomCleared?.Invoke su tutti i client (incluso l'host/server).
        // Invocare qui causerebbe un double-fire sull'host con conseguenti duplicati in CheckpointManager.
    }

    // ─── Porte ───────────────────────────────────────────────────────────────

    private void SetEntryDoorsLocked(bool locked)
    {
        if (!IsServer) return;

        foreach (var door in entryDoors)
        {
            if (door == null) continue;

            if (locked)
            {
                // La porta non ha un metodo "Lock" diretto — la teniamo chiusa
                // non chiamando Open(): rimane nel suo stato iniziale (IsOpen=false).
                // Se era già aperta (stanza precedentemente visitata), non la richiudiamo.
            }
            else
            {
                door.Open();
            }
        }
    }

    private void OpenExitDoors()
    {
        if (!IsServer) return;

        foreach (var door in exitDoors)
        {
            if (door != null)
                door.Open();
        }
    }

    // ─── RPC ─────────────────────────────────────────────────────────────────

    [Rpc(SendTo.Everyone)]
    private void NotifyActivatedClientRpc()
    {
        OnRoomActivated?.Invoke(this);
        LevelManager.Instance?.HandleRoomActivated(this);
        Debug.Log($"[Room] '{roomDisplayName}' → ACTIVE");
    }

    [Rpc(SendTo.Everyone)]
    private void NotifyClearedClientRpc()
    {
        OnRoomCleared?.Invoke(this);
        LevelManager.Instance?.HandleRoomCleared(this);
        Debug.Log($"[Room] '{roomDisplayName}' → CLEARED");
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Colore in base allo stato
        Gizmos.color = _state.Value switch
        {
            RoomState.Active  => new Color(1f, 0.5f, 0f, 0.3f),
            RoomState.Cleared => new Color(0f, 1f, 0f, 0.3f),
            _                 => new Color(0.5f, 0.5f, 1f, 0.2f)
        };

        if (roomBounds != null)
            Gizmos.DrawCube(roomBounds.bounds.center, roomBounds.bounds.size);
        else
            Gizmos.DrawWireCube(transform.position, Vector3.one * 10f);

        // Etichetta
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2f,
            $"{roomDisplayName}\n[{_state.Value}]");
    }
#endif
}
