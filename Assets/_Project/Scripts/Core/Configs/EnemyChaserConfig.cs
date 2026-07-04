using UnityEngine;

[CreateAssetMenu(menuName = "Game/Enemies/Enemy Chaser Config")]
public sealed class EnemyChaserConfig : ScriptableObject
{
    [Header("Perception")]
    public float aggroRadius = 8f;
    public float loseTargetRadiusMultiplier = 1.2f;
    public LayerMask playerMask;

    [Header("Chase")]
    public float moveSpeed = 3.5f;
    public float stopDistance = 0.9f;
    public float repathInterval = 0.05f;

    [Header("Melee")]
    public float attackRange = 1.2f;
    public float windup = 0.15f;
    public float active = 0.10f;
    public float cooldown = 0.80f;

    [Header("Damage")]
    public int damage = 10;
    public float knockbackForce = 6f;
    public LayerMask damageTargetMask;

    [Header("Targeting")]
    public float retargetInterval = 0.20f;
    public float switchTargetDistanceBias = 0.35f; // P2 deve essere almeno 0.35 più vicino (in unità mondo) per sostituire P1
}
