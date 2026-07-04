using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Danno da contatto 3D. Sostituisce DamageOnTouchNetwork (2D).
/// Usa Collider 3D e OnTriggerEnter invece di Collider2D/OnTriggerEnter2D.
/// Stessa API pubblica: Damage, KnockbackForce, TargetMask, SetHitboxEnabledServer.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class DamageOnTouchNetwork3D : NetworkBehaviour
{
    [SerializeField] private Collider col;

    public int       Damage         { get; set; } = 10;
    public float     KnockbackForce { get; set; } = 6f;
    public LayerMask TargetMask     { get; set; }

    private void Awake()
    {
        if (col == null) col = GetComponent<Collider>();
        col.isTrigger = true;
        col.enabled   = false;
    }

    public override void OnNetworkSpawn()
    {
        enabled = IsServer;
        if (!IsServer) col.enabled = false;
    }

    public void SetHitboxEnabledServer(bool enable)
    {
        if (!IsServer) return;
        col.enabled = enable;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (((1 << other.gameObject.layer) & TargetMask.value) == 0) return;

        var health = other.GetComponentInParent<HealthNetwork>();
        if (health == null) return;

        // knockback su piano XZ
        Vector3 dir = other.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
        dir.Normalize();

        Vector2 kb = new Vector2(dir.x, dir.z) * KnockbackForce;
        health.ApplyDamageServer(Damage, kb, OwnerClientId);
    }
}
