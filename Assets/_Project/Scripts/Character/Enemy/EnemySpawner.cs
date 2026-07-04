using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Spawner legacy: rimane in scena per comodità (ContextMenu, spawn points configurabili).
/// Delega però lo spawn all'EnemyManager in modo che il nemico venga registrato
/// nel registro centrale.
///
/// Si dovrà rimuovere questo componente e chiamare
/// EnemyManager.Instance.SpawnEnemy() direttamente dal LevelManager.
/// </summary>
public sealed class EnemySpawner : NetworkBehaviour
{
    [Tooltip("Indice del prefab nell'array EnemyManager.enemyPrefabs.")]
    [SerializeField] private int enemyPrefabIndex = 0;

    [SerializeField] private Transform[] spawnPoints;

    [ContextMenu("Spawn Enemy (Server Only)")]
    public void SpawnEnemy()
    {
        if (!IsServer) return;

        if (EnemyManager.Instance == null)
        {
            Debug.LogError("[EnemySpawner] EnemyManager.Instance non trovato. " +
                           "Assicurati che EnemyManager sia in scena e spawnato.");
            return;
        }

        Vector3 pos = spawnPoints != null && spawnPoints.Length > 0
            ? spawnPoints[0].position
            : transform.position;

        EnemyManager.Instance.SpawnEnemy(enemyPrefabIndex, pos);
    }

    /// <summary>Spawna il nemico in uno spawn point specifico per indice.</summary>
    public void SpawnEnemyAt(int spawnPointIndex)
    {
        if (!IsServer) return;
        if (EnemyManager.Instance == null) return;

        Vector3 pos = (spawnPoints != null && spawnPointIndex < spawnPoints.Length)
            ? spawnPoints[spawnPointIndex].position
            : transform.position;

        EnemyManager.Instance.SpawnEnemy(enemyPrefabIndex, pos);
    }
}