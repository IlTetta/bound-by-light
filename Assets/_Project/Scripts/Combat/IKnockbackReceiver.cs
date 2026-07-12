using UnityEngine;

/// <summary>
/// Implementata dai motor (player e nemici) che sanno ricevere un impulso di knockback
/// sul piano XZ. Permette a <see cref="HealthNetwork"/> di instradare il knockback senza
/// conoscere il tipo concreto del motor (niente più probing di 4 GetComponent per colpo).
/// </summary>
public interface IKnockbackReceiver
{
    /// <summary>Applica un impulso di knockback nel piano XZ (x = world X, y = world Z).</summary>
    void ApplyKnockback(Vector2 impulseXZ);
}
