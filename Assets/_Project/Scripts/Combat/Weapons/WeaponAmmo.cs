using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gestisce le munizioni di UNA singola arma di UN singolo player (per-player, per-weapon).
/// Metti due istanze di questo componente sul prefab del player: una per Slot 0, una per Slot 1.
/// Assegna lo stesso WeaponData usato dal NetworkProjectileWeapon dello stesso slot.
/// </summary>
public class WeaponAmmo : NetworkBehaviour
{
    [Header("Data (opzionale — sovrascrive i valori manuali se assegnato)")]
    [SerializeField] private WeaponData weaponData;

    [Header("Ammo Settings (usati solo se WeaponData è null)")]
    public string AmmoID = "PistolAmmo";
    public int MagazineSize = 12;
    [SerializeField] private int totalAmmo = 60;

    // NetworkVariable: leggibili da tutti, scrivibili solo dal Server
    public NetworkVariable<int> CurrentAmmoLoaded = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> CurrentAmmoAvailable = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        // Sovrascrive con i dati del WeaponData se disponibile
        if (weaponData != null)
        {
            AmmoID       = weaponData.weaponName;
            MagazineSize = weaponData.magazineSize;
            totalAmmo    = weaponData.totalAmmo;
        }

        // Caricatore pieno all'avvio, il resto in riserva
        CurrentAmmoLoaded.Value    = MagazineSize;
        CurrentAmmoAvailable.Value = Mathf.Max(0, totalAmmo - MagazineSize);
    }

    /// <summary>
    /// Consuma munizioni dal caricatore. Chiamato direttamente server-side.
    /// </summary>
    public void ConsumeAmmo(int amount = 1)
    {
        if (!IsServer) return;
        CurrentAmmoLoaded.Value = Mathf.Max(0, CurrentAmmoLoaded.Value - amount);
    }

    /// <summary>Ripristina caricatore e riserva a valori specifici (usato dopo reconnessione).</summary>
    public void RestoreAmmo(int loaded, int available)
    {
        if (!IsServer) return;
        CurrentAmmoLoaded.Value    = Mathf.Clamp(loaded,    0, MagazineSize);
        CurrentAmmoAvailable.Value = Mathf.Max(0, available);
    }

    /// <summary>
    /// Aggiunge munizioni alla riserva (CurrentAmmoAvailable).
    /// Chiamato da AmmoPickup quando il player raccoglie un drop o una cassa.
    /// </summary>
    public void AddReserveAmmo(int amount)
    {
        if (!IsServer || amount <= 0) return;
        CurrentAmmoAvailable.Value += amount;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestConsumeAmmoServerRpc(int amount)
    {
        ConsumeAmmo(amount);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestReloadServerRpc()
    {
        int amountNeeded = MagazineSize - CurrentAmmoLoaded.Value;
        int toTransfer   = Mathf.Min(amountNeeded, CurrentAmmoAvailable.Value);

        if (toTransfer > 0)
        {
            CurrentAmmoLoaded.Value    += toTransfer;
            CurrentAmmoAvailable.Value -= toTransfer;
        }
    }
}
