using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Reazione locale del player al colpo subìto: camera shake per il solo owner.
/// Si sottoscrive a <see cref="HealthNetwork.OnHitClient"/> (fired su tutti i client);
/// lo shake parte solo sul client che possiede questo player.
///
/// Sostituisce il flag shakeCameraOnHit che prima viveva dentro HealthNetwork:
/// era una reazione specifica del player e non apparteneva al core.
/// La sola presenza di questo componente sul prefab abilita lo shake.
/// </summary>
[RequireComponent(typeof(HealthNetwork))]
public sealed class PlayerHitReaction : NetworkBehaviour
{
    private HealthNetwork _health;

    public override void OnNetworkSpawn()
    {
        _health = GetComponent<HealthNetwork>();
        if (_health != null) _health.OnHitClient += HandleHit;
    }

    public override void OnNetworkDespawn()
    {
        if (_health != null) _health.OnHitClient -= HandleHit;
    }

    private void HandleHit()
    {
        if (!IsOwner) return;
        CoopCameraController.Instance?.Shake();
    }
}
