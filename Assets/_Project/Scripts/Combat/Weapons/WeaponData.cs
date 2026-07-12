using UnityEngine;

/// <summary>
/// ScriptableObject di configurazione per un'arma ranged.
/// Crea istanze tramite il menu Assets > Create > Game/Weapons/WeaponData.
/// Assegna la stessa istanza sia a NetworkProjectileWeapon che a WeaponAmmo
/// sullo stesso slot del player prefab, così i valori sono definiti in un solo posto.
/// </summary>
[CreateAssetMenu(menuName = "Game/Weapons/WeaponData")]
public class WeaponData : ScriptableObject
{
    [Header("Identity")]
    public string weaponName = "Weapon";
    [Tooltip("Icona mostrata nell'inventario quando l'arma viene raccolta.")]
    public Sprite icon;

    [Header("Projectile")]
    [Tooltip("Prefab del proiettile (deve avere NetworkObject + Projectile3D).")]
    public GameObject projectilePrefab;
    public int projectilesPerShot = 1;
    [Range(0f, 45f)]
    public float spread = 5f;
    public bool randomSpread = true;
    public Vector3 projectileSpawnOffset = Vector3.zero;

    [Header("Stats")]
    public int damage = 10;
    [Tooltip("Secondi di cooldown tra uno sparo e l'altro.")]
    public float fireCooldown = 0.3f;

    [Header("Ammo")]
    public int magazineSize = 12;
    [Tooltip("Totale munizioni disponibili (incluse quelle nel caricatore iniziale).")]
    public int totalAmmo = 60;
    [Tooltip("Durata del reload in secondi. Deve corrispondere alla lunghezza del clip Reload nell'Animator.")]
    public float reloadTime = 1.5f;
}
