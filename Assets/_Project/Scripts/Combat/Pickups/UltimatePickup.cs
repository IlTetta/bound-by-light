using Unity.Netcode;
using UnityEngine;
using MyGame.Core;

/// <summary>
/// Pickup nella stanza sotterranea che sblocca la Bond Ability (ultimate).
///
/// Setup prefab:
///   1. Crea un GameObject con Collider trigger + NetworkObject + questo componente.
///   2. Metti il GameObject sul layer "Pickup" (o uno dedicato "Ultimate").
///   3. La raccolta avviene automaticamente quando un player entra nel trigger.
///   4. Una volta raccolto, si sblocca <see cref="GameManager.BondAbilityUnlocked"/>
///      per entrambi i player.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class UltimatePickup : NetworkBehaviour
{
    [Header("Visuals")]
    [Tooltip("Renderer o GameObject decorativo da nascondere alla raccolta.")]
    [SerializeField] private GameObject visual;

    private bool _collected;

    // ─── Raccolta via trigger ─────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        // Solo il server decide la raccolta
        if (!IsServer || _collected) return;

        // Accetta solo i player (usa il layer "Player" configurato nel progetto)
        if (!other.CompareTag("Player")) return;

        _collected = true;
        CollectServerSide();
    }

    // ─── Logica server ────────────────────────────────────────────────────────

    private void CollectServerSide()
    {
        GameManager.Instance?.UnlockBondAbility();

        // Feedback su tutti i client prima di despawnare
        OnCollectedClientRpc();

        // Ritardo minimo per garantire che l'RPC arrivi prima del despawn
        Invoke(nameof(DespawnSelf), 0.1f);
    }

    private void DespawnSelf()
    {
        if (IsSpawned)
            NetworkObject.Despawn();
    }

    // ─── RPC ─────────────────────────────────────────────────────────────────

    [Rpc(SendTo.Everyone)]
    private void OnCollectedClientRpc()
    {
        if (visual != null)
            visual.SetActive(false);

        Debug.Log("[UltimatePickup] Bond Ability sbloccata per entrambi i player!");
    }
}
