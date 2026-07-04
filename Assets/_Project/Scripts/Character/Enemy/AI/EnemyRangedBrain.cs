using UnityEngine;
using Unity.Netcode;

public sealed class EnemyRangedBrain : NetworkBehaviour, IEnemyEntity
{
    private enum BrainState : byte {
        Idle = 0,
        Chase = 1,
        Kite = 2,
        Reposition = 3,
        Windup = 4,
        Active = 5,
        Recover = 6
    }

    // --- Inspector fields ---
    [Header("Config")]
    [SerializeField] private EnemyRangedConfig config;

    [Header("Modules")]
    [SerializeField] private EnemyPerceptionSensor3D sensor;
    [SerializeField] private EnemyMotor3D motor;
    [SerializeField] private MeleeAttackTimeline attackTimeline;

    [Header("Telegraph")]
    [Tooltip("Transform dell'anchor del quad telegraph (figlio TelegraphAnchor nel prefab).")]
    [SerializeField] private Transform telegraphAnchor;
    [Tooltip("Larghezza del quad in unità mondo.")]
    [SerializeField] private float telegraphWidth = 0.8f;
    [Tooltip("Lunghezza massima del telegraph in unità mondo (cappa la distanza visiva).")]
    [SerializeField] private float telegraphLength = 12f;
    [Tooltip("Altezza del quad dal pavimento. Piccolo offset per evitare z-fighting col terreno.")]
    [SerializeField] private float telegraphGroundOffset = 0.05f;

    [Header("LoS / Reposition")]
    [Tooltip("Layer degli ostacoli. Se lasciato a 0 il check LoS � disabilitato.")]
    [SerializeField] private LayerMask obstacleMask;
    [Tooltip("Passo laterale per ogni tentativo di strafe.")]
    [SerializeField] private float repositionStepSize = 0.8f;
    [Tooltip("Numero massimo di step laterali prima di rinunciare.")]
    [SerializeField] private int repositionMaxSteps = 6;
    [Tooltip("Velocit� durante il Reposition.")]
    [SerializeField] private float repositionSpeed = 3.5f;

    [Header("Predictive Aim (opzionale - lasciare OFF in demo)")]
    [Tooltip("OFF = mira congelata al momento del Windup (comportamento di default, consigliato).\n" +
             "ON  = mira predittiva basata sulla velocit� stimata del player.\n" +
             "      Richiede calibrazione di Projectile Speed con il sistema proiettili reale.")]
    [SerializeField] private bool enablePredictiveAim = false;
    [Tooltip("Velocit� del proiettile (unit�/s) usata per stimare il tempo di volo. " +
             "Deve corrispondere alla velocit� reale del proiettile.")]
    [SerializeField] private float projectileSpeed = 10f;
    [Tooltip("Smoothing sulla stima della velocit� del player. 0 = nessun, ~0.9 = molto smooth.")]
    [Range(0f, 0.95f)]
    [SerializeField] private float velocitySmoothing = 0.6f;

    [Header("Visual / Facing")]
    [Tooltip("Root del modello 3D (Nemico_Ranged). Usato per girare il personaggio verso il target.")]
    [SerializeField] private Transform modelTransform;

    [Header("Loot")]
    [SerializeField] private int currencyReward = 5;
    [SerializeField] private float energyReward = 10f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private float deathLingerSeconds = 3f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    // --- Stato interno ---
    private Transform _target;
    private bool _isDead;
    private HealthNetwork _health; // cachato in OnNetworkSpawn — evita GetComponent in TakeDamage

    private static readonly int AnimState      = Animator.StringToHash("State");
    private static readonly int AnimIsDead    = Animator.StringToHash("IsDead");
    private static readonly int AnimIsShooting = Animator.StringToHash("IsShooting");
    private IRangedWeapon _weapon;
    private float _retargetTimer;

    // Aim congelato: impostato al TryStart, usato per tutta la durata del Windup e al fire
    private Vector2 _lockedAimTarget;
    private bool _aimLocked;

    // Predictive aim (solo se enablePredictiveAim = true)
    private Vector2 _targetPosLast;
    private Vector2 _targetVelocityEst;

    // Reposition
    private Vector2 _repositionGoal;
    private bool _repositionGoalSet;

    // NetworkVariables
    private readonly NetworkVariable<byte> _state =
        new NetworkVariable<byte>((byte)BrainState.Idle,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<ulong> _targetNetworkObjectId =
        new NetworkVariable<ulong>(0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    /// <summary>Angolo Y del modello replicato a tutti i client per il facing.</summary>
    private readonly NetworkVariable<float> _facingAngleY = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // IEnemyEntity
    public bool IsFlying => false;
    public bool IsDiruptor => false;
    public int CurrencyReward => currencyReward;
    public float EnergyReward => energyReward;

    // --- Lifecycle ---
    public override void OnNetworkSpawn() {
        Debug.Log($"[RangedBrain] OnNetworkSpawn name={name} IsServer={IsServer} IsClient={IsClient}", this);

        // Animazioni e facing: subscriptions prima del guard IsServer (girano su tutti i client)
        _state.OnValueChanged        += OnStateChanged;
        _facingAngleY.OnValueChanged += (_, angle) => ApplyFacingAngle(angle);
        ApplyFacingAngle(_facingAngleY.Value); // applica subito per client che si connettono tardi
        _health = GetComponent<HealthNetwork>();
        if (_health != null)
            _health.CurrentHealth.OnValueChanged += OnHealthChanged;

        enabled = IsServer; // Solo il server esegue la logica dell'AI
        if (!IsServer) return;

        if (motor == null) motor = GetComponent<EnemyMotor3D>();
        if (sensor == null) sensor = GetComponent<EnemyPerceptionSensor3D>();
        if (attackTimeline == null) attackTimeline = GetComponent<MeleeAttackTimeline>();
        _weapon = GetComponentInChildren<IRangedWeapon>(true);

        attackTimeline.HitWindowOpened += OnHitWindowOpened;
        attackTimeline.AttackStarted   += OnAttackStarted;
        attackTimeline.HitWindowClosed += () => SetState(BrainState.Recover);
        attackTimeline.AttackEnded     += OnAttackEnded;

        SetTelegraphClientRpc(false, Vector2.zero, Vector2.zero);
        SetState(BrainState.Idle);
    }

    public override void OnNetworkDespawn() {
        _state.OnValueChanged -= OnStateChanged;
        if (_health != null)
            _health.CurrentHealth.OnValueChanged -= OnHealthChanged;
    }

    private void OnDestroy() {
        if (attackTimeline == null) return;
        attackTimeline.HitWindowOpened -= OnHitWindowOpened;
        attackTimeline.AttackStarted   -= OnAttackStarted;
        attackTimeline.AttackEnded     -= OnAttackEnded;
    }

    public bool TakeDamage(float amount)
    {
        if (_health == null || _health.IsDead) return false;
        _health.ApplyDamageServer((int)amount, Vector2.zero, 0);
        return _health.IsDead;
    }

    // --- FixedUpdate ---
    private void FixedUpdate() {
        if (!IsServer) return;
        if (_isDead) return;

        if (config == null || sensor == null || motor == null || attackTimeline == null) {
            Debug.LogError($"[{name}] EnemyRangedBrain: riferimenti mancanti.", this);
            enabled = false;
            return;
        }
            
        float dt = Time.fixedDeltaTime;
        attackTimeline.Tick(dt);

        bool inAttackCommit = attackTimeline.IsAttacking;

        if (!inAttackCommit) {

            _retargetTimer -= dt;
            if (_retargetTimer <= 0f) {

                _retargetTimer = Mathf.Max(0.05f, config.retargetInterval);
                AcquireOrValidateTarget();
            }
        }

        if (_target == null) {

            _targetNetworkObjectId.Value = 0;
            motor.Stop();
            SetState(BrainState.Idle);
            SetTelegraphClientRpc(false, Vector2.zero, Vector2.zero);
            return;
        }

        Vector2 myPos  = ToXZ(motor.Position);
        Vector2 tgtPos = ToXZ(_target.position);

        // Aggiorna facing verso il target (sempre, anche durante l'attacco)
        // Nota: tgtPos è Vector2 (XZ), riconvertiamo in 3D mantenendo la Y del motor.
        FaceTarget(new Vector3(tgtPos.x, motor.Position.y, tgtPos.y));

        // Aggiorna stima velocit� sempre (serve solo se enablePredictiveAim � ON)
        if (enablePredictiveAim)
            UpdateTargetVelocityEstimate(tgtPos, dt);
        else
            _targetPosLast = tgtPos;

        float dist = Vector2.Distance(myPos, tgtPos);
        float loseDist = config.aggroRadius * config.loseTargetRadiusMultiplier;

        if (!inAttackCommit && dist > loseDist) {

            LoseTarget();
            return;
        }

        if (inAttackCommit) {
            motor.Stop();
            SyncStateFromTimeline();
            // Il telegraph punta al punto CONGELATO, non si aggiorna
            return;
        }

        // --- Gestion range ---
        float minR = Mathf.Max(0f, config.minRange);
        float maxR = Mathf.Max(minR, config.maxRange);
        float tol = Mathf.Max(0f, config.rangeTolerance);

        if (dist > (maxR + tol)) {

            // Chase verso target
            motor.MoveTowards(tgtPos, config.moveSpeed, dt);
            SetState(BrainState.Chase);
            return;
        }

        if (dist < (minR - tol)) {

            // Kite: muovi via dal target
            Vector2 away = (myPos - tgtPos);
            if (away.sqrMagnitude < 0.0001f) {
                motor.Stop();
            }
            else {

                away.Normalize();
                float retreatSpeed = config.moveSpeed * Mathf.Max(0.05f, config.retreatSpeedMultiplier);
                motor.MoveTowards(myPos + away * 10f, retreatSpeed, dt);
            }
            SetState(BrainState.Kite);
            return;
        }

        // --- Nel range giusto: check LoS (sul centro-massa del player) ---
        Vector2 origin = myPos;
        bool hasLos = HasLineOfSight(origin, tgtPos);

        if (!hasLos) {

            TickReposition(myPos, tgtPos, dt);
            return;
        }

        // LoS libero -> attacca
        _repositionGoalSet = false;
        motor.Stop();

        if (attackTimeline.CanStart) {

            if (_weapon == null)
                _weapon = GetComponentInChildren<IRangedWeapon>(true);

            if (_weapon == null || _weapon.CanFire) {

                // Congelo il target qui (centro-massa del player)
                _lockedAimTarget = ComputeAimTarget(origin, tgtPos);
                _aimLocked = true;

                bool started = attackTimeline.TryStart(config.windup, config.active, config.recover);
                if (started) {

                    ShowTelegraph(origin, _lockedAimTarget);
                    SetState(BrainState.Windup);
                    return;
                }
            }
        }
        SetState(BrainState.Idle);
    }

    // --- Calcolo punto di mira ---
    private Vector2 ComputeAimTarget(Vector2 origin, Vector2 currentTargetPos) {

        if (!enablePredictiveAim || projectileSpeed <= 0f)
            return currentTargetPos;

        return PredictedAimPosition(origin, currentTargetPos);
    }

    // --- Predictive Aim ---
    private void UpdateTargetVelocityEstimate(Vector2 currentPos, float dt) {

        if (dt <= 0f) return;
        Vector2 rawVel = (currentPos - _targetPosLast) / dt;
        _targetVelocityEst = Vector2.Lerp(rawVel, _targetVelocityEst, velocitySmoothing);
        _targetPosLast = currentPos;
    }

    private Vector2 PredictedAimPosition(Vector2 origin, Vector2 currentTargetPos) {

        if (!enablePredictiveAim || projectileSpeed <= 0f)
            return currentTargetPos;

        // Intercept equation: due iterazioni per una stima migliore
        float dist = Vector2.Distance(origin, currentTargetPos);
        float timeGuess = dist / projectileSpeed;
        Vector2 pred = currentTargetPos + _targetVelocityEst * timeGuess;

        dist = Vector2.Distance(origin, pred);
        timeGuess = dist / projectileSpeed;
        pred = currentTargetPos + _targetVelocityEst * timeGuess;

        return pred;
    }

    // --- LoS ---
    private bool HasLineOfSight(Vector2 origin, Vector2 targetPos) {

        if (obstacleMask.value == 0) return true;

        Vector2 dir = targetPos - origin;
        float dist = dir.magnitude;
        if (dist < 0.01f) return true;

        // Converte XY (Vector2 brain) → XZ (world 3D)
        Vector3 origin3 = new Vector3(origin.x, transform.position.y, origin.y);
        Vector3 dir3    = new Vector3(dir.x, 0f, dir.y).normalized;
        return !Physics.Raycast(origin3, dir3, dist, obstacleMask);
    }

    private void TickReposition(Vector2 myPos, Vector2 tgtPos, float dt) {

        SetState(BrainState.Reposition);

        bool goalStillValid = _repositionGoalSet
            && HasLineOfSight(_repositionGoal, tgtPos)
            && Vector2.Distance(myPos, _repositionGoal) > 0.3f;

        if (!goalStillValid)
            _repositionGoalSet = TryFindRepositionGoal(myPos, tgtPos, out _repositionGoal);

        if (_repositionGoalSet)
            motor.MoveTowards(_repositionGoal, repositionSpeed, dt);
        else
            motor.MoveTowards(tgtPos, config.moveSpeed * 0.7f, dt);
    }

    private bool TryFindRepositionGoal(Vector2 myPos, Vector2 tgtPos, out Vector2 goal) {

        Vector2 toTarget = (tgtPos - myPos).normalized;
        Vector2 perp = new Vector2(-toTarget.y, toTarget.x);

        for(int i = 1; i <= repositionMaxSteps; i++) {
            for (int sign = 1; sign >= -1; sign -= 2) {

                Vector2 candidate = myPos + perp * (sign * i * repositionStepSize);
                if (HasLineOfSight(candidate, tgtPos)) {

                    goal = candidate;
                    return true;
                }
            }
        }

        goal = myPos;
        return false;
    }

    

    // --- Telegraph ---
    private void ShowTelegraph(Vector2 origin, Vector2 aimTarget) {

        Vector2 dir = aimTarget - origin;
        float dist = dir.magnitude;
        if (dist < 0.01f) return;

        Vector2 end = origin + dir.normalized * Mathf.Min(dist, telegraphLength);
        SetTelegraphClientRpc(true, origin, end);
    }

    [ClientRpc]
    private void SetTelegraphClientRpc(bool visible, Vector2 start, Vector2 end)
    {
        if (telegraphAnchor == null) return;

        telegraphAnchor.gameObject.SetActive(visible);
        if (!visible) return;

        // Ricostruisce le posizioni 3D dal piano XZ
        float h = transform.position.y + telegraphGroundOffset;
        Vector3 start3D = new Vector3(start.x, h, start.y);
        Vector3 end3D   = new Vector3(end.x,   h, end.y);

        Vector3 dir = end3D - start3D;
        float dist = dir.magnitude;
        if (dist < 0.01f) return;

        // Posiziona l'anchor al punto di partenza (piedi del nemico),
        // ruotalo verso il target e scala Z = distanza (il quad figlio è offset di 0.5 in Z,
        // quindi si estende da 0 a dist nel verso corretto).
        telegraphAnchor.position = start3D;
        telegraphAnchor.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        telegraphAnchor.localScale = new Vector3(telegraphWidth, 1f, dist);
    }

    // --- Evento: finestra di fuoco ---
    private void OnHitWindowOpened() {

        SetState(BrainState.Active);
        SetTelegraphClientRpc(false, Vector2.zero, Vector2.zero);

        if (_target == null || !_aimLocked) return;
        if (_weapon == null) _weapon = GetComponentInChildren<IRangedWeapon>(true);
        if (_weapon == null) return;

        Vector2 origin = ToXZ(motor.Position);

        // Spara verso il punto CONGELATO al momento del Windup (direzione normalizzata, non posizione)
        Vector2 aimDir = (_lockedAimTarget - origin).normalized;
        _weapon.TryFire(origin, aimDir);
        _aimLocked = false;
    }

    private void OnAttackStarted() {
        SetState(BrainState.Windup);
        // Attiva il flag: l'animator entra in Gunplay e ci rimane finché IsShooting è true.
        // Il clip parte da frame 0 ad ogni nuovo attacco (false → true retriggera la transizione).
        if (animator != null) animator.SetBool(AnimIsShooting, true);
    }

    private void OnAttackEnded() {

        _aimLocked = false;
        SetTelegraphClientRpc(false, Vector2.zero, Vector2.zero);

        // Disattiva il flag: l'animator esce da Gunplay e torna a Idle/Run.
        if (animator != null) animator.SetBool(AnimIsShooting, false);

        // Riporta lo State a Chase/Idle così il prossimo OnAttackStarted
        // triggera la transizione IsShooting false→true da uno state "pulito".
        SetState(_target != null ? BrainState.Chase : BrainState.Idle);
    }

    // --- Utility ---
    private void SyncStateFromTimeline() {

        switch (attackTimeline.CurrentPhase) {
            case MeleeAttackTimeline.Phase.Windup: SetState(BrainState.Windup); break;
            case MeleeAttackTimeline.Phase.Active: SetState(BrainState.Active); break;
            case MeleeAttackTimeline.Phase.Recover: SetState(BrainState.Recover); break;
        }
    }

    private void LoseTarget() {

        _target = null;
        _targetNetworkObjectId.Value = 0;
        _repositionGoalSet = false;
        _aimLocked = false;
        _targetVelocityEst = Vector2.zero;
        motor.Stop();
        SetState(BrainState.Idle);
        SetTelegraphClientRpc(false, Vector2.zero, Vector2.zero);
    }

    private void AcquireOrValidateTarget() {

        if (_target == null) {

            var found = sensor.FindClosestTarget(motor.Position, config.aggroRadius, config.playerMask);
            Debug.Log($"[RangedBrain] AcquireTarget found={(found != null ? found.name : "null")}", this);
            if (found == null) {

                _target = null;
                _targetNetworkObjectId.Value = 0;
                return;
            }

            _target = found;
            _targetPosLast = ToXZ(_target.position);
            _targetVelocityEst = Vector2.zero;
            _targetNetworkObjectId.Value = GetTargetNetworkObjectId(_target);
            return;
        }

        _targetNetworkObjectId.Value = GetTargetNetworkObjectId(_target);
    }

    private void SetState(BrainState s) {
        byte b = (byte)s;
        if (_state.Value == b) return;
        _state.Value = b;
    }

    // --- Animazioni ---
    private void OnStateChanged(byte prev, byte next) {
        if (animator == null || _isDead) return;
        BrainState s = (BrainState)next;
        // IsShooting controlla Gunplay; State guida solo Idle/Run tra un attacco e l'altro.
        int animValue = s switch {
            BrainState.Chase      => 1,
            BrainState.Kite       => 1,
            BrainState.Reposition => 1,
            _                     => 0  // Idle (e stati di attacco: IsShooting li copre)
        };
        animator.SetInteger(AnimState, animValue);
    }

    private void OnHealthChanged(int prev, int next) {
        if (next > 0 || prev <= 0) return;

        _isDead = true;

        if (animator != null) {
            animator.SetInteger(AnimState, -1);
            animator.SetBool(AnimIsDead, true);
        }

        if (IsServer) {
            motor.Stop();
            StartCoroutine(DespawnAfterAnimation());
        }
    }

    private System.Collections.IEnumerator DespawnAfterAnimation() {
        yield return new WaitForSeconds(deathLingerSeconds);
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(destroy: true);
    }

    private static ulong GetTargetNetworkObjectId(Transform t) {
        if (t == null) return 0;
        var no = t.GetComponentInParent<NetworkObject>();
        return (no != null && no.IsSpawned) ? no.NetworkObjectId : 0;
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
        if (dir.sqrMagnitude < 0.0025f) return;

        float angleY = Quaternion.LookRotation(dir.normalized).eulerAngles.y;
        if (Mathf.Abs(Mathf.DeltaAngle(angleY, _facingAngleY.Value)) < 0.5f) return;

        _facingAngleY.Value = angleY;
        ApplyFacingAngle(angleY);
    }

    private void ApplyFacingAngle(float angleY)
    {
        if (modelTransform != null)
            modelTransform.localRotation = Quaternion.Euler(0f, angleY, 0f);
    }

    // --- Gizmos ---
#if UNITY_EDITOR
    private void OnDrawGizmosSelected() {
        if (!drawGizmos || config == null) return;

        Vector3 pos = transform.position;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(pos, config.aggroRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(pos, config.aggroRadius * config.loseTargetRadiusMultiplier);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(pos, config.minRange);

        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(pos, config.maxRange);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(pos, config.preferredRange);

        if (_target != null) {
            float h = pos.y;
            Vector2 myPos2 = new Vector2(pos.x, pos.z);
            Gizmos.color = HasLineOfSight(myPos2, ToXZ(_target.position)) ? Color.green : Color.red;
            Gizmos.DrawLine(pos, _target.position);

            if (_repositionGoalSet) {
                Gizmos.color = Color.magenta;
                Vector3 goal3 = new Vector3(_repositionGoal.x, h, _repositionGoal.y);
                Gizmos.DrawWireSphere(goal3, 0.25f);
                Gizmos.DrawLine(pos, goal3);
            }

            // Mostra il punto di mira congelato se presente
            if (_aimLocked) {
                Gizmos.color = Color.red;
                Vector3 aim3 = new Vector3(_lockedAimTarget.x, h, _lockedAimTarget.y);
                Gizmos.DrawWireSphere(aim3, 0.2f);
                Gizmos.DrawLine(pos, aim3);
            }
        }
    }
#endif

    private static Vector2 ToXZ(Vector3 v) => new Vector2(v.x, v.z);
}
