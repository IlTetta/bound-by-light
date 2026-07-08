using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manager server-side per i nemici.
/// 
/// Responsabilità:
///   - Registro dei nemici attivi in scena (spawn/despawn)
///   - Esposizione dei Transform dei player ai Brain (evita che ogni Brain
///     vada a pescare da ConnectedClientsList in modo indipendente)
///   - Spawn centralizzato dei nemici tramite SpawnEnemy()
///
/// È un NetworkSingleton: esiste uno solo in scena, accessibile via EnemyManager.Instance.
/// Gira solo sul server (enabled = IsServer in OnNetworkSpawn).
/// </summary>
public sealed class EnemyManager : MyGame.Core.NetworkSingleton<EnemyManager>
{
    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("Spawn")]
    [Tooltip("Prefab dei nemici disponibili. L'indice corrisponde all'EnemyType.")]
    [SerializeField] private NetworkObject[] enemyPrefabs;

    [Header("Debug")]
    [Tooltip("Scrive in Console gli eventi di spawn/unregister dei nemici.")]
    [SerializeField] private bool logSpawnEvents = true;

    [Tooltip("Disegna il contatore nemici/player in basso a sinistra. Solo in editor.")]
    [SerializeField] private bool showDebugOverlay = false;

    // ─── Stato interno (solo server) ──────────────────────────────────────────
    // Nemici attivi: NetworkObjectId → NetworkObject
    private readonly Dictionary<ulong, NetworkObject> _activeEnemies = new();

    // Cache dei player transform, aggiornata quando un client si connette/disconnette
    private readonly List<Transform> _playerTransforms = new();

    // ─── Lifecycle ────────────────────────────────────────────────────────────
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        enabled = IsServer;
        if (!IsServer) return;

        // Ascolta connessioni/disconnessioni per aggiornare la cache player
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        // Registra i player già connessi (caso in cui il manager spawna dopo i player)
        RefreshPlayerCache();
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        base.OnNetworkDespawn();
    }

    // ─── API pubblica: Player ─────────────────────────────────────────────────

    /// <summary>
    /// Restituisce i Transform dei player attualmente connessi.
    /// Lista in sola lettura — non modificarla.
    /// Solo server.
    /// </summary>
    public IReadOnlyList<Transform> PlayerTransforms => _playerTransforms;

    /// <summary>
    /// Restituisce il Transform del player più vicino a <paramref name="position"/>,
    /// o null se non ci sono player.
    /// Solo server.
    /// </summary>
    public Transform GetClosestPlayer(Vector2 position)
    {
        Transform best = null;
        float bestD = float.MaxValue;

        foreach (var t in _playerTransforms)
        {
            if (t == null) continue;
            float d = Vector2.Distance(position, t.position);
            if (d < bestD) { bestD = d; best = t; }
        }

        return best;
    }

    /// <summary>
    /// Popola <paramref name="result"/> con i Transform dei due player più vicini
    /// a <paramref name="position"/>. Il risultato ha 0, 1 o 2 elementi.
    /// Solo server.
    /// </summary>
    public void GetTwoClosestPlayers(Vector2 position, List<Transform> result)
    {
        result.Clear();

        Transform first = null, second = null;
        float distA = float.MaxValue, distB = float.MaxValue;

        foreach (var t in _playerTransforms)
        {
            if (t == null) continue;
            float d = Vector2.Distance(position, t.position);

            if (d < distA)
            {
                distB = distA; second = first;
                distA = d; first = t;
            }
            else if (d < distB)
            {
                distB = d; second = t;
            }
        }

        if (first != null) result.Add(first);
        if (second != null) result.Add(second);
    }

    // ─── API pubblica: Spawn ──────────────────────────────────────────────────

    /// <summary>
    /// Spawna un nemico del tipo indicato nella posizione data e lo registra.
    /// Solo server.
    /// </summary>
    /// <param name="prefabIndex">Indice nell'array enemyPrefabs.</param>
    /// <param name="position">Posizione di spawn.</param>
    /// <returns>Il NetworkObject spawnato, o null in caso di errore.</returns>
    public NetworkObject SpawnEnemy(int prefabIndex, Vector3 position)
    {
        if (!IsServer) return null;

        if (enemyPrefabs == null || prefabIndex < 0 || prefabIndex >= enemyPrefabs.Length)
        {
            Debug.LogError($"[EnemyManager] Prefab index {prefabIndex} non valido " +
                           $"(array size={enemyPrefabs?.Length ?? 0}).");
            return null;
        }

        var prefab = enemyPrefabs[prefabIndex];
        if (prefab == null)
        {
            Debug.LogError($"[EnemyManager] Prefab all'indice {prefabIndex} è null.");
            return null;
        }

        var go = Instantiate(prefab, position, Quaternion.identity);
        go.Spawn(true);

        RegisterEnemy(go);

        if (logSpawnEvents)
            Debug.Log($"[EnemyManager] Spawned {go.name} (id={go.NetworkObjectId}) " +
                      $"at {position}. Active enemies: {_activeEnemies.Count}");

        return go;
    }

    // ─── API pubblica: Registro nemici ────────────────────────────────────────

    /// <summary>
    /// Registra manualmente un nemico già spawnato.
    /// Utile se il nemico viene spawnato al di fuori dell'EnemyManager
    /// (es. EnemySpawner legacy in scena).
    /// Solo server.
    /// </summary>
    public void RegisterEnemy(NetworkObject enemy)
    {
        if (!IsServer || enemy == null) return;

        _activeEnemies[enemy.NetworkObjectId] = enemy;
        // La deregistrazione avviene tramite EnemyDeathNotifier.OnDestroy()
        // che chiama UnregisterEnemy() quando il GameObject viene distrutto.
    }

    /// <summary>Numero di nemici attivi in scena.</summary>
    public int ActiveEnemyCount => _activeEnemies.Count;

    /// <summary>Restituisce tutti i nemici attivi (sola lettura).</summary>
    public IReadOnlyDictionary<ulong, NetworkObject> ActiveEnemies => _activeEnemies;

    // ─── Callbacks interni ────────────────────────────────────────────────────
    private void OnClientConnected(ulong clientId)
    {
        // Piccolo delay: il PlayerObject potrebbe non essere ancora assegnato
        // nel frame esatto della callback. RefreshPlayerCache viene chiamata
        // anche dal PlayerSpawnServer dopo lo spawn, ma questa è una safety net.
        RefreshPlayerCache();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        RefreshPlayerCache();
    }

    /// <summary>
    /// Rimuove un nemico dal registro. Chiamato da EnemyDeathNotifier.OnDestroy().
    /// </summary>
    public void UnregisterEnemy(ulong networkObjectId)
    {
        if (_activeEnemies.Remove(networkObjectId) && logSpawnEvents)
            Debug.Log($"[EnemyManager] Enemy unregistered (id={networkObjectId}). " +
                      $"Active enemies: {_activeEnemies.Count}");
    }

    // ─── Utility ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Ricostruisce la cache dei player transform dai ConnectedClients.
    /// Chiamata ad ogni connect/disconnect.
    /// </summary>
    public void RefreshPlayerCache()
    {
        _playerTransforms.Clear();

        if (NetworkManager.Singleton == null) return;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
                _playerTransforms.Add(client.PlayerObject.transform);
        }
    }

#if UNITY_EDITOR
    private void OnGUI()
    {
        if (!showDebugOverlay) return;
        if (!Application.isPlaying) return;

        // Piccolo overlay debug in basso a sinistra
        int h = 20;
        GUI.Label(new Rect(10, Screen.height - h * 3, 300, h),
            $"[EnemyManager] Active enemies: {_activeEnemies.Count}");
        GUI.Label(new Rect(10, Screen.height - h * 2, 300, h),
            $"[EnemyManager] Players tracked: {_playerTransforms.Count}");
    }
#endif
}