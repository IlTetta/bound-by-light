using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gestore del livello completo.
///
/// Responsabilità:
///   • Registra tutte le Room della scena
///   • Traccia la stanza corrente e la cronologia
///   • Gestisce il sistema a N chiavi (porta {3} del diagramma)
///   • Notifica la CoopCamera dei bounds della stanza attiva
///   • Fornisce eventi globali per UI e altri sistemi
///
/// Setup:
///   1. Aggiungi questo componente al prefab GameManager (o a un GameObject dedicato).
///   2. Le Room si registrano automaticamente in OnNetworkSpawn.
///   3. Collega opzionalmente le "multiKeyDoors" nell'Inspector.
/// </summary>
public class LevelManager : NetworkBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static LevelManager Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Porte a chiave multipla")]
    [Tooltip("Porte che si aprono solo raccogliendo N chiavi nel livello. " +
             "Configurale anche in MultiKeyDoor per il numero richiesto.")]
    [SerializeField] private List<MultiKeyDoor> multiKeyDoors = new();

    [Header("Debug")]
    [SerializeField] private bool verboseLog = true;

    // ─── Stato ────────────────────────────────────────────────────────────────

    private readonly Dictionary<string, Room> _rooms = new();

    private Room _currentRoom;

    private readonly NetworkVariable<int> _totalKeysCollected = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ─── Proprietà pubbliche ──────────────────────────────────────────────────

    public Room CurrentRoom    => _currentRoom;
    public int  KeysCollected  => _totalKeysCollected.Value;

    // ─── NGO ──────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public override void OnNetworkSpawn()
    {
        _totalKeysCollected.OnValueChanged += OnKeysChanged;

        // Auto-discover tutte le Room nella scena
        var allRooms = FindObjectsByType<Room>(FindObjectsSortMode.None);
        foreach (var room in allRooms)
            RegisterRoom(room);

        Log($"LevelManager avviato: {_rooms.Count} stanze registrate.");
    }

    public override void OnNetworkDespawn()
    {
        _totalKeysCollected.OnValueChanged -= OnKeysChanged;
        _rooms.Clear();
    }

    // ─── Registrazione stanze ─────────────────────────────────────────────────

    public void RegisterRoom(Room room)
    {
        if (room == null || string.IsNullOrEmpty(room.RoomId)) return;

        if (_rooms.TryAdd(room.RoomId, room))
            Log($"Stanza registrata: '{room.RoomId}'");
        else
            Debug.LogWarning($"[LevelManager] RoomId duplicato: '{room.RoomId}'. " +
                             "Ogni stanza deve avere un ID univoco.");
    }

    public Room GetRoom(string id) => _rooms.TryGetValue(id, out var r) ? r : null;

    // ─── Callback dalle Room ──────────────────────────────────────────────────

    /// <summary>Chiamato da Room (via RPC) quando diventa Active.</summary>
    public void HandleRoomActivated(Room room)
    {
        _currentRoom = room;
        Log($"Stanza corrente → '{room.RoomDisplayName}'");
        UpdateCameraBounds(room);
    }

    /// <summary>Chiamato da Room (via RPC) quando viene Cleared.</summary>
    public void HandleRoomCleared(Room room)
    {
        Log($"Stanza '{room.RoomDisplayName}' completata!");
    }

    // ─── Sistema chiavi ───────────────────────────────────────────────────────

    /// <summary>
    /// Registra una chiave raccolta nel livello.
    /// Chiamare da RoomKey dopo la raccolta. Solo server.
    /// </summary>
    public void RegisterKeyCollected()
    {
        if (!IsServer) return;
        _totalKeysCollected.Value++;
        Log($"Chiave raccolta! Totale nel livello: {_totalKeysCollected.Value}");
    }

    private void OnKeysChanged(int previous, int current)
    {
        foreach (var door in multiKeyDoors)
        {
            if (door == null) continue;
            if (current >= door.KeysRequired && !door.IsUnlocked.Value)
            {
                if (IsServer) door.UnlockWithKeys();
            }
        }
    }

    // ─── Camera bounds ────────────────────────────────────────────────────────

    private void UpdateCameraBounds(Room room)
    {
        // Estensione futura: clampa la camera ai bounds della stanza attiva.
        // if (CoopCameraController.Instance != null)
        //     CoopCameraController.Instance.SetRoomBounds(room.RoomBoundsWorld);
    }

    // ─── Utility ─────────────────────────────────────────────────────────────

    private void Log(string msg)
    {
        if (verboseLog) Debug.Log($"[LevelManager] {msg}");
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_currentRoom == null) return;
        Gizmos.color = Color.yellow;
        var b = _currentRoom.RoomBoundsWorld;
        Gizmos.DrawWireCube(b.center, b.size);
    }
#endif
}
