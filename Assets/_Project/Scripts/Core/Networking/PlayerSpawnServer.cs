using System;
using System.Collections;
using System.Collections.Generic;
using MyGame.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gestisce lo spawn dei manager, dei player e del tether in modo demo-ready.
///
/// Setup scena:
///   1. Assegna i prefab nei campi Inspector.
///   2. Crea 2 Transform figli come spawn point player e assegnali a Spawn Points.
///   3. Disabilita/rimuovi il vecchio Bootstrapper — questo script lo sostituisce.
///   4. Assegna playerPrefabs[0] = PlayerPrefab_Twin1, playerPrefabs[1] = PlayerPrefab_Twin2.
///      Il primo client che si connette riceve Twin1, il secondo Twin2.
///   5. Registra ENTRAMBI i prefab nella lista NetworkPrefabs del NetworkManager.
///
/// Flusso:
///   Server avvia → SpawnManagers() → OnClientConnected (x2) → SpawnTether()
///   Disconnect   → reset automatico → tutto ripronto per una nuova sessione
/// </summary>
public sealed class PlayerSpawnServer : MonoBehaviour
{
    [Header("Manager Prefabs")]
    [Tooltip("Prefab del GameManager (NetworkObject). Spawnato quando il server parte.")]
    [SerializeField] private NetworkObject gameManagerPrefab;

    [Tooltip("Prefab dell'EnemyManager (NetworkObject). Spawnato quando il server parte.")]
    [SerializeField] private NetworkObject enemyManagerPrefab;

    [Header("Player")]
    [Tooltip("Prefab per ciascun player in ordine di connessione. Indice 0 = Twin1 (primo client), Indice 1 = Twin2 (secondo client).")]
    [SerializeField] private NetworkObject[] playerPrefabs;

    [Tooltip("Spawn point per i player (indice = ordine di connessione).")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("Tether")]
    [Tooltip("Se true, spawna il tether non appena entrambi i player sono in scena.")]
    [SerializeField] private bool autoSpawnTether = true;

    [Tooltip("Numero di player richiesti prima di spawnare il tether (di solito 2).")]
    [SerializeField] private int requiredPlayersForTether = 2;

    // Player spawnati nella sessione corrente
    private readonly List<NetworkObject> _spawnedPlayers = new();

    // Mappa clientId → indice prefab (per riconoscere quale Twin usare al reconnect)
    private readonly Dictionary<ulong, int> _clientPrefabIndex = new();

    // Stato reconnect
    private bool              _waitingForReconnect;
    private PlayerSessionData _savedSessionData;

    /// <summary>Fired quando un player si è riconnesso con successo e il suo stato è stato ripristinato.</summary>
    public event Action OnPlayerReconnected;

    // ─── Unity ────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.OnServerStarted          += HandleServerStarted;
        NetworkManager.Singleton.OnServerStopped          += HandleServerStopped;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

        // La scena è stata caricata dopo che il server era già avviato (es. da MainMenu):
        // riproduco manualmente gli eventi già scattati.
        if (NetworkManager.Singleton.IsServer)
        {
            HandleServerStarted();
            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
                OnClientConnected(clientId);
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.OnServerStarted          -= HandleServerStarted;
        NetworkManager.Singleton.OnServerStopped          -= HandleServerStopped;
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    // ─── Handlers ─────────────────────────────────────────────────────────────

    private void HandleServerStarted()
    {
        _spawnedPlayers.Clear();
        _clientPrefabIndex.Clear();
        _waitingForReconnect = false;
        SpawnManager(gameManagerPrefab,  "GameManager");
        SpawnManager(enemyManagerPrefab, "EnemyManager");
    }

    private void HandleServerStopped(bool wasShutdown)
    {
        _spawnedPlayers.Clear();
        _clientPrefabIndex.Clear();
        _waitingForReconnect = false;
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[PlayerSpawnServer] OnClientConnected clientId={clientId} " +
                  $"IsServer={NetworkManager.Singleton.IsServer}");

        if (!NetworkManager.Singleton.IsServer) return;

        // Se stiamo aspettando un reconnect, spawn con stato salvato
        if (_waitingForReconnect)
        {
            _waitingForReconnect = false;
            StartCoroutine(SpawnReconnectedPlayerRoutine(clientId, _savedSessionData));
            return;
        }

        if (playerPrefabs == null || playerPrefabs.Length == 0)
        {
            Debug.LogError("[PlayerSpawnServer] playerPrefabs non assegnati.");
            return;
        }

        // Seleziona il prefab in base all'ordine di connessione (0 = Twin1, 1 = Twin2)
        int prefabIndex = _spawnedPlayers.Count % playerPrefabs.Length;
        NetworkObject selectedPrefab = playerPrefabs[prefabIndex];

        if (selectedPrefab == null)
        {
            Debug.LogError($"[PlayerSpawnServer] playerPrefabs[{prefabIndex}] non assegnato.");
            return;
        }

        // Posizione di spawn in base all'ordine di connessione
        Vector3    pos = Vector3.zero;
        Quaternion rot = Quaternion.identity;

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int spawnIndex = _spawnedPlayers.Count % spawnPoints.Length;
            pos = spawnPoints[spawnIndex].position;
            rot = spawnPoints[spawnIndex].rotation;
        }

        var player = Instantiate(selectedPrefab, pos, rot);
        player.SpawnAsPlayerObject(clientId, destroyWithScene: true);
        _spawnedPlayers.Add(player);
        _clientPrefabIndex[clientId] = prefabIndex;

        Debug.Log($"[PlayerSpawnServer] Spawnato playerPrefabs[{prefabIndex}] ({selectedPrefab.name}) per clientId={clientId}.");
        Debug.Log($"[PlayerSpawnServer] Totale player spawnati: {_spawnedPlayers.Count}");

        // Spawna il tether quando abbastanza player sono pronti
        if (autoSpawnTether && _spawnedPlayers.Count >= requiredPlayersForTether)
            TrySpawnTether();
    }

    // ─── Reconnect API ────────────────────────────────────────────────────────

    /// <summary>
    /// Chiamato da DisconnectionHandler quando il joiner si disconnette.
    /// Salva lo stato e predispone lo spawn per il reconnect.
    /// </summary>
    public void BeginWaitForReconnect(ulong disconnectedClientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        NetworkObject playerObj = NetworkManager.Singleton.SpawnManager
            .GetPlayerNetworkObject(disconnectedClientId);

        if (playerObj == null)
        {
            Debug.LogWarning("[PlayerSpawnServer] Impossibile trovare NetworkObject del joiner per salvare lo stato.");
            return;
        }

        int prefabIndex = _clientPrefabIndex.TryGetValue(disconnectedClientId, out int idx) ? idx : 1;
        _savedSessionData    = PlayerSessionData.Capture(playerObj, prefabIndex);
        _waitingForReconnect = true;

        Debug.Log($"[PlayerSpawnServer] Stato joiner salvato. Posizione={_savedSessionData.Position}, " +
                  $"HP={_savedSessionData.Health}, W0={_savedSessionData.Weapon0Acquired}, W1={_savedSessionData.Weapon1Acquired}");
    }

    /// <summary>Annulla l'attesa reconnect (es. timeout scaduto).</summary>
    public void CancelReconnectWait()
    {
        _waitingForReconnect = false;
    }

    private IEnumerator SpawnReconnectedPlayerRoutine(ulong clientId, PlayerSessionData data)
    {
        int prefabIndex = data.PrefabIndex;
        if (prefabIndex < 0 || prefabIndex >= playerPrefabs.Length) prefabIndex = 1;

        NetworkObject selectedPrefab = playerPrefabs[prefabIndex];
        if (selectedPrefab == null)
        {
            Debug.LogError("[PlayerSpawnServer] Prefab mancante per il reconnect.");
            yield break;
        }

        // Se esiste un checkpoint più recente, usa quello come posizione di spawn
        if (CheckpointManager.Instance != null && CheckpointManager.Instance.HasCheckpoint)
            data = CheckpointManager.Instance.GetCheckpointData(prefabIndex);

        // Spawna alla posizione checkpoint (o ultima posizione salvata)
        var player = Instantiate(selectedPrefab, data.Position, Quaternion.identity);
        player.SpawnAsPlayerObject(clientId, destroyWithScene: true);
        _spawnedPlayers.Add(player);
        _clientPrefabIndex[clientId] = prefabIndex;

        // Attende un frame per dare tempo a OnNetworkSpawn di girare su tutti i componenti
        yield return null;

        // Ripristina salute
        player.GetComponent<HealthNetwork>()?.RestoreHealth(data.Health);

        // Ripristina armi (server-side + ClientRpc all'owner per la UI)
        player.GetComponent<PlayerController>()?.RestoreWeaponStateServer(
            data.Weapon0Acquired, data.Weapon1Acquired, data.ActiveWeaponIndex);

        // Ripristina munizioni
        var ctrl = player.GetComponent<PlayerController>();
        ctrl?.GetAmmo(0)?.RestoreAmmo(data.AmmoLoaded0, data.AmmoAvailable0);
        ctrl?.GetAmmo(1)?.RestoreAmmo(data.AmmoLoaded1, data.AmmoAvailable1);

        // Ripristina scudo
        if (data.HasShield)
            player.GetComponent<PlayerShield>()?.Activate();

        // Ripristina medikit
        player.GetComponent<PlayerMedikit>()?.RestoreMedikits(data.MedikitCount);

        // Ripristina bond ability
        if (data.BondAbilityUnlocked && GameManager.Instance != null)
            GameManager.Instance.UnlockBondAbility();

        // Teletrasporta anche l'host al checkpoint
        if (CheckpointManager.Instance != null && CheckpointManager.Instance.HasCheckpoint)
        {
            Vector3 checkpointPos = data.Position; // già aggiornata al checkpoint
            foreach (var c in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (c.ClientId == clientId) continue; // joiner già spawnato lì
                if (c.PlayerObject == null) continue;

                var cc = c.PlayerObject.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                c.PlayerObject.transform.position = checkpointPos;
                if (cc != null) cc.enabled = true;

                // Ripristina stato host dal checkpoint
                var hostData = CheckpointManager.Instance.GetCheckpointData(0);
                var hostCtrl = c.PlayerObject.GetComponent<PlayerController>();
                hostCtrl?.RestoreWeaponStateServer(hostData.Weapon0Acquired, hostData.Weapon1Acquired, hostData.ActiveWeaponIndex);
                hostCtrl?.GetAmmo(0)?.RestoreAmmo(hostData.AmmoLoaded0, hostData.AmmoAvailable0);
                hostCtrl?.GetAmmo(1)?.RestoreAmmo(hostData.AmmoLoaded1, hostData.AmmoAvailable1);
                if (hostData.HasShield)
                    c.PlayerObject.GetComponent<PlayerShield>()?.Activate();
            }
        }

        Debug.Log($"[PlayerSpawnServer] Joiner riconnesso, entrambi i player al checkpoint (clientId={clientId}).");

        OnPlayerReconnected?.Invoke();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void SpawnManager(NetworkObject prefab, string label)
    {
        if (prefab == null) return;

        var go = Instantiate(prefab);
        go.Spawn();
        Debug.Log($"[PlayerSpawnServer] {label} spawnato.");
    }

    private void TrySpawnTether()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("[PlayerSpawnServer] GameManager non ancora disponibile — tether non spawnato.");
            return;
        }

        GameManager.Instance.SpawnTether(_spawnedPlayers[0], _spawnedPlayers[1]);
        Debug.Log("[PlayerSpawnServer] Tether spawnato e collegato ai player.");
    }
}
