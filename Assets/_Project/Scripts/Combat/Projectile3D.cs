using UnityEngine;
using Unity.Netcode;
using System.Collections;

/// <summary>
/// Proiettile 3D. Sostituisce Projectile (2D).
/// Modalità piatta (default): si muove sul piano XZ a quota costante.
/// Modalità inclinata (FlatTrajectory = false): viaggia nella direzione 3D completa,
/// utile per proiettili che devono scendere verso target più bassi del punto di spawn.
/// Stessa API pubblica: Initialize, SetDamage, SetOwner, SetFiredByEnemy.
/// </summary>
public class Projectile3D : NetworkBehaviour
{
    [Header("Movement")]
    public float Speed       = 20f;
    public float Acceleration = 0f;
    public float MaxSpeed    = 50f;

    [Header("Orientation")]
    public bool FaceMovement = true;

    [Header("Trajectory")]
    [Tooltip("true  = volo piatto sul piano XZ a quota costante (proiettili player).\n" +
             "false = direzione 3D completa, il proiettile segue la Y della direzione iniziale\n" +
             "        (utile per boss più alti che sparano verso il basso).")]
    public bool FlatTrajectory = true;

    [Header("Safety & Lifecycle")]
    public float InitialInvulnerabilityDuration = 0.05f;
    public float LifeTime   = 5f;
    public bool  DamageOwner = false;

    [Header("Collision")]
    [Tooltip("Layer che fermano il proiettile (muri, ostacoli). Se vuoto, il proiettile si ferma SOLO su oggetti con HealthNetwork.")]
    public LayerMask BlockingLayers;

    [Header("Damage")]
    public int Damage = 10;

    protected Vector3 _direction;
    protected float   _currentSpeed;
    protected ulong   _ownerId;
    protected bool    _isInitialized = false;
    private   bool    _firedByEnemy  = false;
    private   int     _damage;

    // Direzione e quota di spawn replicate a TUTTI i client come parte del payload
    // di spawn del NetworkObject. Più affidabile di una ClientRpc inviata nello stesso
    // frame dello Spawn(): garantisce che ogni client (host + joiner) inizializzi il
    // proiettile e ne simuli il movimento localmente, anche senza NetworkTransform.
    private readonly NetworkVariable<Vector3> _netDirection = new(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> _netSpawnY = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Rigidbody  _rb;
    private Collider   _col;
    private float      _fixedY;

    // true se il prefab ha un NetworkTransform: in quel caso la posizione è
    // replicata dal server (server authority) e i client NON simulano localmente.
    private bool _hasNetworkTransform;

    private void Awake()
    {
        _rb  = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();

        if (_rb != null)
        {
            _rb.useGravity    = false;
            _rb.isKinematic   = false;   // non-kinematic: trigger events funzionano vs kinematic RB
            _rb.freezeRotation = true;
            _rb.interpolation  = RigidbodyInterpolation.Interpolate;
        }
    }

    // ─── NGO ──────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _hasNetworkTransform =
            GetComponent<Unity.Netcode.Components.NetworkTransform>() != null;

        if (IsServer) return; // il server inizializza direttamente in Initialize()

        if (_hasNetworkTransform)
        {
            // Posizione e rotazione sono guidate dal NetworkTransform (server authority).
            // Il client NON simula: RB kinematic per non litigare con la fisica locale.
            if (_rb != null) _rb.isKinematic = true;
            if (_col != null) _col.enabled   = true;
            return;
        }

        // Nessun NetworkTransform: il client simula il movimento localmente usando
        // la direzione replicata via NetworkVariable (inclusa nel payload di spawn).
        _netDirection.OnValueChanged += OnNetDirectionChanged;
        if (_netDirection.Value != Vector3.zero)
            InitializeFromNetwork(_netDirection.Value, _netSpawnY.Value);
    }

    public override void OnNetworkDespawn()
    {
        _netDirection.OnValueChanged -= OnNetDirectionChanged;
        base.OnNetworkDespawn();
    }

    private void OnNetDirectionChanged(Vector3 previous, Vector3 current)
    {
        if (current != Vector3.zero)
            InitializeFromNetwork(current, _netSpawnY.Value);
    }

    // ─── API Pubblica ─────────────────────────────────────────────────────────

    /// <summary>
    /// Configura il proiettile. direction è un vettore XZ (Y ignorato).
    /// Chiamato SOLO dal server. Pubblica direzione e quota via NetworkVariable
    /// così i client possono simulare il movimento localmente.
    /// </summary>
    public void Initialize(Vector3 direction)
    {
        _fixedY = transform.position.y;
        if (FlatTrajectory) direction.y = 0f;   // piatto: ignora Y
        _direction     = direction.normalized;
        _currentSpeed  = Speed;
        _isInitialized = true;
        _damage        = Damage;

        if (_col != null) _col.enabled = true;

        if (FaceMovement && _direction != Vector3.zero)
        {
            // Ruota nel piano XZ verso la direzione di volo
            transform.rotation = Quaternion.LookRotation(_direction, Vector3.up);
        }

        // Replica ai client (incluso nel payload di spawn se settato nello stesso frame)
        if (IsServer)
        {
            _netDirection.Value = _direction;
            _netSpawnY.Value    = _fixedY;
        }

        StartCoroutine(LifeTimeRoutine());
        StartCoroutine(InitialInvulnerabilityRoutine());
    }

    public void SetDamage(int damage)   => _damage = damage;
    public void SetOwner(ulong ownerId) => _ownerId = ownerId;
    public void SetFiredByEnemy(bool v) => _firedByEnemy = v;

    // ─── Sync direzione ai client ─────────────────────────────────────────────

    /// <summary>
    /// Inizializzazione client-side a partire dalla direzione replicata via
    /// NetworkVariable. Senza questo i client ricevono il NetworkObject ma non lo
    /// vedono muoversi (nessun NetworkTransform sul prefab).
    /// </summary>
    private void InitializeFromNetwork(Vector3 direction, float spawnY)
    {
        if (_isInitialized) return; // già inizializzato

        _fixedY        = spawnY;
        _direction     = direction.normalized;
        _currentSpeed  = Speed;
        _damage        = Damage;
        _isInitialized = true;
        if (_col != null) _col.enabled = true;
        if (FaceMovement && _direction != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(_direction, Vector3.up);
        StartCoroutine(InitialInvulnerabilityRoutine());
    }

    // ─── Movement ─────────────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        if (!_isInitialized) return;
        // Con NetworkTransform la posizione dei client la guida il server: niente sim locale.
        if (!IsServer && _hasNetworkTransform) return;

        if (Acceleration != 0f)
            _currentSpeed = Mathf.MoveTowards(
                _currentSpeed, MaxSpeed, Acceleration * Time.fixedDeltaTime);

        Vector3 vel = _direction * _currentSpeed;
        if (FlatTrajectory) vel.y = 0f;   // piatto: nessuna componente verticale
        _rb.linearVelocity = vel;

        // Corregge deriva verticale accumulata in modalità piatta
        if (FlatTrajectory && Mathf.Abs(transform.position.y - _fixedY) > 0.01f)
        {
            Vector3 p = transform.position;
            p.y = _fixedY;
            _rb.position = p;
        }
    }

    // ─── Collision ────────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || !_isInitialized) return;

        var targetNetObj = other.GetComponentInParent<NetworkObject>();

        if (!DamageOwner && targetNetObj != null
            && targetNetObj.NetworkObjectId == _ownerId) return;

        if (_firedByEnemy && other.GetComponentInParent<IEnemyEntity>() != null) return;
        if (!_firedByEnemy && other.GetComponentInParent<PlayerHealth>() != null) return;

        var health = other.GetComponentInParent<HealthNetwork>();
        if (health != null)
        {
            Vector2 kb = new Vector2(_direction.x, _direction.z) * 5f;
            health.ApplyDamageServer(_damage, kb, _ownerId);
            OnDeath();
            return;
        }

        // Ferma il proiettile solo se il layer è in BlockingLayers (muri, ostacoli).
        // Se BlockingLayers è vuoto il proiettile attraversa pavimento/muri e
        // si ferma solo sui damageable.
        if (BlockingLayers.value != 0 && (BlockingLayers.value & (1 << other.gameObject.layer)) != 0)
            OnDeath();
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private IEnumerator InitialInvulnerabilityRoutine()
    {
        if (_col == null) yield break;
        _col.enabled = false;
        yield return new WaitForSeconds(InitialInvulnerabilityDuration);
        _col.enabled = true;
    }

    private IEnumerator LifeTimeRoutine()
    {
        yield return new WaitForSeconds(LifeTime);
        var netObj = GetComponent<NetworkObject>();
        if (IsServer && netObj != null && netObj.IsSpawned)
            OnDeath();
    }

    protected virtual void OnDeath()
    {
        _isInitialized = false;
        // linearVelocity non è supportata su Kinematic — il movimento si ferma
        // semplicemente perché FixedUpdate esce subito quando !_isInitialized
        if (_col != null) _col.enabled = false;

        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
            netObj.Despawn(true);
    }
}
