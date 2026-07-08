using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Ricevitore di knockback 3D. Sostituisce KnockbackReceiver2D.
/// L'impulso agisce sul piano XZ (Y bloccato).
/// Solo il server applica il knockback.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class KnockReceiver3D : NetworkBehaviour
{
    [Tooltip("Sui PLAYER è il valore effettivo.\n" +
             "Sui NEMICI viene sovrascritto da EnemyBaseConfig.knockReceiverDecay.")]
    [SerializeField] private float decay = 20f;

    /// <summary>Applica le stat dal config del nemico. Da chiamare in Awake.</summary>
    public void ConfigureStats(float newDecay) => decay = Mathf.Max(0f, newDecay);

    private Rigidbody _rb;
    private Vector3   _impactVelocity;
    private float     _fixedY;

    private void Awake()
    {
        _rb    = GetComponent<Rigidbody>();
        _fixedY = transform.position.y;
    }

    public override void OnNetworkSpawn()
    {
        enabled = IsServer;
    }

    /// <summary>Applica un impulso di knockback (solo server). Force in XZ.</summary>
    public void AddImpactServer(Vector2 impulse)
    {
        if (!IsServer) return;
        _impactVelocity += new Vector3(impulse.x, 0f, impulse.y);
    }

    /// <summary>Overload con Vector3 — Y ignorata.</summary>
    public void AddImpactServer(Vector3 impulse)
        => AddImpactServer(new Vector2(impulse.x, impulse.z));

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        _impactVelocity = Vector3.MoveTowards(
            _impactVelocity, Vector3.zero, decay * dt);

        if (_impactVelocity.sqrMagnitude > 0.0001f)
        {
            Vector3 next = _rb.position + _impactVelocity * dt;
            next.y = _fixedY;
            _rb.MovePosition(next);
        }
    }
}
