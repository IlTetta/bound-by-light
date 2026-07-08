using UnityEngine;

/// <summary>
/// Stat del nemico melee (chaser). I campi comuni stanno in EnemyBaseConfig.
/// </summary>
[CreateAssetMenu(menuName = "Game/Enemies/Enemy Chaser Config")]
public sealed class EnemyChaserConfig : EnemyBaseConfig
{
    [Header("Chase")]
    public float stopDistance   = 0.9f;
    public float repathInterval = 0.05f;

    [Header("Melee")]
    public float attackRange = 1.2f;
    public float windup      = 0.15f;
    public float active      = 0.10f;
    public float cooldown    = 0.80f;

    [Header("Damage")]
    public int       damage         = 10;
    public float     knockbackForce = 6f;
    public LayerMask damageTargetMask;
}
