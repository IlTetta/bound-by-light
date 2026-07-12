using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Motore di movimento 3D isometrico.
/// Il player si muove sul piano XZ (Y = su, bloccato a Y costante).
/// Sostituisce PlayerMovementMotor2D per il passaggio a fisica 3D.
/// Stessa API pubblica: SetMoveInput, AddImpact, SetTetherForce, SetLocked.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class PlayerMovementMotor3D : NetworkBehaviour, IKnockbackReceiver
{
    public enum MoveMode { Free, Strict4, Strict8 }

    /// <summary>IKnockbackReceiver: HealthNetwork instrada qui il knockback (owner-side).</summary>
    public void ApplyKnockback(Vector2 impulseXZ) => AddImpact(impulseXZ);

    [Header("Tuning")]
    [SerializeField] private MoveMode mode        = MoveMode.Free;
    [SerializeField] private float    maxSpeed     = 6f;
    [SerializeField] private float    acceleration = 40f;
    [SerializeField] private float    deceleration = 55f;
    [SerializeField] private float    deadZone     = 0.05f;

    [Header("Knockback")]
    [SerializeField] private float impactDecay = 20f;

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 720f; // gradi al secondo

    // ─── Componenti ───────────────────────────────────────────────────────────
    private Rigidbody _rb;
    private float     _fixedY; // quota Y bloccata al momento dello spawn

    // ─── Velocità ─────────────────────────────────────────────────────────────
    private Vector3 _moveVelocity;
    private Vector3 _impactVelocity;
    private Vector3 _tetherVelocity;

    public bool IsLocked { get; private set; } = false;

    // ─── Unity / NGO ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _rb              = GetComponent<Rigidbody>();
        _rb.useGravity   = false;
        // Blocca Y e TUTTE le rotazioni: il player non deve mai ruotare
        _rb.constraints  = RigidbodyConstraints.FreezePositionY
                         | RigidbodyConstraints.FreezeRotationX
                         | RigidbodyConstraints.FreezeRotationY
                         | RigidbodyConstraints.FreezeRotationZ;
    }

    private void Start()
    {
        // Fallback per test locali senza NetworkManager avviato
        if (!IsSpawned)
        {
            _fixedY = transform.position.y;
            CoopCameraController.Instance?.RegisterPlayer(transform);
        }
    }

    public override void OnDestroy()
    {
        if (!IsSpawned)
            CoopCameraController.Instance?.UnregisterPlayer(transform);
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        _fixedY = transform.position.y;
        enabled  = IsOwner;
        CoopCameraController.Instance?.RegisterPlayer(transform);
    }

    public override void OnNetworkDespawn()
    {
        CoopCameraController.Instance?.UnregisterPlayer(transform);
    }

    // ─── API Pubblica ─────────────────────────────────────────────────────────

    /// <summary>
    /// Imposta la direzione di movimento dall'input (owner).
    /// Input.x = asse X, Input.y = asse Z (avanti/indietro in isometrico).
    /// </summary>
    public void SetMoveInput(Vector2 rawInput, bool run = false, float runMultiplier = 1f)
    {
        if (!IsOwner || IsLocked) return;

        Vector2 input2D = ApplyMode(rawInput);
        input2D = input2D.magnitude < deadZone ? Vector2.zero : input2D.normalized;

        // Converte input 2D (XY) → velocità 3D (XZ)
        // Con camera Y=180 (guarda verso -Z): X e Z entrambi negati per corrispondere allo schermo
        Vector3 inputDir = new Vector3(-input2D.x, 0f, -input2D.y);
        float   speed    = maxSpeed * (run ? runMultiplier : 1f);
        float   rate     = (inputDir == Vector3.zero) ? deceleration : acceleration;

        _moveVelocity = Vector3.MoveTowards(
            _moveVelocity, inputDir * speed, rate * Time.fixedDeltaTime);
    }

    /// <summary>Impulso singolo (knockback, dash). Si accumula e decade.</summary>
    public void AddImpact(Vector2 force)
    {
        if (!IsOwner || IsLocked) return;
        _impactVelocity += new Vector3(force.x, 0f, force.y);
    }

    /// <summary>Forza continua del tether. Chiamata ogni frame da TetherPhysics.</summary>
    public void SetTetherForce(Vector2 force)
    {
        if (!IsOwner) return;
        _tetherVelocity = new Vector3(force.x, 0f, force.y);
    }

    public void SetLocked(bool locked)
    {
        IsLocked = locked;
        if (locked)
        {
            _moveVelocity   = Vector3.zero;
            _impactVelocity = Vector3.zero;
            _tetherVelocity = Vector3.zero;
            if (_rb) _rb.linearVelocity = Vector3.zero;
        }
    }

    // ─── Teleport ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Teleporta il player a <paramref name="position"/>. Chiamabile dal server
    /// (es. respawn al checkpoint). Il NetworkTransform del player è owner-authority:
    /// quindi la posizione va impostata SULL'OWNER, altrimenti l'owner la sovrascrive
    /// subito con la propria. Il server inoltra la richiesta all'owner via RPC.
    /// </summary>
    public void TeleportFromServer(Vector3 position)
    {
        if (!IsServer) return;
        TeleportOwnerRpc(position);
    }

    [Rpc(SendTo.Owner)]
    private void TeleportOwnerRpc(Vector3 position)
    {
        // Eseguito sull'owner (authority del NetworkTransform): la nuova posizione
        // si propaga a tutti gli altri client tramite il NetworkTransform.
        _fixedY = position.y;
        _moveVelocity   = Vector3.zero;
        _impactVelocity = Vector3.zero;
        _tetherVelocity = Vector3.zero;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.position       = position;
        }
        transform.position = position;
    }

    // ─── FixedUpdate ──────────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        if (!IsOwner) return;

        float dt = Time.fixedDeltaTime;

        if (IsLocked)
        {
            // Solo il tether può ancora spingere il player bloccato
            _rb.linearVelocity = new Vector3(_tetherVelocity.x, 0f, _tetherVelocity.z);
            _tetherVelocity = Vector3.zero;
            return;
        }

        _impactVelocity = Vector3.MoveTowards(
            _impactVelocity, Vector3.zero, impactDecay * dt);

        Vector3 finalVelocity = _moveVelocity + _impactVelocity + _tetherVelocity;
        finalVelocity.y = 0f;
        _tetherVelocity = Vector3.zero;

        _rb.linearVelocity = finalVelocity;

        if (_moveVelocity.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(-_moveVelocity.normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeed * dt);
        }
    }

    // ─── Modalità direzionali ─────────────────────────────────────────────────

    private Vector2 ApplyMode(Vector2 input)
    {
        if (input.sqrMagnitude < 0.0001f) return Vector2.zero;

        switch (mode)
        {
            case MoveMode.Strict4:
                return Mathf.Abs(input.x) >= Mathf.Abs(input.y)
                    ? new Vector2(Mathf.Sign(input.x), 0f)
                    : new Vector2(0f, Mathf.Sign(input.y));

            case MoveMode.Strict8:
                float angle = Mathf.Atan2(input.y, input.x) * Mathf.Rad2Deg;
                angle = Mathf.Round(angle / 45f) * 45f;
                float rad = angle * Mathf.Deg2Rad;
                return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

            default:
                return input;
        }
    }
}
