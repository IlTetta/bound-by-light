using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Bootstrapper per la scena di test TetherCombat.
/// - Spawna GameManager e EnemyManager come NetworkObject
/// - Aspetta che entrambi i player siano connessi
/// - Spawna il Tether e lo collega ai due player
/// </summary>
public class TetherCombatTestBootstrapper : MonoBehaviour
{
    [Header("Network Objects da spawnare")]
    [SerializeField] private NetworkObject gameManagerPrefab;  // prefab con GameManager
    [SerializeField] private NetworkObject enemyManagerPrefab; // prefab con EnemyManager

    [Header("Nemico")]
    [SerializeField] private NetworkObject enemyPrefab;
    [SerializeField] private Vector3 enemySpawnPosition = new Vector3(3f, 0f, 0f);

    [Header("Tether")]
    [SerializeField] private NetworkObject tetherPrefab;

    //[Header("Nemico in scena")]
    //[SerializeField] private NetworkObject enemyInScene; // trascina Enemy_Melee dalla scena

    private bool _tetherSpawned = false;
    private bool _managersSpawned = false;

    private void Start()
    {
        // Solo il server gestisce il bootstrap
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[Bootstrapper] NetworkManager.Singleton non trovato. " +
                           "Assicurati che NetworkManager sia in scena.");
            return;
        }

        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    private void OnServerStarted()
    {
        if (!NetworkManager.Singleton.IsServer) return;
        if (_managersSpawned) return;
        _managersSpawned = true;

        SpawnObject(gameManagerPrefab, "GameManager");
        SpawnObject(enemyManagerPrefab, "EnemyManager");
        SpawnObject(enemyPrefab, "Enemy", enemySpawnPosition);
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        if (_tetherSpawned) return;

        // Aspetta che ci siano esattamente 2 client (host + client)
        if (NetworkManager.Singleton.ConnectedClients.Count < 2) return;

        SpawnTether();
    }

    private void SpawnObject(NetworkObject prefab, string label, Vector3 position = default)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"[Bootstrapper] {label} prefab non assegnato.");
            return;
        }
        var instance = Instantiate(prefab, position, Quaternion.identity);
        instance.Spawn();
        Debug.Log($"[Bootstrapper] {label} spawnato.");
    }

    private void SpawnTether()
    {
        _tetherSpawned = true;

        // Recupera i NetworkObject dei due player
        NetworkObject playerA = null, playerB = null;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (playerA == null) playerA = client.PlayerObject;
            else if (playerB == null) { playerB = client.PlayerObject; break; }
        }

        if (playerA == null || playerB == null)
        {
            Debug.LogError("[Bootstrapper] Non riesco a trovare entrambi i player.");
            return;
        }

        var tether = Instantiate(tetherPrefab, Vector3.zero, Quaternion.identity);
        tether.Spawn();

        var manager = tether.GetComponent<TetherManager>();
        manager.PlayerARef.Value = playerA;
        manager.PlayerBRef.Value = playerB;

        Debug.Log("[Bootstrapper] Tether spawnato e collegato ai player.");
    }
}