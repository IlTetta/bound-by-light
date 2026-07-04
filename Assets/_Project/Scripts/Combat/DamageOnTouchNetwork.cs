using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Collider2D))]
public sealed class DamageOnTouchNetwork : NetworkBehaviour
{
    [SerializeField] private Collider2D col;

    [Header("Runtime (set by brain/config)")]
    public int Damage { get; set; } = 10;
    public float KnockbackForce { get; set; } = 6f;
    public LayerMask TargetMask { get; set; }

    private void Awake() {
        if (col == null) col = GetComponent<Collider2D>();
        col.isTrigger = true;
        col.enabled = false; // deafult: disabilitata finch? l'attacco non apre la finestra
    }

    public override void OnNetworkSpawn() {
        enabled = IsServer; // solo il server gestisce la logica di danno
        if(!IsServer) col.enabled = false; // client: collider trigger disabilitato, non gestisce danno
    }

    public void SetHitboxEnabledServer(bool enabled) {
        if (!IsServer) return;
        col.enabled = enabled;
    }

    private void OnTriggerEnter2D(Collider2D other) {
        if (!IsServer) return;

        if (((1 << other.gameObject.layer) & TargetMask.value) == 0) return; // non è un target valido

        var health = other.GetComponentInParent<HealthNetwork>();
        if (health == null) return;

        Vector2 dir = (other.transform.position - transform.position);
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
        dir.Normalize();

        Vector2 kb = dir * KnockbackForce;
        health.ApplyDamageServer(Damage, kb, OwnerClientId);
    }
}
