using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public sealed class EnemyMotor2D : MonoBehaviour
{
    [Header("Knockback")]
    [Tooltip("Velocità massima di knockback accumulabile. Impedisce l'abuso da fuoco rapido.")]
    [SerializeField] private float maxKnockbackSpeed = 4f;
    [Tooltip("Decadimento del knockback per secondo. Più alto = recupero più rapido.")]
    [SerializeField] private float knockbackDecay = 10f;

    [Header("Separazione (anti-overlap)")]
    [Tooltip("Raggio entro cui vengono rilevati i vicini. " +
             "Impostalo circa uguale al diametro del collider del nemico.")]
    [SerializeField] private float separationRadius = 1.0f;
    [Tooltip("Forza di repulsione quando due nemici si sovrappongono.")]
    [SerializeField] private float separationForce = 4f;
    [Tooltip("Layer dei nemici, usato per trovare i vicini.")]
    [SerializeField] private LayerMask enemyLayer;

    private Rigidbody2D _rb;

    // Velocità desiderata dall'AI (impostata da MoveTowards/Stop)
    private Vector2 _aiVelocity;

    // Impulso di knockback (somma capped, decade nel tempo)
    private Vector2 _knockbackVelocity;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // Decade il knockback indipendentemente dal movimento AI
        _knockbackVelocity = Vector2.MoveTowards(
            _knockbackVelocity, Vector2.zero, knockbackDecay * dt);

        // Applica AI + knockback + separazione in un'unica MovePosition
        Vector2 total = _aiVelocity + _knockbackVelocity + ComputeSeparation();
        if (total.sqrMagnitude > 0.0001f)
            _rb.MovePosition(_rb.position + total * dt);
    }

    /// <summary>
    /// Calcola una spinta di separazione dai vicini troppo vicini.
    /// Due kinematic body non si respingono fisicamente: questa funzione
    /// simula la repulsione applicando una velocità proporzionale alla sovrapposizione.
    /// </summary>
    private Vector2 ComputeSeparation()
    {
        if (separationRadius <= 0f) return Vector2.zero;

        Collider2D[] neighbors = Physics2D.OverlapCircleAll(_rb.position, separationRadius, enemyLayer);
        Vector2 push = Vector2.zero;

        foreach (var col in neighbors)
        {
            // Ignora se stesso
            if (col.attachedRigidbody == _rb) continue;

            Vector2 toMe = _rb.position - (Vector2)col.transform.position;
            float dist = toMe.magnitude;

            // Caso degenere: stessa posizione esatta → spingi in alto per uscire
            if (dist < 0.001f) { push += Vector2.up; continue; }

            float overlap = separationRadius - dist;
            if (overlap > 0f)
                push += toMe.normalized * overlap;
        }

        return push.sqrMagnitude > 0.0001f
            ? push.normalized * separationForce
            : Vector2.zero;
    }

    public Vector2 Position => _rb.position;

    /// <summary>
    /// Muove il nemico verso targetPos alla velocità data.
    /// Il parametro dt è mantenuto per compatibilità con i brain ma non è usato
    /// direttamente: la traslazione avviene nel FixedUpdate di questo componente.
    /// </summary>
    public void MoveTowards(Vector2 targetPos, float speed, float dt)
    {
        Vector2 dir = targetPos - _rb.position;
        if (dir.sqrMagnitude < 0.0001f)
        {
            Stop();
            return;
        }
        _aiVelocity = dir.normalized * speed;
    }

    public void Stop()
    {
        _aiVelocity = Vector2.zero;
        _rb.linearVelocity = Vector2.zero;
    }

    /// <summary>
    /// Applica un impulso di knockback al nemico.
    /// Capped a maxKnockbackSpeed per evitare abusi da fuoco rapido.
    /// </summary>
    public void AddKnockback(Vector2 impulse)
    {
        _knockbackVelocity += impulse;

        // Il cap impedisce di spammare proiettili per bloccare un nemico sul posto
        if (_knockbackVelocity.magnitude > maxKnockbackSpeed)
            _knockbackVelocity = _knockbackVelocity.normalized * maxKnockbackSpeed;
    }
}
