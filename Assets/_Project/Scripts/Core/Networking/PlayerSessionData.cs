using MyGame.Core;
using UnityEngine;

/// <summary>
/// Snapshot dello stato di un player al momento della disconnessione.
/// Usato da PlayerSpawnServer per ripristinare posizione, salute, armi e munizioni
/// quando il joiner si riconnette.
/// </summary>
[System.Serializable]
public struct PlayerSessionData
{
    public int     PrefabIndex;
    public Vector3 Position;
    public int     Health;
    public bool    Weapon0Acquired;
    public bool    Weapon1Acquired;
    public int     ActiveWeaponIndex;
    public int     AmmoLoaded0;
    public int     AmmoAvailable0;
    public int     AmmoLoaded1;
    public int     AmmoAvailable1;
    public bool    HasShield;
    public float   ShieldHp;
    public bool    BondAbilityUnlocked;
    public int     MedikitCount;

    /// <summary>Cattura lo stato corrente di un player NetworkObject lato server.</summary>
    public static PlayerSessionData Capture(Unity.Netcode.NetworkObject playerObj, int prefabIndex)
    {
        var data = new PlayerSessionData();
        data.PrefabIndex = prefabIndex;
        data.Position    = playerObj.transform.position;

        var health = playerObj.GetComponent<HealthNetwork>();
        data.Health = health != null ? health.CurrentHealth.Value : 100;

        var ctrl = playerObj.GetComponent<PlayerController>();
        if (ctrl != null)
        {
            data.Weapon0Acquired   = ctrl.WeaponsAcquired[0];
            data.Weapon1Acquired   = ctrl.WeaponsAcquired[1];
            data.ActiveWeaponIndex = ctrl.ActiveWeaponIndex;

            var ammo0 = ctrl.GetAmmo(0);
            if (ammo0 != null)
            {
                data.AmmoLoaded0    = ammo0.CurrentAmmoLoaded.Value;
                data.AmmoAvailable0 = ammo0.CurrentAmmoAvailable.Value;
            }

            var ammo1 = ctrl.GetAmmo(1);
            if (ammo1 != null)
            {
                data.AmmoLoaded1    = ammo1.CurrentAmmoLoaded.Value;
                data.AmmoAvailable1 = ammo1.CurrentAmmoAvailable.Value;
            }
        }

        var shield = playerObj.GetComponent<PlayerShield>();
        if (shield != null)
        {
            data.HasShield = shield.HasShield.Value;
            data.ShieldHp  = shield.ShieldHp.Value;
        }

        data.BondAbilityUnlocked = GameManager.Instance != null &&
                                   GameManager.Instance.BondAbilityUnlocked.Value;

        var medikit = playerObj.GetComponent<PlayerMedikit>();
        if (medikit != null)
            data.MedikitCount = medikit.MedikitCount.Value;

        return data;
    }
}
