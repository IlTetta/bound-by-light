using UnityEngine;

/// <summary>
/// Stat di design comuni a tutti i nemici. Classe base dei config specifici
/// (EnemyChaserConfig, EnemyRangedConfig, EnemyDisruptiveConfig).
///
/// REGOLA: qui stanno solo i NUMERI che si bilanciano. I riferimenti a componenti,
/// prefab, animator ed emitter restano sul prefab — sono "com'è fatto" il nemico,
/// non "quanto è forte".
///
/// I brain chiamano ApplyTo(gameObject) in Awake: i componenti ricevono i valori
/// prima di qualsiasi OnNetworkSpawn, quindi HealthNetwork inizializza CurrentHealth
/// col maxHealth del config e non con quello del prefab.
/// </summary>
public abstract class EnemyBaseConfig : ScriptableObject
{
    [Header("Sopravvivenza")]
    public int   maxHealth            = 100;
    public float invincibilitySeconds = 0.25f;

    [Header("Percezione")]
    public float     aggroRadius                = 8f;
    public float     loseTargetRadiusMultiplier = 1.2f;
    public LayerMask playerMask;

    [Header("Movimento")]
    public float moveSpeed = 3.5f;

    [Header("Targeting")]
    public float retargetInterval = 0.20f;

    [Tooltip("Quanto più vicino deve essere l'altro player (in world units) per " +
             "rubare il target a quello corrente.")]
    public float switchTargetDistanceBias = 0.35f;

    [Header("Knockback")]
    public float maxKnockbackSpeed = 4f;
    public float knockbackDecay    = 10f;

    [Tooltip("Decadimento dell'impulso su KnockReceiver3D.")]
    public float knockReceiverDecay = 20f;

    [Header("Separazione (anti-overlap)")]
    public float separationRadius = 1.0f;
    public float separationForce  = 4f;

    [Header("Loot")]
    public int   currencyReward = 5;
    public float energyReward   = 10f;

    [Range(0f, 1f)]
    public float ammoDropChance = 0.35f;
    public int   ammoAmount     = 6;

    /// <summary>
    /// Scrive le stat nei componenti del nemico. Da chiamare in Awake, mai dopo
    /// lo spawn: HealthNetwork.OnNetworkSpawn legge maxHealth per inizializzare
    /// CurrentHealth.
    /// </summary>
    public void ApplyTo(GameObject enemy)
    {
        if (enemy == null) return;

        enemy.GetComponent<HealthNetwork>()
            ?.ConfigureStats(maxHealth, invincibilitySeconds);

        enemy.GetComponent<EnemyMotor3D>()
            ?.ConfigureStats(maxKnockbackSpeed, knockbackDecay, separationRadius, separationForce);

        enemy.GetComponent<KnockReceiver3D>()
            ?.ConfigureStats(knockReceiverDecay);

        enemy.GetComponent<EnemyAmmoDropper>()
            ?.ConfigureStats(ammoDropChance, ammoAmount);
    }
}
