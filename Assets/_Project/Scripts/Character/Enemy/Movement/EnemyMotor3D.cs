using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Motore di movimento 3D dei nemici basato su NavMeshAgent (pathfinding attorno a
/// muri e ostacoli). API pubblica invariata (MoveTowards, Stop, Position, AddKnockback):
/// i brain non sanno che sotto c'è la navmesh.
///
/// MULTIPLAYER: la navigazione gira SOLO sul server (l'agent è disabilitato sui client
/// via <see cref="ConfigureNavigation"/>). La posizione viene replicata a tutti i client
/// dal NetworkTransform del nemico (server-authority): una sola simulazione, gli altri
/// replicano → nessun rischio di desync da pathfinding indipendenti.
///
/// Il Rigidbody è reso kinematic: il transform lo muove l'agent, la fisica non deve
/// interferire. Il collider serve solo per i trigger (venire colpiti). L'anti-overlap
/// tra nemici è affidato all'avoidance integrata del NavMeshAgent (impostabile sul prefab).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class EnemyMotor3D : MonoBehaviour, IKnockbackReceiver
{
    /// <summary>IKnockbackReceiver: HealthNetwork instrada qui il knockback (server-side).</summary>
    public void ApplyKnockback(Vector2 impulseXZ) => AddKnockback(impulseXZ);

    [Header("Ripathing")]
    [Tooltip("Intervallo minimo (s) tra due ricalcoli del path. Throttla SetDestination, " +
             "che è costoso. Il path viene comunque ricalcolato subito se il target si sposta molto.")]
    [SerializeField] private float repathInterval = 0.15f;

    // Stat di design: NON serializzate. Arrivano da EnemyBaseConfig via ConfigureStats().
    // La separazione anti-overlap ora è gestita dall'avoidance del NavMeshAgent (sul prefab),
    // quindi separationRadius/Force del config sono ignorati qui.
    private float maxKnockbackSpeed = 4f;
    private float knockbackDecay    = 10f;

    private Rigidbody    _rb;
    private NavMeshAgent _agent;
    private bool         _isServer;

    private Vector3 _knockbackVelocity;
    private float   _repathTimer;
    private Vector3 _lastDestination;

    /// <summary>Applica le stat dal config del nemico. Da chiamare in Awake.</summary>
    public void ConfigureStats(float newMaxKnockbackSpeed, float newKnockbackDecay,
                               float newSeparationRadius, float newSeparationForce)
    {
        maxKnockbackSpeed = newMaxKnockbackSpeed;
        knockbackDecay    = newKnockbackDecay;
        // separationRadius/Force: gestiti dall'avoidance del NavMeshAgent (Radius/Priority sul prefab).
    }

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;   // l'agent muove il transform; la fisica non interferisce
        _rb.useGravity  = false;

        _agent = GetComponent<NavMeshAgent>();
        _agent.updateRotation = false; // il facing lo gestisce il brain sul modelTransform
        _agent.enabled = false;        // abilitato solo sul server (ConfigureNavigation)
    }

    /// <summary>
    /// Attiva la navigazione solo sul server. Sui client l'agent resta spento:
    /// la posizione arriva dal NetworkTransform.
    /// </summary>
    public void ConfigureNavigation(bool isServer)
    {
        _isServer = isServer;
        if (_agent != null) _agent.enabled = isServer;
    }

    private void FixedUpdate()
    {
        if (!_isServer || _agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;

        float dt = Time.fixedDeltaTime;

        // Knockback: decade e viene applicato con agent.Move, che resta sulla navmesh
        // (non spinge il nemico dentro i muri).
        _knockbackVelocity = Vector3.MoveTowards(_knockbackVelocity, Vector3.zero, knockbackDecay * dt);
        if (_knockbackVelocity.sqrMagnitude > 0.0001f)
            _agent.Move(_knockbackVelocity * dt);
    }

    // ─── API Pubblica (invariata per i brain) ──────────────────────────────────

    public Vector3 Position => transform.position;

    /// <summary>Instrada il nemico verso targetPos calcolando un path sulla navmesh.</summary>
    public void MoveTowards(Vector3 targetPos, float speed, float dt)
    {
        if (!_isServer || _agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;

        _agent.speed     = speed;
        _agent.isStopped = false;

        // Throttle di SetDestination (costoso): ricalcola a intervalli, o subito se il
        // target si è spostato di oltre ~0.5 unità.
        _repathTimer -= dt;
        if (_repathTimer <= 0f || (targetPos - _lastDestination).sqrMagnitude > 0.25f)
        {
            _repathTimer     = repathInterval;
            _lastDestination = targetPos;
            _agent.SetDestination(targetPos);
        }
    }

    /// <summary>Compatibilità con brain che passano Vector2 (X,Z).</summary>
    public void MoveTowards(Vector2 targetXZ, float speed, float dt)
        => MoveTowards(new Vector3(targetXZ.x, transform.position.y, targetXZ.y), speed, dt);

    public void Stop()
    {
        if (!_isServer || _agent == null || !_agent.enabled) return;
        if (_agent.isOnNavMesh)
        {
            _agent.isStopped = true;
            _agent.velocity  = Vector3.zero;
        }
        _repathTimer = 0f; // il prossimo MoveTowards ricalcola subito
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
