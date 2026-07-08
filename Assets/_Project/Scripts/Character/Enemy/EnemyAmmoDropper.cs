using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Aggiunge a un prefab nemico la probabilità di droppare munizioni alla morte.
///
/// Setup prefab nemico:
///   1. Aggiungi questo componente accanto a HealthNetwork ed EnemyRewardOnDeath.
///   2. Assegna il prefab AmmoPickup al campo <see cref="ammoPickupPrefab"/>.
///   3. Registra il prefab come NetworkPrefab nelle impostazioni NGO.
///   4. Regola <see cref="dropChance"/> (0 = mai, 1 = sempre).
/// </summary>
[RequireComponent(typeof(HealthNetwork))]
public class EnemyAmmoDropper : NetworkBehaviour
{
    // Stat di design: NON serializzate. Arrivano da EnemyBaseConfig via ConfigureStats(),
    // chiamata dal brain in Awake. I valori qui sono solo il fallback senza config.
    private float dropChance = 0.35f;
    private int   ammoAmount = 6;

    [Tooltip("Riferimento al prefab: resta sul prefab del nemico, non è una stat.")]
    [SerializeField] private GameObject ammoPickupPrefab;

    /// <summary>Applica le stat dal config del nemico. Da chiamare in Awake.</summary>
    public void ConfigureStats(float newDropChance, int newAmmoAmount)
    {
        dropChance = Mathf.Clamp01(newDropChance);
        ammoAmount = Mathf.Max(0, newAmmoAmount);
    }

    private HealthNetwork _health;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        _health = GetComponent<HealthNetwork>();
        if (_health != null)
            _health.OnServerDeath += TryDrop;
    }

    public override void OnNetworkDespawn()
    {
        if (_health != null)
            _health.OnServerDeath -= TryDrop;
    }

    private void TryDrop()
    {
        if (ammoPickupPrefab == null) return;
        if (Random.value > dropChance) return; // non droppare

        var go = Instantiate(ammoPickupPrefab, transform.position, Quaternion.identity);

        var netObj = go.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError($"[EnemyAmmoDropper] '{ammoPickupPrefab.name}' non ha NetworkObject!");
            Destroy(go);
            return;
        }

        netObj.Spawn();
        go.GetComponent<AmmoPickup>()?.SetAmount(ammoAmount);
    }
}
