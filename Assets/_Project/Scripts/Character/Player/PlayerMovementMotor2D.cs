using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody2D))]
public sealed class PlayerMovementMotor2D : NetworkBehaviour, IKnockbackReceiver
{
    public enum MoveMode { Free, Strict4, Strict8 }

    /// <summary>IKnockbackReceiver: HealthNetwork instrada qui il knockback (owner-side).</summary>
    public void ApplyKnockback(Vector2 impulseXZ) => AddImpact(impulseXZ);

    [Header("Tuning")]
    [SerializeField] private MoveMode mode = MoveMode.Free;
    [SerializeField] private float maxSpeed = 6f;
    [SerializeField] private float acceleration = 40f;
    [SerializeField] private float deceleration = 55f;
    [SerializeField] private float deadZone = 0.05f; // ignore small input

    [Header("Optional impact/knockback")]
    [SerializeField] private float impactDecay = 20f; // pi� alto = si ferma pi� velocemente

    private Rigidbody2D _rb;

    private Vector2 _moveVelocity; // velocità controllata dall'input
    private Vector2 _impactVelocity; // knockback/impulsi
    private Vector2 _tetherVelocity; // forza continua del tether

    // Rotazione iniziale del prefab — viene bloccata ogni frame per evitare drift
    private Quaternion _lockedRotation;

    // Quando true, ignora qualsiasi input e azzera le velocit�.
    // Usato da PlayerFaintHandler per bloccare il movimento al faint.
    public bool IsLocked { get; private set; } = false;

    private void Awake() {
        _rb = GetComponent<Rigidbody2D>();
        _lockedRotation = transform.rotation; // salva la rotazione iniziale del prefab
        _rb.gravityScale = 0f; // no gravity for top-down movement
        _rb.freezeRotation = true; // prevent rotation from physics
        // NECESSARIO: senza questo flag, i proiettili (anch'essi Kinematic) non generano
        // OnTriggerEnter2D sul player, perché Unity 2D richiede useFullKinematicContacts=true
        // su ENTRAMBI i Kinematic body perché i trigger vadano a buon fine.
        _rb.useFullKinematicContacts = true;
    }

    public override void OnNetworkSpawn() {
        // Il movimento lo calcola solo il proprietario, il knockback solo il server
        // Ma entrambi devono essere attivi
        enabled = IsOwner;
    }

    public void SetLocked(bool locked)
    {
        IsLocked = locked;
        if (locked)
        {
            _moveVelocity = Vector2.zero;
            _impactVelocity = Vector2.zero;
            _tetherVelocity = Vector2.zero;
            if (_rb != null)
            {
                _rb.linearVelocity = Vector2.zero;
                _rb.angularVelocity = 0f;
            }
        }
    }

    public void SetMoveInput (Vector2 rawInput, bool run = false, float runMultiplier = 1.0f) {
        // Solo il proprietario pu� controllare il movimento
        if (!IsOwner) return;
        if (IsLocked) return;

        Vector2 input = ApplyMode(rawInput);
        input = input.magnitude < deadZone ? Vector2.zero : input.normalized;

        float speed = maxSpeed * (run ? runMultiplier : 1f);
        float dt = Time.fixedDeltaTime;
        float rate = (input == Vector2.zero) ? deceleration : acceleration;
        _moveVelocity = Vector2.MoveTowards(_moveVelocity, input * speed, rate * dt);
    }

    /// <summary>
    /// Impulso singolo (knockback nemici, dash). Si accumula e decade nel tempo.
    /// </summary>
    public void AddImpact(Vector2 force)
    {
        if (!IsOwner) return;
        if (IsLocked) return;
        _impactVelocity += force;
    }

    /// <summary>
    /// Forza continua del tether. Va chiamata ogni frame dal TetherPhysics.
    /// Sovrascrive il valore precedente invece di accumularlo,
    /// cos� non esplode se chiamata ogni FixedUpdate.
    /// Passa Vector2.zero quando il player � entro maxDistance.
    /// </summary>
    public void SetTetherForce(Vector2 force)
    {
        if (!IsOwner) return;
        // Non blocchiamo il tether force durante il faint:
        // il tether deve poter tirare anche un player fainted
        _tetherVelocity = force;
    }

    private void FixedUpdate() {
        if (!IsOwner) return; // solo il proprietario muove il personaggio, ma il server gestisce il knockback

        float dt = Time.fixedDeltaTime;

        if (IsLocked)
        {
            // Anche se locked, il tether pu� spostarlo (tiro del compagno)
            if (_tetherVelocity != Vector2.zero)
            {
                _rb.MovePosition(_rb.position + _tetherVelocity * dt);
                _tetherVelocity = Vector2.zero;
            }
            else
            {
                _rb.linearVelocity = Vector2.zero;
            }
            return;
        }

        // Decay impact velocity over time
        _impactVelocity = Vector2.MoveTowards(_impactVelocity, Vector2.zero, impactDecay * dt);

        // La tether force viene azzerata ogni frame dopo l'uso:
        // TetherPhysics la rimette ogni FixedUpdate se necessario
        Vector2 finalVelocity = _moveVelocity + _impactVelocity + _tetherVelocity;
        _tetherVelocity = Vector2.zero;

        _rb.MovePosition(_rb.position + finalVelocity * dt);

        // Blocca la rotazione al valore iniziale del prefab per evitare drift da NetworkTransform
        transform.rotation = _lockedRotation;
    }

    private Vector2 ApplyMode(Vector2 input) {
        if (input.sqrMagnitude < 0.0001f) return Vector2.zero;

        switch (mode) {
            case MoveMode.Strict4: {
                    // Asse dominante: elimina completamente le diagonali
                    if (Mathf.Abs(input.x) >= Mathf.Abs(input.y))
                        return new Vector2(Mathf.Sign(input.x), 0f);
                    else
                        return new Vector2(0f, Mathf.Sign(input.y));
                }

            case MoveMode.Strict8: {
                    // 8 direzioni: quantizza l'angolo a multipli di 45�
                    float angle = Mathf.Atan2(input.y, input.x) * Mathf.Rad2Deg;
                    angle = Mathf.Round(angle / 45f) * 45f;
                    float rad = angle * Mathf.Deg2Rad;
                    return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                }

            default:
                return input; // Free
        }
    }

}
