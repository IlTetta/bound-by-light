using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Componente da mettere sul prefab di ogni nemico.
/// Notifica l'EnemyManager quando il NetworkObject viene despawnato,
/// permettendo al manager di aggiornare il registro dei nemici attivi.
/// </summary>
public sealed class EnemyDeathNotifier : MonoBehaviour
{
    private NetworkObject _networkObject;

    private void Awake()
    {
        _networkObject = GetComponent<NetworkObject>();
    }

    private void OnDestroy()
    {
        if (EnemyManager.Instance == null) return;
        if (_networkObject == null) return;

        EnemyManager.Instance.UnregisterEnemy(_networkObject.NetworkObjectId);
    }
}