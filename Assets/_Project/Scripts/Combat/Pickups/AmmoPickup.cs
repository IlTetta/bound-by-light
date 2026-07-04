using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Pickup di munizioni. Quando un player ci cammina sopra, aggiunge
/// <see cref="ammoPerWeapon"/> munizioni alla riserva di OGNI slot arma del player.
///
/// Usato sia come drop di un nemico (spawnato da EnemyAmmoDropper)
/// sia come ricompensa al completamento di una stanza (spawnato da RoomWaveController).
///
/// Setup prefab:
///   - Rigidbody2D: Kinematic, Trigger
///   - Collider2D: IsTrigger = true
///   - NetworkObject
///   - Questo componente
/// </summary>
public class AmmoPickup : NetworkBehaviour
{
    [Tooltip("Munizioni aggiunte alla riserva di OGNI slot arma del player che raccoglie.")]
    [SerializeField] private int ammoPerWeapon = 6;

    /// <summary>Permette a EnemyAmmoDropper di configurare la quantità dopo lo spawn.</summary>
    public void SetAmount(int amount) => ammoPerWeapon = Mathf.Max(1, amount);

    // Buffer riutilizzato per evitare allocazione array su ogni trigger
    private readonly List<WeaponAmmo> _ammoBuffer = new List<WeaponAmmo>(4);

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        // Cerca i WeaponAmmo risalendo la gerarchia: se non ne trova, non è un player armato
        other.GetComponentsInParent(false, _ammoBuffer);
        if (_ammoBuffer.Count == 0) return;

        foreach (var ammo in _ammoBuffer)
            ammo.AddReserveAmmo(ammoPerWeapon);

        Debug.Log($"[AmmoPickup] Raccolto: +{ammoPerWeapon} munizioni per arma.");
        NetworkObject.Despawn(true);
    }
}
