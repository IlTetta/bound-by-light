using UnityEngine;

[CreateAssetMenu(menuName = "Game/Enemies/Enemy Ranged Config")]
public sealed class EnemyRangedConfig : ScriptableObject
{
    [Header("Perception")]
    public float aggroRadius = 10f;
    public float loseTargetRadiusMultiplier = 1.2f;
    public LayerMask playerMask;

    [Header("Movement")]
    public float moveSpeed = 3.2f;

    [Tooltip("Se distanza > maxRange: chase verso il target")]
    public float maxRange = 7.5f;

    [Tooltip("Se distanza < minRange: kite (retreat( lontano dal target")]
    public float minRange = 3.0f;

    [Tooltip("Range ideale: se dentro [minRange, maxRange] l'enemy tende a fermarsi e sparare")]
    public float preferredRange = 5.0f;

    [Tooltip("Velocità durante il retreat (kite). Tipicamente leggermente inferiore a moveSpeed")]
    public float retreatSpeedMultiplier = 0.9f;

    [Tooltip("Deadzone per evitare jitter: entro questa tolleranza non aggiusta distanza.")]
    public float rangeTolerance = 0.25f;

    [Header("Attack Timeline")]
    public float windup = 0.20f;
    public float active = 0.05f;
    public float recover = 0.90f;

    [Header("Targeting")]
    public float retargetInterval = 0.20f;
    public float switchTargetDistanceBias = 0.35f;
}
