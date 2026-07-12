using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Arma raccoglibile a terra. NetworkObject: sparisce per tutti quando raccolta.
///
/// Setup scena:
///   1. Crea un GameObject con questo script + NetworkObject + Collider (trigger) nel layer "Pickup".
///   2. Assegna WeaponSlot (0 = Rifle, 1 = Shotgun).
///   3. Il player ci cammina sopra → OnTriggerEnter chiama RequestPickupServerRpc().
///   4. Il server verifica che il chiamante non abbia già quell'arma, poi:
///      - Invia GrantWeaponClientRpc solo al chiamante
///      - Despawna il pickup per tutti
/// </summary>
public class WeaponPickup : NetworkBehaviour
{
    [Header("Weapon")]
    [Tooltip("0 = Rifle (slot principale), 1 = Shotgun (slot secondario).")]
    [SerializeField] private int weaponSlot = 0;

    // ─── Auto-pickup on contact ───────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || !player.IsOwner) return;

        RequestPickupServerRpc();
    }

    // ─── API chiamata da OnTriggerEnter ───────────────────────────────────────

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestPickupServerRpc(RpcParams rpcParams = default)
    {
        ulong callerId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(callerId, out var client))
            return;

        PlayerController playerController = client.PlayerObject?.GetComponent<PlayerController>();
        if (playerController == null) return;

        // Rifiuta se il player ha già un'arma in questo slot
        if (playerController.HasWeaponSlot(weaponSlot)) return;

        GrantWeaponClientRpc(weaponSlot, new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { callerId } }
        });

        // Nasconde il pickup su tutti i client prima del Despawn:
        // Despawn(destroy:false) non chiama SetActive(false) sui client (oggetto in-scena)
        HidePickupClientRpc();
        NetworkObject.Despawn(destroy: false);

        Debug.Log($"[WeaponPickup] Slot {weaponSlot} raccolta da clientId={callerId}.");
    }

    // ─── Client RPC ───────────────────────────────────────────────────────────

    [ClientRpc]
    private void GrantWeaponClientRpc(int slot, ClientRpcParams clientRpcParams = default)
    {
        var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer == null) return;

        localPlayer.GetComponent<PlayerController>()?.AcquireWeapon(slot);
        localPlayer.GetComponent<WeaponVisualHandler>()?.AcquireWeapon(slot);
    }

    [ClientRpc]
    private void HidePickupClientRpc()
    {
        gameObject.SetActive(false);
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 1.5f);
    }
#endif
}
