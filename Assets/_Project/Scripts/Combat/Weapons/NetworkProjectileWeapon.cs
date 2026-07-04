using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Arma a proiettili sincronizzata in rete.
/// Assegna un WeaponData per configurare le stat da ScriptableObject.
/// Se WeaponData è null, vengono usati i valori impostati direttamente nell'Inspector.
/// </summary>
public class NetworkProjectileWeapon : NetworkBehaviour, IRangedWeapon
{
    [Header("Data (opzionale — sovrascrive i valori manuali se assegnato)")]
    [SerializeField] private WeaponData weaponData;

    [Header("Weapon Stats")]
    [SerializeField] private int damage = 10;
    [SerializeField] private float fireCooldown = 0.3f;
    private float _cooldownTimer = 0f;

    [Header("Reload")]
    [Tooltip("Durata del reload in secondi. Deve corrispondere alla lunghezza del clip Reload nell'Animator. Sovrascritto da WeaponData.reloadTime se assegnato.")]
    [SerializeField] private float reloadTime = 1.5f;

    public bool CanFire => _cooldownTimer <= 0f && !_isReloading;
    private bool _isReloading = false;

    /// <summary>Espone il cooldown corrente (usato da PlayerController per il throttle locale).</summary>
    public float FireCooldown => fireCooldown;

    [Header("Spawn Settings")]
    public List<Transform> SpawnTransforms;
    public Vector3 ProjectileSpawnOffset;
    public bool RandomSpawnTransform = false;
    [Tooltip("Se != 0, sovrascrive la Y del punto di spawn con Root.Y + questo valore. " +
             "Usare valori positivi per alzare, negativi per abbassare rispetto al pivot del prefab. " +
             "Es: boss root a Y=-1.2, player center a Y≈0 → spawnHeightOffset = 1.2")]
    [SerializeField] private float spawnHeightOffset = 0f;
    private int _nextSpawnIndex = 0;

    [Header("Burst & Spread")]
    public int ProjectilesPerShot = 1;
    public float Spread = 5f;
    public bool RandomSpread = true;

    [Header("Visual Feedbacks")]
    public GameObject MuzzleFlashPrefab;
    public GameObject CasingPrefab;

    [SerializeField] private GameObject projectilePrefab;

    // True se chi possiede questa arma è un nemico (IEnemyEntity).
    // Calcolato in Awake, usato per settare il flag sui proiettili spawnati.
    private bool _ownerIsEnemy;

    private void Awake()
    {
        // Applica la configurazione da WeaponData, se presente.
        // Awake garantisce che i valori siano pronti prima di qualsiasi logica di gioco.
        if (weaponData != null)
        {
            damage                = weaponData.damage;
            fireCooldown          = weaponData.fireCooldown;
            reloadTime            = weaponData.reloadTime;
            ProjectilesPerShot    = weaponData.projectilesPerShot;
            Spread                = weaponData.spread;
            RandomSpread          = weaponData.randomSpread;
            ProjectileSpawnOffset = weaponData.projectileSpawnOffset;

            if (weaponData.projectilePrefab != null)
                projectilePrefab = weaponData.projectilePrefab;
        }

        // Rileva se chi possiede l'arma è un nemico, per filtrare i friendly fire
        _ownerIsEnemy = GetComponentInParent<IEnemyEntity>() != null;
    }

    private void Update()
    {
        // Il cooldown è usato solo dal server in CanFire: inutile decrementarlo sui client.
        if (!IsServer) return;
        if (_cooldownTimer > 0f)
            _cooldownTimer -= Time.deltaTime;
    }

    public bool TryFire(Vector2 origin, Vector2 aimDirection)
    {
        if (!CanFire) return false;
        _cooldownTimer = fireCooldown;
        // aimDirection da MouseAimProvider è Vector2(worldX, worldZ)
        SpawnProjectiles(new Vector3(aimDirection.x, aimDirection.y, 0f));
        return true;
    }

    /// <summary>
    /// Spara in direzione 3D completa, preservando la componente Y.
    /// Usato dal boss per proiettili inclinati verso il basso/player.
    /// Il prefab deve avere Projectile3D con FlatTrajectory = false.
    /// </summary>
    public bool TryFire3D(Vector3 direction3D)
    {
        if (!CanFire) return false;
        _cooldownTimer = fireCooldown;
        SpawnProjectiles3D(direction3D);
        return true;
    }

    public void SpawnProjectiles3D(Vector3 direction3D)
    {
        if (!IsServer) return;
        for (int i = 0; i < ProjectilesPerShot; i++)
            SpawnProjectile3D(direction3D, i);
    }

    private void SpawnProjectile3D(Vector3 direction3D, int index)
    {
        if (projectilePrefab == null)
        {
            Debug.LogError($"[NetworkProjectileWeapon] projectilePrefab non assegnato su {gameObject.name}!");
            return;
        }

        // Punto di spawn
        Transform spawnPoint  = GetSpawnTransform();
        Vector3 spawnPosition = spawnPoint.position + (spawnPoint.rotation * ProjectileSpawnOffset);

        // Spread orizzontale (rotazione attorno a Y) applicato alla direzione 3D
        float spreadOffset = ComputeSpreadOffset3D(index);
        Vector3 finalDir   = Quaternion.Euler(0f, spreadOffset, 0f) * direction3D;

        GameObject projectileGo = Instantiate(
            projectilePrefab, spawnPosition, Quaternion.LookRotation(finalDir));

        NetworkObject netObj = projectileGo.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError($"[NetworkProjectileWeapon] '{projectilePrefab.name}' non ha NetworkObject!");
            Destroy(projectileGo);
            return;
        }
        if (!netObj.IsSpawned) netObj.Spawn();

        Projectile3D proj = projectileGo.GetComponent<Projectile3D>();
        if (proj == null)
        {
            Debug.LogError($"[NetworkProjectileWeapon] TryFire3D richiede Projectile3D su '{projectilePrefab.name}'!");
            netObj.Despawn(true);
            return;
        }

        proj.FlatTrajectory = false;   // traiettoria inclinata — non congelare la Y
        proj.SetOwner(NetworkObject.NetworkObjectId);
        proj.SetFiredByEnemy(_ownerIsEnemy);
        // Initialize replica direzione e quota ai client via NetworkVariable
        proj.Initialize(finalDir);
        proj.SetDamage(damage);        // dopo Initialize: sovrascrive il Damage di default
        TriggerSpawnFeedbacks(spawnPoint);
    }

    public void SpawnProjectiles(Vector3 aimDirection)
    {
        if (!IsServer) return;

        for (int i = 0; i < ProjectilesPerShot; i++)
            SpawnProjectile(aimDirection, i);
    }

    private void SpawnProjectile(Vector3 aimDirection, int index)
    {
        if (projectilePrefab == null)
        {
            Debug.LogError($"[NetworkProjectileWeapon] projectilePrefab non assegnato su {gameObject.name}!");
            return;
        }

        // 1. Punto di nascita
        Transform spawnPoint  = GetSpawnTransform();
        Vector3 spawnPosition = spawnPoint.position + (spawnPoint.rotation * ProjectileSpawnOffset);
        if (spawnHeightOffset != 0f)
            spawnPosition.y = transform.position.y + spawnHeightOffset;

        // 2. Spread
        float baseAngle  = Mathf.Atan2(aimDirection.y, aimDirection.x) * Mathf.Rad2Deg;
        float finalAngle = CalculateSpread(baseAngle, index);
        Quaternion rotation = Quaternion.Euler(0, 0, finalAngle);

        // 3. Spawn in rete
        GameObject projectileGo = Instantiate(projectilePrefab, spawnPosition, rotation);

        NetworkObject netObj = projectileGo.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError($"[NetworkProjectileWeapon] Il prefab '{projectilePrefab.name}' non ha un componente NetworkObject! Aggiungilo al prefab.");
            Destroy(projectileGo);
            return;
        }
        if (!netObj.IsSpawned) netObj.Spawn();

        // 4. Configurazione proiettile — prova prima il componente 3D
        Projectile3D projectileScript3D = projectileGo.GetComponent<Projectile3D>();
        if (projectileScript3D != null)
        {
            // aimDirection arriva da TryFire come Vector3(worldX, worldZ, 0)
            // Convertiamo in XZ 3D e applichiamo spread come rotazione Y
            Vector3 dir3 = new Vector3(aimDirection.x, 0f, aimDirection.y);
            float spreadOffset = ComputeSpreadOffset3D(index);
            Vector3 finalDir = Quaternion.Euler(0f, spreadOffset, 0f) * dir3;
            projectileScript3D.SetOwner(NetworkObject.NetworkObjectId);
            projectileScript3D.SetFiredByEnemy(_ownerIsEnemy);
            // Initialize replica direzione e quota ai client via NetworkVariable
            projectileScript3D.Initialize(finalDir);
            projectileScript3D.SetDamage(damage); // dopo Initialize: sovrascrive il Damage di default
            TriggerSpawnFeedbacks(spawnPoint);
            return;
        }

        // Fallback 2D
        Projectile projectileScript = projectileGo.GetComponent<Projectile>();
        if (projectileScript == null)
        {
            Debug.LogError($"[NetworkProjectileWeapon] '{projectilePrefab.name}' non ha Projectile né Projectile3D!");
            netObj.Despawn(true);
            return;
        }

        // NetworkObjectId (univoco per oggetto) evita il falso-positivo host vs nemici (entrambi OwnerClientId=0)
        projectileScript.SetOwner(NetworkObject.NetworkObjectId);
        projectileScript.Initialize(rotation * Vector2.right);
        projectileScript.SetDamage(damage);
        projectileScript.SetFiredByEnemy(_ownerIsEnemy);

        // 5. Feedbacks
        TriggerSpawnFeedbacks(spawnPoint);
    }

    public void SetDamageOverride(int dmg) => damage = dmg;

    private float CalculateSpread(float baseAngle, int index)
    {
        if (RandomSpread)
            return baseAngle + Random.Range(-Spread / 2f, Spread / 2f);

        if (ProjectilesPerShot <= 1) return baseAngle;
        float step = Spread / (ProjectilesPerShot - 1);
        return (baseAngle - Spread / 2f) + step * index;
    }

    // Spread relativo (offset in gradi attorno alla direzione base) per i proiettili 3D
    private float ComputeSpreadOffset3D(int index)
    {
        if (RandomSpread)
            return Random.Range(-Spread / 2f, Spread / 2f);
        if (ProjectilesPerShot <= 1) return 0f;
        float step = Spread / (ProjectilesPerShot - 1);
        return (-Spread / 2f) + step * index;
    }

    private Transform GetSpawnTransform()
    {
        if (SpawnTransforms == null || SpawnTransforms.Count == 0) return transform;

        if (RandomSpawnTransform)
            return SpawnTransforms[Random.Range(0, SpawnTransforms.Count)];

        Transform t = SpawnTransforms[_nextSpawnIndex];
        _nextSpawnIndex = (_nextSpawnIndex + 1) % SpawnTransforms.Count;
        return t;
    }

    private void TriggerSpawnFeedbacks(Transform spawnPoint)
    {
        if (MuzzleFlashPrefab != null)
        {
            // Il muzzle flash è un effetto puramente visivo: va istanziato su tutti i client,
            // non solo sul server. Lo spariamo via ClientRpc con posizione e rotazione.
            SpawnMuzzleFlashClientRpc(spawnPoint.position, spawnPoint.rotation);
        }
    }

    [ClientRpc]
    private void SpawnMuzzleFlashClientRpc(Vector3 position, Quaternion rotation)
    {
        if (MuzzleFlashPrefab != null)
            Instantiate(MuzzleFlashPrefab, position, rotation);
    }

    // ─── Reload server-side ───────────────────────────────────────────────────

    /// <summary>
    /// Avvia il cooldown di rianimazione server-side. Chiamato da PlayerController
    /// in contemporanea a WeaponAmmo.RequestReloadServerRpc().
    /// Blocca CanFire per la durata del reloadTime.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void BeginReloadServerRpc()
    {
        if (!IsServer || _isReloading) return;
        StartCoroutine(ReloadCoroutine());
    }

    private IEnumerator ReloadCoroutine()
    {
        _isReloading = true;
        yield return new WaitForSeconds(reloadTime);
        _isReloading = false;
    }
}
