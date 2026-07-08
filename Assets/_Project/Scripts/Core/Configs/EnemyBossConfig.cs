using UnityEngine;

/// <summary>
/// Stat del miniboss (Bishop). I campi comuni — vita, aggroRadius, moveSpeed,
/// playerMask, knockback, separazione, reward — stanno in EnemyBaseConfig.
///
/// Il brain copia questi valori nei propri campi in Awake (BishopMinibossBrain.ApplyConfig),
/// quindi il corpo del brain resta invariato.
/// </summary>
[CreateAssetMenu(menuName = "Game/Enemies/Enemy Boss Config")]
public sealed class EnemyBossConfig : EnemyBaseConfig
{
    [Header("Attack Ranges")]
    [Tooltip("Distanza entro cui il boss si ferma e sceglie uno dei 4 attacchi a caso.")]
    public float attackRange = 4f;

    [Tooltip("Probabilità (0-1) che l'attacco scelto sia melee anziché ranged.")]
    [Range(0f, 1f)]
    public float meleeProbability = 0.5f;

    [Header("Melee – Heavy Smash")]
    public int   smashDamage    = 20;
    public float smashKnockback = 8f;
    public float smashWindup    = 0.7f;
    public float smashActive    = 0.25f;
    public float smashRecover   = 0.5f;

    [Header("Melee – Sweep")]
    public int   sweepDamage    = 12;
    public float sweepKnockback = 5f;
    public float sweepWindup    = 0.4f;
    public float sweepActive    = 0.3f;
    public float sweepRecover   = 0.4f;

    [Header("Ranged – Holy Bolt")]
    public int   boltDamage   = 15;
    public float boltCooldown = 1.5f;

    [Header("Ranged – Triple Shot")]
    public int   tripleDamage   = 10;
    public float tripleCooldown = 2.2f;
    public float tripleSpread   = 30f;

    [Header("Ranged – Timing")]
    [Tooltip("Secondi dall'inizio dell'animazione ranged allo sparo del proiettile.")]
    public float rangedFireDelay = 0.6f;

    [Header("Cooldown between attacks")]
    public float attackCooldownMin = 1.0f;
    public float attackCooldownMax = 2.0f;
}
