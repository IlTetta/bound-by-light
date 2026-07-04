using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Pickup scudo. Uno per player: se il player ha già lo scudo, il pickup rimane a terra.
/// Al momento della raccolta attiva PlayerShield sul player collezionista.
/// </summary>
public class ShieldPickup : NetworkBehaviour
{
    [SerializeField] private GameObject visual;

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        var netObj = other.GetComponentInParent<NetworkObject>();
        if (netObj == null || !netObj.CompareTag("Player")) return;

        var shield = netObj.GetComponent<PlayerShield>();
        if (shield == null) return;

        // Un player non può prendere entrambi i pickup
        if (shield.HasShield.Value) return;

        shield.Activate();
        OnCollectedRpc();
    }

    [Rpc(SendTo.Everyone)]
    private void OnCollectedRpc()
    {
        if (visual != null) visual.SetActive(false);

        // Despawn dopo un frame per dare tempo all'RPC di arrivare
        if (IsServer)
            StartCoroutine(DespawnNextFrame());
    }

    private System.Collections.IEnumerator DespawnNextFrame()
    {
        yield return null;
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(true);
    }
}
