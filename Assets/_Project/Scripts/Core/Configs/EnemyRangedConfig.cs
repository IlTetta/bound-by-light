using UnityEngine;

/// <summary>
/// Stat del nemico ranged. I campi comuni (vita, aggro, moveSpeed, loot, knockback)
/// stanno in EnemyBaseConfig.
/// </summary>
[CreateAssetMenu(menuName = "Game/Enemies/Enemy Ranged Config")]
public sealed class EnemyRangedConfig : EnemyBaseConfig
{
    [Header("Distanze")]
    [Tooltip("Se distanza > maxRange: chase verso il target.")]
    public float maxRange = 7.5f;

    [Tooltip("Se distanza < minRange: kite (retreat) lontano dal target.")]
    public float minRange = 3.0f;

    [Tooltip("Range ideale: se dentro [minRange, maxRange] tende a fermarsi e sparare.")]
    public float preferredRange = 5.0f;

    [Tooltip("Velocità durante il retreat (kite). Tipicamente inferiore a moveSpeed.")]
    public float retreatSpeedMultiplier = 0.9f;

    [Tooltip("Deadzone per evitare jitter: entro questa tolleranza non aggiusta distanza.")]
    public float rangeTolerance = 0.25f;

    [Header("Attack Timeline")]
    public float windup  = 0.20f;
    public float active  = 0.05f;
    public float recover = 0.90f;
}
