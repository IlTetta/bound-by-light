using Unity.Netcode;
using UnityEngine;

public sealed class EnemyMeleeBrain : NetworkBehaviour, IEnemyEntity
{
    public enum BrainState : byte {
        Idle = 0,
        Chase = 1,
        Windup = 2,
        Active = 3,
        Recover = 4
    }

    [SerializeField] private EnemyChaserConfig config;

    [Header("Modules")]
    [SerializeField] private EnemyPerceptionSensor3D sensor;
    [SerializeField] private EnemyMotor3D motor;
    [SerializeField] private MeleeAttackTimeline attackTimeline;
    [SerializeField] private DamageOnTouchNetwork3D hitbox;

    [Header("Visual / Facing")]
    [Tooltip("Root del modello 3D (enemy_model). Usato per girare il personaggio verso il target.")]
    [SerializeField] private Transform modelTransform;
    [Tooltip("Rotazione Y quando il target è a destra (+X). Cambia a 180 se il modello guarda al contrario.")]
    [SerializeField] private float facingRightY = 0f;

    [Header("Loot")]
    [SerializeField] private int currencyReward = 5;
    [SerializeField] private float energyReward = 10f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private float deathLingerSeconds = 3f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private Transform _target;
    private float _retargetTimer;
    private bool _isDead;
    private HealthNetwork _health; // cachato in Awake — evita GetComponent in TakeDamage

    private static readonly int AnimState  = Animator.StringToHash("State");
    private static readonly int AnimIsDead = Animator.StringToHash("IsDead");

    // IEnemyEntity
    public bool IsFlying => false;
    public bool IsDiruptor => false;
    public int CurrencyReward => currencyReward;
    public float EnergyReward => energyReward;

    private readonly NetworkVariable<byte> _state = new(
        (byte)BrainState.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<ulong> _targetNetworkObjectId = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Angolo Y del modello replicato a tutti i client per il facing.</summary>
    private readonly NetworkVariable<float> _facingAngleY = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public BrainState State => (BrainState)_state.Value;
    public ulong TargetNetworkObjectId => _targetNetworkObjectId.Value;

    private void Awake() {
        // Auto-wiring per evitare null su prefab/varianti
        if (motor == null) motor = GetComponent<EnemyMotor3D>();

        // Sensor e Timeline possono stare sullo stesso GO; se li metti nei child usa GetComponentInChildren
        if (sensor == null) sensor = GetComponent<EnemyPerceptionSensor3D>();
        if (attackTimeline == null) attackTimeline = GetComponent<MeleeAttackTimeline>();

        // Hitbox spesso è in un child
        if (hitbox == null) hitbox = GetComponentInChildren<DamageOnTouchNetwork3D>(true);

        // Cache HealthNetwork per evitare GetComponent in TakeDamage (chiamato ogni frame dal tether)
        _health = GetComponent<HealthNetwork>();
    }

    public override void OnNetworkSpawn() {
        // Animazioni e facing: girano su tutti i client (prima del guard IsServer)
        _state.OnValueChanged       += OnStateChanged;
        _facingAngleY.OnValueChanged += (_, angle) => ApplyFacingAngle(angle);
        ApplyFacingAngle(_facingAngleY.Value); // applica subito per client che si connettono tardi

        var health = GetComponent<HealthNetwork>();
        if (health != null)
            health.CurrentHealth.OnValueChanged += OnHealthChanged;

        enabled = IsServer;
        if (!IsServer) return;

        ApplyConfigToHitbox();
        WireAttackToHitboxAndDebug();

        SetState(BrainState.Idle);
    }

    public override void OnNetworkDespawn() {
        _state.OnValueChanged -= OnStateChanged;

        if (_health != null)
            _health.CurrentHealth.OnValueChanged -= OnHealthChanged;
    }

    private void OnStateChanged(byte prev, byte next) {
        if (animator == null || _isDead) return;
        BrainState s = (BrainState)next;
        int animValue = s switch {
            BrainState.Chase   => 1,
            BrainState.Windup  => 2,
            BrainState.Active  => 2,
            BrainState.Recover => 2,
            _                  => 0   // Idle
        };
        animator.SetInteger(AnimState, animValue);
    }

    private void OnHealthChanged(int prev, int next) {
        if (next > 0 || prev <= 0) return;

        _isDead = true;

        if (animator != null) {
            animator.SetInteger(AnimState, -1);  // invalida State così AnyState->Running non fa match
            animator.SetBool(AnimIsDead, true);
        }

        if (IsServer) {
            motor.Stop();
            StartCoroutine(DespawnAfterAnimation());
        }
    }

    private System.Collections.IEnumerator DespawnAfterAnimation() {
        motor.Stop();
        yield return new WaitForSeconds(deathLingerSeconds);
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(destroy: true);
    }

    public bool TakeDamage(float amount)
    {
        if (_health == null || _health.IsDead) return false;
        _health.ApplyDamageServer((int)amount, Vector2.zero, 0);
        return _health.IsDead;
    }

    private void ApplyConfigToHitbox() {
        if (config == null || hitbox == null) return;

        hitbox.Damage = config.damage;
        hitbox.KnockbackForce = config.knockbackForce;
        hitbox.TargetMask = config.damageTargetMask;
        hitbox.SetHitboxEnabledServer(false);
    }

    private void WireAttackToHitboxAndDebug() {
        if (attackTimeline == null || hitbox == null) return;

        // Hitbox: attiva solo durante Active
        if (hitbox != null) {
            attackTimeline.HitWindowOpened += () => hitbox.SetHitboxEnabledServer(true);
            attackTimeline.HitWindowClosed += () => hitbox.SetHitboxEnabledServer(false);
            attackTimeline.AttackEnded += () => hitbox.SetHitboxEnabledServer(false);
        }
    }

    private void FixedUpdate() {
        if (!IsServer) return;
        if (_isDead) return;
        if (config == null || sensor == null || motor == null) return;

        float dt = Time.fixedDeltaTime;
        if (attackTimeline != null)
            attackTimeline.Tick(dt);

        // Aggiorna lo stato in base alla fase dell'attacco
        SyncStateWithAttackPhase();

        // Durante Windup/Active/Recover non retargettare (commit dell'attacco)
        bool inAttackCommit = State == BrainState.Windup || State == BrainState.Active || State == BrainState.Recover;
        if (!inAttackCommit)
            Retarget(dt);

        // Acquire target
        if (_target == null) {
            _target = sensor.FindClosestTarget(motor.Position, config.aggroRadius, config.playerMask);
            if (_target == null) {
                _targetNetworkObjectId.Value = 0;
                motor.Stop();
                SetState(BrainState.Idle);
                return;
            }
        }

        // Lose target (solo fuori dalle fasi di attacco, per evitare flicker)
        float dist = XZDistance(motor.Position, _target.position);
        float loseDist = config.aggroRadius * config.loseTargetRadiusMultiplier;

        // Lose target (solo fuori commit per evitare flicker)
        if (!inAttackCommit && dist > loseDist) {
            _target = null;
            _targetNetworkObjectId.Value = 0;
            motor.Stop();
            SetState(BrainState.Idle);
            return;
        }

        // Aggiorna target id replicato (debug + anim)
        _targetNetworkObjectId.Value = GetTargetNetworkObjectId(_target);

        // Aggiorna facing verso il target
        FaceTarget(_target.position);

        // Comportamento per stato
        switch (State) {
            case BrainState.Idle:
                SetState(BrainState.Chase);
                goto case BrainState.Chase;

            case BrainState.Chase:
                HandleChase(dist, dt);
                break;

            case BrainState.Windup:
            case BrainState.Active:
            case BrainState.Recover:
                // In attacco: tipicamente fermo
                motor.Stop();
                // quando la timeline termina, SyncStateWithAttackPhase torner� Chase o Idle
                break;
        }
    }

    private void HandleChase(float dist, float dt) {
        if (_target == null) {
            motor.Stop();
            SetState(BrainState.Idle);
            return;
        }

        // 1. Se in range di attacco e l'attacco � pronto: entra in Windup
        if (attackTimeline != null && attackTimeline.CanStart && dist <= config.attackRange) {
            motor.Stop();
            bool started = attackTimeline.TryStart(config.windup, config.active, config.cooldown);
            if (started)
                SetState(BrainState.Windup);
            return;
        }

        // 2. Stop distance
        if (dist <= config.stopDistance) {
            motor.Stop();
            return;
        }

        // 3. Chase
        motor.MoveTowards(_target.position, config.moveSpeed, dt);
    }

    private void Retarget(float dt) {
        _retargetTimer -= dt;
        if (_retargetTimer > 0f) return;

        _retargetTimer =Mathf.Max(0.02f, config.retargetInterval); // evita retarget troppo frequente

        var candidate = sensor.FindClosestTarget(motor.Position, config.aggroRadius, config.playerMask);
        if (candidate == null) return;

        if (_target == null) {
            _target = candidate;
            return;
        }

        if (candidate == _target) return;

        float curDist  = XZDistance(motor.Position, _target.position);
        float candDist = XZDistance(motor.Position, candidate.position);

        if (candDist + config.switchTargetDistanceBias < curDist)
            _target = candidate;
    }

    private void SyncStateWithAttackPhase() {
        if (attackTimeline == null) {
            if (State != BrainState.Idle && State != BrainState.Chase)
                SetState(BrainState.Chase);
            return;
        }

        switch (attackTimeline.CurrentPhase) {
            case MeleeAttackTimeline.Phase.Windup:
                if (State != BrainState.Windup)
                    SetState(BrainState.Windup);
                break;

            case MeleeAttackTimeline.Phase.Active:
                if (State != BrainState.Active)
                    SetState(BrainState.Active);
                break;

            case MeleeAttackTimeline.Phase.Recover:
                if (State != BrainState.Recover)
                    SetState(BrainState.Recover);
                break;

            case MeleeAttackTimeline.Phase.Ready:
                // Se l'attacco � finito e abbiamo un target, torna a Chase
                if (_target != null) {
                    if (State != BrainState.Chase)
                        SetState(BrainState.Chase);
                }
                else {
                    if (State != BrainState.Idle)
                        SetState(BrainState.Idle);
                }
                break;
        }
    }

    private void SetState(BrainState s) {
        _state.Value = (byte)s;
    }

    // --- Facing ---

    /// <summary>
    /// Calcola l'angolo Y verso il target nel piano XZ e lo replica via NetworkVariable.
    /// Sostituisce il vecchio approccio binario (solo sinistra/destra).
    /// </summary>
    private void FaceTarget(Vector3 targetPos)
    {
        Vector3 dir = targetPos - motor.Position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0025f) return; // troppo vicino per determinare la direzione

        float angleY = Quaternion.LookRotation(dir.normalized).eulerAngles.y;
        if (Mathf.Abs(Mathf.DeltaAngle(angleY, _facingAngleY.Value)) < 0.5f) return;

        _facingAngleY.Value = angleY;
        ApplyFacingAngle(angleY); // applica immediatamente sul server senza attendere il sync
    }

    private void ApplyFacingAngle(float angleY)
    {
        if (modelTransform != null)
            modelTransform.localRotation = Quaternion.Euler(0f, angleY, 0f);
    }

    private ulong GetTargetNetworkObjectId(Transform t) {
        if (t == null) return 0;
        var no = t.GetComponentInParent<NetworkObject>();
        return no != null ? no.NetworkObjectId : 0;
    }

    private static float XZDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected() {
        if (!drawGizmos) return;
        if (config == null) return;

        Vector3 pos = transform.position;

        // Aggro
        Gizmos.DrawWireSphere(pos, config.aggroRadius);

        // Lose target
        Gizmos.DrawWireSphere(pos, config.aggroRadius * config.loseTargetRadiusMultiplier);

        // Attack range
        Gizmos.DrawWireSphere(pos, config.attackRange);

        // Target line
        if (_target != null) {
            Gizmos.DrawLine(pos, _target.position);
        }
    }
#endif

}
