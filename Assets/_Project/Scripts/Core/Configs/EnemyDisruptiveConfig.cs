using UnityEngine;

/// <summary>
/// Stat del nemico disruptive. I campi comuni (vita, aggro, moveSpeed, loot, knockback)
/// stanno in EnemyBaseConfig.
/// </summary>
[CreateAssetMenu(menuName = "Game/Enemies/Enemy Disruptive Config")]
public sealed class EnemyDisruptiveConfig : EnemyBaseConfig
{
    [Header("Interpose")]
    [Tooltip("Distanza minima dal centro del tether a cui si ferma in Interpose.")]
    public float interposeStopDistance = 0.6f;

    [Tooltip("Quanto tempo rimane in Interpose prima di valutare se fare il Dash.")]
    public float interposeDuration = 1.8f;

    [Tooltip("Distanza dal punto ideale di interpose sotto la quale si considera 'in posizione'.")]
    public float interposeReachedThreshold = 0.4f;

    [Header("Dash")]
    [Tooltip("Velocità della carica.")]
    public float dashSpeed = 14f;

    [Tooltip("Durata massima del Dash (safety cap nel caso non colpisca nulla).")]
    public float dashMaxDuration = 0.35f;

    [Tooltip("Windup prima della carica (telegraph per il player).")]
    public float dashWindup = 0.45f;

    [Tooltip("Recover dopo la carica.")]
    public float dashRecover = 0.7f;

    [Tooltip("Distanza percorsa oltre il tether durante il Dash (overshoot).")]
    public float dashOvershoot = 1.5f;

    [Header("Dash - Danno e Knockback")]
    public int dashDamage = 12;

    [Tooltip("Forza del knockback del Dash. Alta: attiva il pull del tether.")]
    public float dashKnockbackForce = 18f;

    [Tooltip("Direzione del knockback: 0 = allontana il nemico, 1 = nella direzione del Dash.")]
    [Range(0f, 1f)]
    public float dashKnockbackBias = 0.65f;

    public LayerMask damageTargetMask;
}
