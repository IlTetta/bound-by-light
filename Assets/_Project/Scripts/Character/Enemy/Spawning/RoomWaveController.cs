using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// ─── Data Structures ──────────────────────────────────────────────────────────

/// <summary>
/// Una voce all'interno di una wave: tipo di nemico, quanti spawnare e da dove.
/// </summary>
[Serializable]
public class WaveEntry
{
    [Tooltip("Prefab del nemico (deve avere NetworkObject + HealthNetwork).")]
    public GameObject enemyPrefab;

    [Min(1)]
    public int count = 1;

    [Tooltip("Transform del punto di spawn nella scena. " +
             "Se null, usa la posizione del RoomWaveController.")]
    public Transform spawnPoint;

    [Min(0f)]
    [Tooltip("Raggio di dispersione attorno allo spawn point. " +
             "Con count > 1 i nemici vengono distribuiti casualmente in quest'area " +
             "invece di sovrapporsi tutti nello stesso punto.")]
    public float scatterRadius = 1.2f;
}

/// <summary>
/// Una singola wave: lista di nemici da spawnare e ritardo prima dello spawn.
/// </summary>
[Serializable]
public class WaveData
{
    public string waveName = "Wave";

    [Tooltip("Secondi di attesa dopo che la wave precedente è stata eliminata.")]
    [Min(0f)]
    public float spawnDelay = 1f;

    public List<WaveEntry> entries = new();
}

// ─── Controller ───────────────────────────────────────────────────────────────

/// <summary>
/// Gestore delle wave sequenziali di una stanza.
///
/// Wave:
///   1. Aggiungi questo componente a un GameObject vuoto nella scena.
///   2. Configura la lista "waves" nell'Inspector.
///   3. Crea transform figli come punti di spawn e assegnali alle WaveEntry.
///   4. Abilita "Activate On Start" per i test, oppure chiama StartWaves()
///      dall'evento OnTriggerEnter2D di un trigger di ingresso stanza.
///
/// Completamento stanza — modalità A (auto-open):
///   • Assegna porte a "Auto Open Doors": si aprono subito al clear.
///
/// Completamento stanza — modalità B (chiave):
///   • Assegna "Key Prefab" + "Key Spawn Point" + "Key Locked Doors".
///   • La chiave spawna al clear; il player la raccoglie e sblocca le porte.
///   • I player possono poi scegliere quale porta aprire premendo E.
///
/// Ricompensa ammo al clear:
///   • Assegna prefab AmmoPickup a "Completion Ammo Prefabs" con i rispettivi
///     "Completion Ammo Spawn Points" (uno per entry, allineati per indice).
///
/// NOTA: tutta la logica gira solo sul Server (guard IsServer).
/// </summary>
public class RoomWaveController : MonoBehaviour
{
    // ─── Wave Configuration ───────────────────────────────────────────────────

    [Header("Wave Configuration")]
    [SerializeField] private List<WaveData> waves = new();

    [Tooltip("Se true, le wave partono automaticamente non appena i player richiesti sono connessi.")]
    [SerializeField] private bool activateOnStart = false;

    [Tooltip("Numero minimo di player connessi prima di avviare le wave. " +
             "1 = parte subito (utile per test in solo-host), 2 = aspetta entrambi i player.")]
    [SerializeField] private int requiredPlayers = 2;

    // ─── Room Completion — Doors ──────────────────────────────────────────────

    [Header("Room Completion — Porte auto-aperte")]
    [Tooltip("Porte che si aprono immediatamente al completamento di tutte le wave.")]
    [SerializeField] private List<RoomDoor> autoOpenDoors = new();

    [Header("Room Completion — Porta con chiave")]
    [Tooltip("Prefab della chiave (deve avere NetworkObject + RoomKey). " +
             "Se null, nessuna chiave viene spawnata.")]
    [SerializeField] private GameObject keyPrefab;

    [Tooltip("Punto di spawn della chiave. Se null, spawna sulla posizione di questo GameObject.")]
    [SerializeField] private Transform keySpawnPoint;

    [Tooltip("Porte che vengono sbloccate quando il player raccoglie la chiave.")]
    [SerializeField] private List<RoomDoor> keyLockedDoors = new();

    // ─── Room Completion — Ammo Reward ────────────────────────────────────────

    [Header("Room Completion — Ricompensa munizioni")]
    [Tooltip("Prefab AmmoPickup da spawnare al completamento (uno per elemento).")]
    [SerializeField] private List<GameObject> completionAmmoPrefabs = new();

    [Tooltip("Punti di spawn per i prefab ammo sopra (allineati per indice). " +
             "Se mancante per un elemento, usa la posizione di questo GameObject.")]
    [SerializeField] private List<Transform> completionAmmoSpawnPoints = new();

    // ─── Events ───────────────────────────────────────────────────────────────

    /// <summary>Fired sul server quando tutte le wave sono state completate.</summary>
    public event Action OnAllWavesCleared;

    // ─── State ────────────────────────────────────────────────────────────────

    private int  _currentWaveIndex  = -1;
    private int  _aliveEnemies      = 0;
    private bool _wavesStarted      = false;
    private bool _allCleared        = false;
    private bool _waitingForPlayers = false; // true mentre aspettiamo abbastanza client

    // ─── Unity ────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (NetworkManager.Singleton == null) return;

        // Ci iscriviamo a OnServerStarted per ogni sessione (host o restart).
        // Ci iscriviamo anche a OnServerStopped per resettare allo state iniziale
        // quando l'host clicca Disconnect, senza dover riavviare il gioco.
        NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
        NetworkManager.Singleton.OnServerStopped += HandleServerStopped;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnServerStarted          -= HandleServerStarted;
        NetworkManager.Singleton.OnServerStopped          -= HandleServerStopped;
        NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnectedForStart;
    }

    // Chiamato ogni volta che il server parte (anche dopo un Disconnect + re-host).
    private void HandleServerStarted()
    {
        ResetWaveState();

        if (!activateOnStart) return;

        if (NetworkManager.Singleton.ConnectedClients.Count >= requiredPlayers)
            StartWaves();
        else
            BeginWaitingForPlayers();
    }

    // Chiamato quando il server si ferma (Disconnect).
    // Il bool indica se lo shutdown era previsto (true) o per errore (false).
    private void HandleServerStopped(bool wasShutdown)
    {
        ResetWaveState();
        // HandleServerStarted si re-iscriverà alla prossima sessione (è già iscritto in Start)
    }

    private void BeginWaitingForPlayers()
    {
        if (_waitingForPlayers) return;
        _waitingForPlayers = true;
        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnectedForStart;
        Debug.Log($"[RoomWaveController] In attesa di {requiredPlayers} player " +
                  $"(connessi: {NetworkManager.Singleton.ConnectedClients.Count})...");
    }

    private void HandleClientConnectedForStart(ulong clientId)
    {
        if (NetworkManager.Singleton.ConnectedClients.Count < requiredPlayers) return;

        // Abbastanza player connessi: togliamo l'ascolto e avviamo
        NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnectedForStart;
        _waitingForPlayers = false;
        StartWaves();
    }

    /// <summary>
    /// Azzera tutto lo stato delle wave senza toccare la configurazione.
    /// Chiamato automaticamente a ogni nuovo avvio del server.
    /// </summary>
    private void ResetWaveState()
    {
        StopAllCoroutines();

        _currentWaveIndex = -1;
        _aliveEnemies     = 0;
        _wavesStarted     = false;
        _allCleared       = false;

        if (_waitingForPlayers && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnectedForStart;
            _waitingForPlayers = false;
        }

        Debug.Log($"[RoomWaveController] '{gameObject.name}': stato resettato.");
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Avvia la sequenza di wave. Da chiamare dal trigger di ingresso nella stanza.
    /// Ignorato silenziosamente sui client.
    /// </summary>
    public void StartWaves()
    {
        if (!NetworkManager.Singleton.IsServer) return;
        if (_wavesStarted) return;
        if (waves == null || waves.Count == 0)
        {
            Debug.LogWarning($"[RoomWaveController] '{gameObject.name}': nessuna wave configurata.");
            return;
        }

        _wavesStarted = true;
        StartCoroutine(SpawnWaveRoutine());
    }

    /// <summary>True se tutte le wave sono state completate.</summary>
    public bool AllCleared => _allCleared;

    /// <summary>
    /// Reset forzato: despawna i nemici vivi e azzera lo stato delle wave.
    /// Chiamato da Room.ResetToSealed() al checkpoint respawn.
    /// </summary>
    public void ForceReset()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        // Despawna tutti i nemici ancora vivi (esclusi i player).
        // GetComponent è chiamato solo due volte per oggetto e solo al momento del reset:
        // il costo è accettabile rispetto a mantenere un registro separato.
        foreach (var health in FindObjectsByType<HealthNetwork>(FindObjectsSortMode.None))
        {
            var netObj = health.GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj == null || !netObj.IsSpawned) continue;
            if (health.GetComponent<PlayerFaintHandler>() != null) continue; // skip player
            netObj.Despawn(true);
        }

        ResetWaveState();
    }

    /// <summary>
    /// Forza il completamento della stanza: uccide tutti i nemici vivi e
    /// chiude le wave come se fossero state completate normalmente.
    /// Usato dal CombatSkipButton di debug. Solo server.
    /// </summary>
    public void ForceComplete()
    {
        if (!NetworkManager.Singleton.IsServer) return;
        if (_allCleared) return;

        StopAllCoroutines();

        // Segna completato PRIMA di killare i nemici, così HandleEnemyDied
        // non prova a spawnare la wave successiva.
        _allCleared   = true;
        _wavesStarted = true;

        foreach (var health in FindObjectsByType<HealthNetwork>(FindObjectsSortMode.None))
        {
            if (health.IsDead) continue;
            if (health.GetComponent<PlayerFaintHandler>() != null) continue;
            health.ApplyDamageServer(health.MaxHealth + 1, Vector2.zero, 0);
        }

        Debug.Log($"[RoomWaveController] '{gameObject.name}': completamento forzato (skip button).");
        OnAllWavesCleared?.Invoke();
        HandleRoomCompletion();
    }

    // ─── Wave Logic ───────────────────────────────────────────────────────────

    private IEnumerator SpawnWaveRoutine()
    {
        _currentWaveIndex++;

        // Tutte le wave completate → gestione stanza
        if (_currentWaveIndex >= waves.Count)
        {
            _allCleared = true;
            Debug.Log($"[RoomWaveController] '{gameObject.name}': tutte le wave completate!");
            OnAllWavesCleared?.Invoke();
            HandleRoomCompletion();
            yield break;
        }

        WaveData wave = waves[_currentWaveIndex];
        Debug.Log($"[RoomWaveController] Preparazione '{wave.waveName}' (delay {wave.spawnDelay}s)...");

        if (wave.spawnDelay > 0f)
            yield return new WaitForSeconds(wave.spawnDelay);

        _aliveEnemies = 0;
        int spawned   = 0;

        foreach (WaveEntry entry in wave.entries)
        {
            if (entry.enemyPrefab == null)
            {
                Debug.LogWarning($"[RoomWaveController] Wave '{wave.waveName}': " +
                                 "WaveEntry con enemyPrefab null — saltata.");
                continue;
            }

            for (int i = 0; i < entry.count; i++)
            {
                Vector3 origin = entry.spawnPoint != null
                    ? entry.spawnPoint.position
                    : transform.position;

                // Distribuzione circolare uniforme: angoli equidistanti garantiscono
                // che nessuna coppia di nemici sia più vicina di 2*r*sin(π/count).
                Vector3 spawnPos = ScatteredPosition(origin, entry.scatterRadius, i, entry.count);

                GameObject go = Instantiate(entry.enemyPrefab, spawnPos, Quaternion.identity);

                NetworkObject netObj = go.GetComponent<NetworkObject>();
                if (netObj == null)
                {
                    Debug.LogError($"[RoomWaveController] '{entry.enemyPrefab.name}' " +
                                   "non ha NetworkObject — distrutto.");
                    Destroy(go);
                    continue;
                }

                netObj.Spawn();
                spawned++;

                HealthNetwork health = go.GetComponent<HealthNetwork>();
                if (health != null)
                {
                    _aliveEnemies++;
                    health.OnServerDeath += HandleEnemyDied;
                }
                else
                {
                    Debug.LogWarning($"[RoomWaveController] '{entry.enemyPrefab.name}' " +
                                     "non ha HealthNetwork — non tracciato dalla wave.");
                }
            }
        }

        Debug.Log($"[RoomWaveController] Wave '{wave.waveName}': " +
                  $"{spawned} spawnati, {_aliveEnemies} tracciati.");

        if (_aliveEnemies == 0)
            StartCoroutine(SpawnWaveRoutine());
    }

    /// <summary>
    /// Distribuisce i nemici su un cerchio a raggi equidistanti attorno all'origine.
    /// count=1 → origin esatta; count>1 → angoli a 360°/count l'uno dall'altro.
    /// Un piccolo jitter casuale (±15°) evita file troppo geometriche.
    /// </summary>
    private static Vector3 ScatteredPosition(Vector3 origin, float radius, int index, int total)
    {
        if (radius <= 0f || total <= 1) return origin;

        float baseAngle  = (2f * Mathf.PI / total) * index;
        float jitter     = UnityEngine.Random.Range(-0.26f, 0.26f); // ±15°
        float angle      = baseAngle + jitter;

        Vector2 offset   = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        return origin + new Vector3(offset.x, 0f, offset.y); // XZ plane per il 3D
    }

    private void HandleEnemyDied()
    {
        _aliveEnemies--;
        Debug.Log($"[RoomWaveController] Nemico eliminato. Rimasti: {_aliveEnemies}");

        // Se ForceComplete() è già in corso, non avviare la wave successiva
        if (_aliveEnemies <= 0 && !_allCleared)
            StartCoroutine(SpawnWaveRoutine());
    }

    // ─── Room Completion ──────────────────────────────────────────────────────

    private void HandleRoomCompletion()
    {
        SpawnAmmoRewards();
        OpenAutoDoors();
        SpawnKey();
    }

    /// <summary>Spawna i pickup ammo configurati come ricompensa di fine stanza.</summary>
    private void SpawnAmmoRewards()
    {
        for (int i = 0; i < completionAmmoPrefabs.Count; i++)
        {
            GameObject prefab = completionAmmoPrefabs[i];
            if (prefab == null) continue;

            Vector3 pos = (i < completionAmmoSpawnPoints.Count && completionAmmoSpawnPoints[i] != null)
                ? completionAmmoSpawnPoints[i].position
                : transform.position;

            GameObject go = Instantiate(prefab, pos, Quaternion.identity);

            NetworkObject netObj = go.GetComponent<NetworkObject>();
            if (netObj != null)
                netObj.Spawn();
            else
            {
                Debug.LogError($"[RoomWaveController] Ammo reward prefab '{prefab.name}' " +
                               "non ha NetworkObject!");
                Destroy(go);
            }
        }
    }

    /// <summary>Apre immediatamente le porte ad apertura automatica.</summary>
    private void OpenAutoDoors()
    {
        foreach (var door in autoOpenDoors)
        {
            if (door != null)
                door.Open();
        }
    }

    /// <summary>
    /// Spawna la chiave se configurata. La chiave, quando raccolta,
    /// sblocca le porte in <see cref="keyLockedDoors"/>.
    /// </summary>
    private void SpawnKey()
    {
        if (keyPrefab == null) return;
        if (keyLockedDoors.Count == 0)
        {
            Debug.LogWarning("[RoomWaveController] keyPrefab assegnato ma keyLockedDoors è vuota.");
            return;
        }

        Vector3 keyPos = keySpawnPoint != null ? keySpawnPoint.position : transform.position;
        GameObject keyGo = Instantiate(keyPrefab, keyPos, Quaternion.identity);

        NetworkObject netObj = keyGo.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError($"[RoomWaveController] keyPrefab '{keyPrefab.name}' non ha NetworkObject!");
            Destroy(keyGo);
            return;
        }

        netObj.Spawn();

        RoomKey key = keyGo.GetComponent<RoomKey>();
        if (key != null)
            key.SetDoors(new List<RoomDoor>(keyLockedDoors));
        else
            Debug.LogError($"[RoomWaveController] keyPrefab '{keyPrefab.name}' non ha RoomKey!");
    }
}
