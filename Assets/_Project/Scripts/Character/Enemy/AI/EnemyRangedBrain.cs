using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Brain del nemico ranged: gestisce le distanze (chase / kite / reposition),
/// il telegraph, la linea di tiro (LoS) e lo sparo tramite <see cref="IRangedWeapon"/>.
/// L'attacco è scandito da <see cref="MeleeAttackTimeline"/> (Windup → Active → Recover):
/// la mira viene congelata al Windup e lo sparo parte all'apertura della hit window.
///
/// L'impalcatura comune (config, lifecycle NGO, morte, facing, reward, TakeDamage,
/// stato replicato) vive in <see cref="EnemyBrainBase{TConfig}"/>.
/// </summary>
public sealed class EnemyRangedBrain : EnemyBrainBase<EnemyRangedConfig>
{
    private enum BrainState : byte
    {
        Idle       = 0,
        Chase      = 1,
        Kite       = 2,
        Reposition = 3,
        Windup     = 4,
        Active     = 5,
        Recover    = 6
    }

    [Header("Ranged Modules")]
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
    [Tooltip("Layer degli ostacoli. Se lasciato a 0 il check LoS è disabilitato.")]
    [SerializeField] private LayerMask obstacleMask;
    [Tooltip("Passo laterale per ogni tentativo di strafe.")]
    [SerializeField] private float repositionStepSize = 0.8f;
    [Tooltip("Numero massimo di step laterali prima di rinunciare.")]
    [SerializeField] private int repositionMaxSteps = 6;
    [Tooltip("Velocità durante il Reposition.")]
    [SerializeField] private float repositionSpeed = 3.5f;

    [Header("Predictive Aim (opzionale - lasciare OFF in demo)")]
    [Tooltip("OFF = mira congelata al momento del Windup (comportamento di default, consigliato).\n" +
             "ON  = mira predittiva basata sulla velocità stimata del player.\n" +
             "      Richiede calibrazione di Projectile Speed con il sistema proiettili reale.")]
    [SerializeField] private bool enablePredictiveAim = false;
    [Tooltip("Velocità del proiettile (unità/s) usata per stimare il tempo di volo. " +
             "Deve corrispondere alla velocità reale del proiettile.")]
    [SerializeField] private float projectileSpeed = 10f;
    [Tooltip("Smoothing sulla stima della velocità del player. 0 = nessuno, ~0.9 = molto smooth.")]
    [Range(0f, 0.95f)]
    [SerializeField] private float velocitySmoothing = 0.6f;

    // --- Stato interno ---
    private static readonly int AnimIsShooting = Animator.StringToHash("IsShooting");

    private Transform _target;
    private IRangedWeapon _weapon;
    private float _retargetTimer;

    // Aim congelato: impostato al TryStart, usato per tutta la durata del Windup e al fire.
    private Vector2 _lockedAimTarget;
    private bool _aimLocked;

    // Predictive aim (solo se enablePredictiveAim = true).
    private Vector2 _targetPosLast;
    private Vector2 _targetVelocityEst;

    // Reposition.
    private Vector2 _repositionGoal;
    private bool _repositionGoalSet;

    private BrainState State => (BrainState)StateByte;
    private void SetState(BrainState s) => SetState((byte)s);

    // ─── Setup ──────────────────────────────────────────────────────────────────

    protected override void OnBrainAwake()
    {
        if (attackTimeline == null) attackTimeline = GetComponent<MeleeAttackTimeline>();
    }

    protected override void OnServerSpawn()
    {
        if (attackTimeline == null) attackTimeline = GetComponent<MeleeAttackTimeline>();
        _weapon = GetComponentInChildren<IRangedWeapon>(true);

        attackTimeline.AttackStarted   += OnAttackStarted;
        attackTimeline.HitWindowOpened += OnHitWindowOpened;
        attackTimeline.HitWindowClosed += OnHitWindowClosed;
        attackTimeline.AttackEnded     += OnAttackEnded;

        SetTelegraphClientRpc(false, Vector2.zero, Vector2.zero);
    }

    protected override void OnBrainDespawn()
    {
        if (attackTimeline == null) return;
        attackTimeline.AttackStarted   -= OnAttackStarted;
        attackTimeline.HitWindowOpened -= OnHitWindowOpened;
        attackTimeline.HitWindowClosed -= OnHitWindowClosed;
        attackTimeline.AttackEnded     -= OnAttackEnded;
    }

    protected override void OnServerDied()
    {
        SetTelegraphClientRpc(false, Vector2.zero, Vector2.zero);
    }

    protected override int MapStateToAnimInt(byte state) => (BrainState)state switch
    {
        // IsShooting controlla Gunplay; State guida solo Idle/Run tra un attacco e l'altro.
        BrainState.Chase      => 1,
        BrainState.Kite       => 1,
        BrainState.Reposition => 1,
        _                     => 0 // Idle (e stati di attacco: li copre IsShooting)
    };

    // ─── Macchina a stati (server) ──────────────────────────────────────────────

    protected override void OnServerTick(float dt)
    {
        if (sensor == null || attackTimeline == null) return;

        attackTimeline.Tick(dt);

        bool inAttackCommit = attackTimeline.IsAttacking;

        if (!inAttackCommit)
        {
            _retargetTimer -= dt;
            if (_retargetTimer <= 0f)
            {
                _retargetTimer = Mathf.Max(0.05f, config.retargetInterval);
                AcquireOrValidateTarget();
            }
        }

        if (_target == null)
        {
            SetReplicatedTargetId(0);
            motor.Stop();
            SetState(BrainState.Idle);
            SetTelegraphClientRpc(false, Vector2.zero, Vector2.zero);
            return;
        }

        Vector2 myPos  = ToXZ(motor.Position);
        Vector2 tgtPos = ToXZ(_target.position);

        // Facing verso il target (sempre, anche durante l'attacco).
        FaceTarget(new Vector3(tgtPos.x, motor.Position.y, tgtPos.y));

        // Stima velocità (serve solo se enablePredictiveAim è ON).
        if (enablePredictiveAim) UpdateTargetVelocityEstimate(tgtPos, dt);
        else                     _targetPosLast = tgtPos;

        float dist = Vector2.Distance(myPos, tgtPos);
        float loseDist = config.aggroRadius * config.loseTargetRadiusMultiplier;

        if (!inAttackCommit && dist > loseDist)
        {
            LoseTarget();
            return;
        }

        if (inAttackCommit)
        {
            motor.Stop();
            SyncStateFromTimeline();
            // Il telegraph punta al punto CONGELATO, non si aggiorna.
            return;
        }

        // --- Gestione range ---
        float minR = Mathf.Max(0f, config.minRange);
        float maxR = Mathf.Max(minR, config.maxRange);
        float tol  = Mathf.Max(0f, config.rangeTolerance);

        if (dist > (maxR + tol))
        {
            motor.MoveTowards(tgtPos, config.moveSpeed, dt);
            SetState(BrainState.Chase);
            return;
        }

        if (dist < (minR - tol))
        {
            // Kite: muovi via dal target.
            Vector2 away = myPos - tgtPos;
            if (away.sqrMagnitude < 0.0001f)
            {
                motor.Stop();
            }
            else
            {
                away.Normalize();
                float retreatSpeed = config.moveSpeed * Mathf.Max(0.05f, config.retreatSpeedMultiplier);
                motor.MoveTowards(myPos + away * 10f, retreatSpeed, dt);
            }
            SetState(BrainState.Kite);
            return;
        }

        // --- Nel range giusto: check LoS ---
        bool hasLos = HasLineOfSight(myPos, tgtPos);

        if (!hasLos)
        {
            TickReposition(myPos, tgtPos, dt);
            return;
        }

        // LoS libero -> attacca.
        _repositionGoalSet = false;
        motor.Stop();

        if (attackTimeline.CanStart)
        {
            if (_weapon == null)
                _weapon = GetComponentInChildren<IRangedWeapon>(true);

            if (_weapon == null || _weapon.CanFire)
            {
                // Congela il target qui (centro-massa del player).
                _lockedAimTarget = ComputeAimTarget(myPos, tgtPos);
                _aimLocked = true;

                if (attackTimeline.TryStart(config.windup, config.active, config.recover))
                {
                    ShowTelegraph(myPos, _lockedAimTarget);
                    SetState(BrainState.Windup);
                    return;
                }
            }
        }
        SetState(BrainState.Idle);
    }

    // ─── Target ─────────────────────────────────────────────────────────────────

    private void AcquireOrValidateTarget()
    {
        // Scarta il target corrente se è a terra/morto: ne cerchiamo uno vivo.
        if (_target != null && IsTargetIncapacitated(_target))
            _target = null;

        if (_target == null)
        {
            var found = sensor.FindClosestTarget(motor.Position, config.aggroRadius, config.playerMask);
            if (found == null)
            {
                SetReplicatedTargetId(0);
                return;
            }

            _target = found;
            _targetPosLast = ToXZ(_target.position);
            _targetVelocityEst = Vector2.zero;
            SetReplicatedTargetId(GetTargetNetworkObjectId(_target));
            return;
        }

        SetReplicatedTargetId(GetTargetNetworkObjectId(_target));
    }

    private void LoseTarget()
    {
        _target = null;
        _repositionGoalSet = false;
        _aimLocked = false;
        _targetVelocityEst = Vector2.zero;
        SetReplicatedTargetId(0);
        motor.Stop();
        SetState(BrainState.Idle);
        SetTelegraphClientRpc(false, Vector2.zero, Vector2.zero);
    }

    // ─── Calcolo punto di mira ──────────────────────────────────────────────────

    private Vector2 ComputeAimTarget(Vector2 origin, Vector2 currentTargetPos)
    {
        if (!enablePredictiveAim || projectileSpeed <= 0f)
            return currentTargetPos;

        return PredictedAimPosition(origin, currentTargetPos);
    }

    private void UpdateTargetVelocityEstimate(Vector2 currentPos, float dt)
    {
        if (dt <= 0f) return;
        Vector2 rawVel = (currentPos - _targetPosLast) / dt;
        _targetVelocityEst = Vector2.Lerp(rawVel, _targetVelocityEst, velocitySmoothing);
        _targetPosLast = currentPos;
    }

    private Vector2 PredictedAimPosition(Vector2 origin, Vector2 currentTargetPos)
    {
        if (!enablePredictiveAim || projectileSpeed <= 0f)
            return currentTargetPos;

        // Intercept equation: due iterazioni per una stima migliore.
        float dist = Vector2.Distance(origin, currentTargetPos);
        float timeGuess = dist / projectileSpeed;
        Vector2 pred = currentTargetPos + _targetVelocityEst * timeGuess;

        dist = Vector2.Distance(origin, pred);
        timeGuess = dist / projectileSpeed;
        pred = currentTargetPos + _targetVelocityEst * timeGuess;

        return pred;
    }

    // ─── LoS / Reposition ───────────────────────────────────────────────────────

    private bool HasLineOfSight(Vector2 origin, Vector2 targetPos)
    {
        if (obstacleMask.value == 0) return true;

        Vector2 dir = targetPos - origin;
        float dist = dir.magnitude;
        if (dist < 0.01f) return true;

        // Converte XZ (Vector2 brain) → world 3D.
        Vector3 origin3 = new(origin.x, transform.position.y, origin.y);
        Vector3 dir3    = new Vector3(dir.x, 0f, dir.y).normalized;
        return !Physics.Raycast(origin3, dir3, dist, obstacleMask);
    }

    private void TickReposition(Vector2 myPos, Vector2 tgtPos, float dt)
    {
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

    private bool TryFindRepositionGoal(Vector2 myPos, Vector2 tgtPos, out Vector2 goal)
    {
        Vector2 toTarget = (tgtPos - myPos).normalized;
        Vector2 perp = new(-toTarget.y, toTarget.x);

        for (int i = 1; i <= repositionMaxSteps; i++)
        {
            for (int sign = 1; sign >= -1; sign -= 2)
            {
                Vector2 candidate = myPos + perp * (sign * i * repositionStepSize);
                if (HasLineOfSight(candidate, tgtPos))
                {
                    goal = candidate;
                    return true;
                }
            }
        }

        goal = myPos;
        return false;
    }

    // ─── Telegraph ──────────────────────────────────────────────────────────────

    private void ShowTelegraph(Vector2 origin, Vector2 aimTarget)
    {
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

        // Ricostruisce le posizioni 3D dal piano XZ.
        float h = transform.position.y + telegraphGroundOffset;
        Vector3 start3D = new(start.x, h, start.y);
        Vector3 end3D   = new(end.x,   h, end.y);

        Vector3 dir = end3D - start3D;
        float dist = dir.magnitude;
        if (dist < 0.01f) return;

        // Anchor al punto di partenza (piedi del nemico), ruotato verso il target,
        // scala Z = distanza (il quad figlio è offset di 0.5 in Z: si estende da 0 a dist).
        telegraphAnchor.position   = start3D;
        telegraphAnchor.rotation   = Quaternion.LookRotation(dir.normalized, Vector3.up);
        telegraphAnchor.localScale = new Vector3(telegraphWidth, 1f, dist);
    }

    // ─── Eventi timeline ────────────────────────────────────────────────────────

    private void OnAttackStarted()
    {
        SetState(BrainState.Windup);
        // Attiva il flag: l'animator entra in Gunplay e ci resta finché IsShooting è true.
        if (animator != null) animator.SetBool(AnimIsShooting, true);
    }

    private void OnHitWindowOpened()
    {
        SetState(BrainState.Active);
        SetTelegraphClientRpc(false, Vector2.zero, Vector2.zero);

        if (_target == null || !_aimLocked) return;
        if (_weapon == null) _weapon = GetComponentInChildren<IRangedWeapon>(true);
        if (_weapon == null) return;

        Vector2 origin = ToXZ(motor.Position);

        // Spara verso il punto CONGELATO al momento del Windup (direzione normalizzata).
        Vector2 aimDir = (_lockedAimTarget - origin).normalized;
        _weapon.TryFire(origin, aimDir);
        _aimLocked = false;
    }

    private void OnHitWindowClosed() => SetState(BrainState.Recover);

    private void OnAttackEnded()
    {
        _aimLocked = false;
        SetTelegraphClientRpc(false, Vector2.zero, Vector2.zero);

        // Disattiva il flag: l'animator esce da Gunplay e torna a Idle/Run.
        if (animator != null) animator.SetBool(AnimIsShooting, false);

        // Riporta lo State a Chase/Idle così il prossimo OnAttackStarted
        // triggera la transizione IsShooting false→true da uno state "pulito".
        SetState(_target != null ? BrainState.Chase : BrainState.Idle);
    }

    private void SyncStateFromTimeline()
    {
        switch (attackTimeline.CurrentPhase)
        {
            case MeleeAttackTimeline.Phase.Windup:  SetState(BrainState.Windup);  break;
            case MeleeAttackTimeline.Phase.Active:  SetState(BrainState.Active);  break;
            case MeleeAttackTimeline.Phase.Recover: SetState(BrainState.Recover); break;
        }
    }

    // ─── Gizmos ─────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
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

        if (_target != null)
        {
            float h = pos.y;
            Vector2 myPos2 = new(pos.x, pos.z);
            Gizmos.color = HasLineOfSight(myPos2, ToXZ(_target.position)) ? Color.green : Color.red;
            Gizmos.DrawLine(pos, _target.position);

            if (_repositionGoalSet)
            {
                Gizmos.color = Color.magenta;
                Vector3 goal3 = new(_repositionGoal.x, h, _repositionGoal.y);
                Gizmos.DrawWireSphere(goal3, 0.25f);
                Gizmos.DrawLine(pos, goal3);
            }

            if (_aimLocked)
            {
                Gizmos.color = Color.red;
                Vector3 aim3 = new(_lockedAimTarget.x, h, _lockedAimTarget.y);
                Gizmos.DrawWireSphere(aim3, 0.2f);
                Gizmos.DrawLine(pos, aim3);
            }
        }
    }
#endif
}
