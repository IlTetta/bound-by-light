using UnityEngine;

/// <summary>
/// Motore di movimento 3D per i nemici.
/// Sostituisce EnemyMotor2D: stessa API pubblica (MoveTowards, Stop, AddKnockback).
/// Il nemico si muove sul piano XZ. Y è bloccata alla quota di spawn.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class EnemyMotor3D : MonoBehaviour
{
    [Header("Knockback")]
    [SerializeField] private float maxKnockbackSpeed = 4f;
    [SerializeField] private float knockbackDecay    = 10f;

    [Header("Separazione (anti-overlap)")]
    [SerializeField] private float     separationRadius = 1.0f;
    [SerializeField] private float     separationForce  = 4f;
    [SerializeField] private LayerMask enemyLayer;

    private Rigidbody _rb;
    private float     _fixedY;

    private Vector3 _aiVelocity;
    private Vector3 _knockbackVelocity;

    private void Awake()
    {
        _rb              = GetComponent<Rigidbody>();
        _rb.useGravity  = false;
        _rb.constraints = RigidbodyConstraints.FreezePositionY
                        | RigidbodyConstraints.FreezeRotationX
                        | RigidbodyConstraints.FreezeRotationY
                        | RigidbodyConstraints.FreezeRotationZ;
        _fixedY = transform.position.y;
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        _knockbackVelocity = Vector3.MoveTowards(
            _knockbackVelocity, Vector3.zero, knockbackDecay * dt);

        Vector3 total = _aiVelocity + _knockbackVelocity + ComputeSeparation();

        if (total.sqrMagnitude > 0.0001f)
        {
            Vector3 next = _rb.position + total * dt;
            next.y = _fixedY;
            _rb.MovePosition(next);
        }
    }

    private Vector3 ComputeSeparation()
    {
        if (separationRadius <= 0f) return Vector3.zero;

        Collider[] neighbors = Physics.OverlapSphere(
            _rb.position, separationRadius, enemyLayer);

        Vector3 push = Vector3.zero;

        foreach (var col in neighbors)
        {
            if (col.attachedRigidbody == _rb) continue;

            Vector3 toMe = _rb.position - col.transform.position;
            toMe.y = 0f; // solo piano XZ
            float dist = toMe.magnitude;

            if (dist < 0.001f) { push += Vector3.right; continue; }

            float overlap = separationRadius - dist;
            if (overlap > 0f)
                push += toMe.normalized * overlap;
        }

        return push.sqrMagnitude > 0.0001f
            ? push.normalized * separationForce
            : Vector3.zero;
    }

    // ─── API Pubblica ─────────────────────────────────────────────────────────

    public Vector3 Position => _rb.position;

    /// <summary>Muove il nemico verso targetPos (XZ) alla velocità data.</summary>
    public void MoveTowards(Vector3 targetPos, float speed, float dt)
    {
        Vector3 dir = targetPos - _rb.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f) { Stop(); return; }

        _aiVelocity = dir.normalized * speed;
    }

    /// <summary>Compatibilità con brain che passano Vector2 (X,Z).</summary>
    public void MoveTowards(Vector2 targetXZ, float speed, float dt)
        => MoveTowards(new Vector3(targetXZ.x, _fixedY, targetXZ.y), speed, dt);

    public void Stop()
    {
        _aiVelocity        = Vector3.zero;
        _rb.linearVelocity = Vector3.zero;
    }

    public void AddKnockback(Vector2 impulse)
    {
        _knockbackVelocity += new Vector3(impulse.x, 0f, impulse.y);

        if (_knockbackVelocity.magnitude > maxKnockbackSpeed)
            _knockbackVelocity = _knockbackVelocity.normalized * maxKnockbackSpeed;
    }

    public void AddKnockback(Vector3 impulse)
    {
        impulse.y = 0f;
        AddKnockback(new Vector2(impulse.x, impulse.z));
    }
}
