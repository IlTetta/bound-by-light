using System.Collections;
using System.Collections.Generic;
using MyGame.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gestisce i checkpoint in-session.
///
/// Checkpoint:
///   - Viene aggiornato ogni volta che una stanza viene completata (cleared).
///   - Quando entrambi i player muoiono → respawn all'ultimo checkpoint.
///   - Fornisce la posizione di respawn a PlayerSpawnServer per il reconnect.
///
/// Setup scena:
///   1. Aggiungi questo componente a un GameObject in GameManagers.
///   2. Assegna startRespawnPoint: il punto di spawn iniziale (Garden).
///   3. Assegna playerSpawnServer.
/// </summary>
public class CheckpointManager : NetworkSingleton<CheckpointManager>
{
    [Header("Riferimenti")]
    [Tooltip("Punto di respawn iniziale (Garden). Usato come primo checkpoint.")]
    [SerializeField] private Transform startRespawnPoint;

    [SerializeField] private PlayerSpawnServer playerSpawnServer;

    [Header("Config")]
    [Tooltip("Secondi di attesa prima del respawn (per dare feedback visivo).")]
    [SerializeField] private float respawnDelay = 2f;

    // ─── Stato checkpoint ─────────────────────────────────────────────────────

    private string          _checkpointRoomId   = "garden";
    private Vector3         _checkpointPosition;
    private PlayerSessionData _player0Data;
    private PlayerSessionData _player1Data;
    private readonly List<string> _clearedRoomIds = new();

    // Ultima stanza in cui i player sono entrati (per resettarla al respawn anche
    // se si è auto-completata a Cleared).
    private Room _currentRoom;

    private bool _respawnInProgress;

    // ─── NGO ─────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _checkpointPosition = startRespawnPoint != null
            ? startRespawnPoint.position
            : Vector3.zero;

        if (!IsServer) return;

        // Ascolta l'attivazione e il clear di tutte le stanze
        foreach (var room in FindObjectsByType<Room>(FindObjectsSortMode.None))
        {
            room.OnRoomActivated += HandleRoomActivated;
            room.OnRoomCleared   += HandleRoomCleared;
        }
    }

    public override void OnNetworkDespawn()
    {
        foreach (var room in FindObjectsByType<Room>(FindObjectsSortMode.None))
        {
            room.OnRoomActivated -= HandleRoomActivated;
            room.OnRoomCleared   -= HandleRoomCleared;
        }

        base.OnNetworkDespawn();
    }

    private void HandleRoomActivated(Room room)
    {
        if (!IsServer) return;
        _currentRoom = room;
    }

    // ─── Checkpoint update ────────────────────────────────────────────────────

    private void HandleRoomCleared(Room room)
    {
        if (!IsServer) return;

        _checkpointRoomId = room.RoomId;
        _clearedRoomIds.Add(room.RoomId);

        // Posizione di respawn: preferisci lo spawn point della stanza (punto sicuro
        // definito dal designer). La media delle posizioni dei player è inaffidabile
        // (se uno è vicino all'uscita, il respawn finisce nel vuoto tra le stanze).
        if (room.PlayerSpawnPoint != null)
        {
            _checkpointPosition = room.PlayerSpawnPoint.position;
        }
        else
        {
            Vector3 avgPos = GetAveragePlayerPosition();
            if (avgPos != Vector3.zero)
                _checkpointPosition = avgPos;
            Debug.LogWarning($"[CheckpointManager] La stanza '{room.RoomId}' non ha un " +
                             $"PlayerSpawnPoint assegnato: uso la media delle posizioni " +
                             $"(può cadere in punti morti). Assegnalo nell'Inspector della Room.");
        }

        // Cattura lo stato corrente di entrambi i player
        CapturePlayerStates();

        Debug.Log($"[CheckpointManager] Checkpoint aggiornato → '{_checkpointRoomId}' @ {_checkpointPosition}");
    }

    private void CapturePlayerStates()
    {
        // Usa il nome del GameObject (come GetSnapshotForPlayer) invece dell'indice di lista,
        // perché l'ordine di ConnectedClientsList non è garantito essere stabile tra chiamate.
        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            bool isTwin1 = client.PlayerObject.name.IndexOf("Twin1", System.StringComparison.OrdinalIgnoreCase) >= 0;
            int prefabIndex = isTwin1 ? 0 : 1;

            var data = PlayerSessionData.Capture(client.PlayerObject, prefabIndex);
            data.Position = _checkpointPosition;

            if (prefabIndex == 0) _player0Data = data;
            else                  _player1Data = data;
        }
    }

    // ─── Respawn ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Chiamato da GameOverHandler quando entrambi i player sono fainted.
    /// Solo server.
    /// </summary>
    public void TriggerRespawn()
    {
        if (!IsServer || _respawnInProgress) return;
        StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        _respawnInProgress = true;

        yield return new WaitForSeconds(respawnDelay);

        // 1. Trova la stanza attiva (non cleared) e resettala
        ResetActiveRoom();

        // 2. Reviva e ripristina tutti i player
        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            var faint  = client.PlayerObject.GetComponent<PlayerFaintHandler>();
            var health = client.PlayerObject.GetComponent<HealthNetwork>();
            var ctrl   = client.PlayerObject.GetComponent<PlayerController>();
            var shield = client.PlayerObject.GetComponent<PlayerShield>();

            // Reviva dal faint
            faint?.ForceRevive(health?.MaxHealth ?? 100);

            // Ripristina salute al massimo
            health?.RestoreHealth(health.MaxHealth);

            // Teletrasporta al checkpoint.
            // Il NetworkTransform del player è owner-authority: il teleport deve avvenire
            // sull'owner (via RPC), altrimenti il joiner sovrascrive la posizione.
            var motor = client.PlayerObject.GetComponent<PlayerMovementMotor3D>();
            if (motor != null)
            {
                motor.TeleportFromServer(_checkpointPosition);
            }
            else
            {
                // Fallback (player senza motor 3D): teleport diretto server-side
                var cc = client.PlayerObject.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                client.PlayerObject.transform.position = _checkpointPosition;
                if (cc != null) cc.enabled = true;
            }

            // Ripristina armi e munizioni dallo snapshot checkpoint
            PlayerSessionData snap = GetSnapshotForPlayer(client.PlayerObject);
            ctrl?.RestoreWeaponStateServer(snap.Weapon0Acquired, snap.Weapon1Acquired, snap.ActiveWeaponIndex);
            ctrl?.GetAmmo(0)?.RestoreAmmo(snap.AmmoLoaded0, snap.AmmoAvailable0);
            ctrl?.GetAmmo(1)?.RestoreAmmo(snap.AmmoLoaded1, snap.AmmoAvailable1);

            // Ripristina scudo
            if (shield != null && snap.HasShield)
                shield.Activate();

            // Ripristina medikit
            client.PlayerObject.GetComponent<PlayerMedikit>()?.RestoreMedikits(snap.MedikitCount);
        }

        // 3. Ripristina bond ability se era sbloccata al checkpoint
        if (_player0Data.BondAbilityUnlocked && GameManager.Instance != null)
            GameManager.Instance.UnlockBondAbility();

        _respawnInProgress = false;
        Debug.Log($"[CheckpointManager] Respawn completato al checkpoint '{_checkpointRoomId}'.");

        // Nascondi la schermata Game Over rimettendo lo stato in Hub
        GameManager.Instance?.ChangeState(GameManager.GameState.Hub);
    }

    private void ResetActiveRoom()
    {
        foreach (var room in FindObjectsByType<Room>(FindObjectsSortMode.None))
        {
            if (room.State == RoomState.Active)
            {
                room.ResetToSealed();
                Debug.Log($"[CheckpointManager] Stanza '{room.RoomId}' resettata a Sealed.");
            }
        }

        // Resetta anche l'ultima stanza in cui i player sono entrati, anche se
        // si è auto-completata a Cleared (es. stanza senza RoomWaveController che
        // al primo ingresso passa subito a Cleared). Non tocca la stanza del
        // checkpoint (dove i player respawnano).
        if (_currentRoom != null
            && _currentRoom.State != RoomState.Active   // già gestita sopra
            && _currentRoom.RoomId != _checkpointRoomId)
        {
            _currentRoom.ResetToSealed();
            Debug.Log($"[CheckpointManager] Stanza corrente '{_currentRoom.RoomId}' resettata a Sealed.");
        }
    }

    private PlayerSessionData GetSnapshotForPlayer(NetworkObject playerObj)
    {
        // Cerca il prefabIndex dal controller per determinare quale snapshot usare
        // Twin1 = indice 0, Twin2 = indice 1
        // Semplificazione: usa il nome del GO
        if (playerObj.name.Contains("Twin1") || playerObj.name.Contains("twin1"))
            return _player0Data;
        return _player1Data;
    }

    private Vector3 GetAveragePlayerPosition()
    {
        Vector3 sum   = Vector3.zero;
        int     count = 0;
        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            sum += client.PlayerObject.transform.position;
            count++;
        }
        return count > 0 ? sum / count : Vector3.zero;
    }

    // ─── API per PlayerSpawnServer (reconnect) ────────────────────────────────

    /// <summary>
    /// Restituisce i dati di sessione salvati all'ultimo checkpoint per il reconnect.
    /// prefabIndex: 0 = Twin1, 1 = Twin2.
    /// </summary>
    public PlayerSessionData GetCheckpointData(int prefabIndex)
    {
        var data = prefabIndex == 0 ? _player0Data : _player1Data;
        data.Position = _checkpointPosition;
        return data;
    }

    public bool HasCheckpoint => _clearedRoomIds.Count > 0;
}
