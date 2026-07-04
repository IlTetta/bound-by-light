using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody2D))]
public class KnockbackReceiver2D : NetworkBehaviour
{
    [SerializeField] private float decay = 20f;

    private Rigidbody2D _rb;
    private Vector2 _impactVelocity;

    private void Awake() => _rb = GetComponent<Rigidbody2D>();

    public override void OnNetworkSpawn() {
        // Il knockback lo appica il server
        enabled = IsServer;
    }

    public void AddImpactServer(Vector2 impulse) {
        if(!IsServer) return;
        _impactVelocity += impulse;
    }

    private void FixedUpdate() {
        float dt = Time.fixedDeltaTime;
        _impactVelocity = Vector2.MoveTowards(_impactVelocity, Vector2.zero, decay * dt);

        // Applica un piccolo MovePosition additivo
        _rb.MovePosition(_rb.position + _impactVelocity * dt);
    }
    
}
