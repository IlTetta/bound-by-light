using UnityEngine;

/// <summary>
/// Brain del nemico melee (chaser): insegue il target e attacca a corto raggio
/// tramite <see cref="MeleeAttackTimeline"/> (Windup → Active → Recover).
///
/// Tutta l'impalcatura comune (config, lifecycle NGO, morte, facing, reward,
/// TakeDamage, stato replicato) vive in <see cref="EnemyBrainBase{TConfig}"/>.
/// Qui resta solo la macchina a stati specifica del melee.
/// </summary>
public sealed class EnemyMeleeBrain : EnemyBrainBase<EnemyChaserConfig>
{
    private enum BrainState : byte
    {
        Idle    = 0,
        Chase   = 1,
        Windup  = 2,
        Active  = 3,
        Recover = 4
    }

    [Header("Melee Modules")]
    [SerializeField] private MeleeAttackTimeline attackTimeline;
    [SerializeField] private DamageOnTouchNetwork3D hitbox;

    private Transform _target;
    private float _retargetTimer;

    private BrainState State => (BrainState)StateByte;
    private void SetState(BrainState s) => SetState((byte)s);

    // ─── Setup ──────────────────────────────────────────────────────────────────

    protected override void OnBrainAwake()
    {
        // Sensor/Timeline possono stare sullo stesso GO; l'hitbox spesso in un child.
        if (attackTimeline == null) attackTimeline = GetComponent<MeleeAttackTimeline>();
        if (hitbox == null) hitbox = GetComponentInChildren<DamageOnTouchNetwork3D>(true);
    }

    protected override void OnServerSpawn()
    {
        ApplyConfigToHitbox();
        WireAttackToHitbox();
    }

    protected override int MapStateToAnimInt(byte state) => (BrainState)state switch
    {
        BrainState.Chase   => 1,
        BrainState.Windup  => 2,
        BrainState.Active  => 2,
        BrainState.Recover => 2,
        _                  => 0 // Idle
    };

    private void ApplyConfigToHitbox()
    {
        if (config == null || hitbox == null) return;

        hitbox.Damage         = config.damage;
        hitbox.KnockbackForce = config.knockbackForce;
        hitbox.TargetMask     = config.damageTargetMask;
        hitbox.SetHitboxEnabledServer(false);
    }

    private void WireAttackToHitbox()
    {
        if (attackTimeline == null || hitbox == null) return;

        // Hitbox: attiva solo durante la fase Active.
        attackTimeline.HitWindowOpened += () => hitbox.SetHitboxEnabledServer(true);
        attackTimeline.HitWindowClosed += () => hitbox.SetHitboxEnabledServer(false);
        attackTimeline.AttackEnded     += () => hitbox.SetHitboxEnabledServer(false);
    }

    // ─── Macchina a stati (server) ──────────────────────────────────────────────

    protected override void OnServerTick(float dt)
    {
        if (sensor == null) return;

        if (attackTimeline != null)
            attackTimeline.Tick(dt);

        // Aggiorna lo stato in base alla fase dell'attacco.
        SyncStateWithAttackPhase();

        // Durante Windup/Active/Recover non retargettare (commit dell'attacco).
        bool inAttackCommit = State == BrainState.Windup
                           || State == BrainState.Active
                           || State == BrainState.Recover;
        if (!inAttackCommit)
            Retarget(dt);

        // Acquire target.
        if (_target == null)
        {
            _target = sensor.FindClosestTarget(motor.Position, config.aggroRadius, config.playerMask);
            if (_target == null)
            {
                SetReplicatedTargetId(0);
                motor.Stop();
                SetState(BrainState.Idle);
                return;
            }
        }

        float dist = XZDistance(motor.Position, _target.position);
        float loseDist = config.aggroRadius * config.loseTargetRadiusMultiplier;

        // Lose target: fuori commit, se troppo lontano o se il target è a terra/morto.
        if (!inAttackCommit && (dist > loseDist || IsTargetIncapacitated(_target)))
        {
            _target = null;
            SetReplicatedTargetId(0);
            motor.Stop();
            SetState(BrainState.Idle);
            return;
        }

        SetReplicatedTargetId(GetTargetNetworkObjectId(_target));
        FaceTarget(_target.position);

        switch (State)
        {
            case BrainState.Idle:
                SetState(BrainState.Chase);
                goto case BrainState.Chase;

            case BrainState.Chase:
                HandleChase(dist, dt);
                break;

            case BrainState.Windup:
            case BrainState.Active:
            case BrainState.Recover:
                // In attacco: fermo. Alla fine SyncStateWithAttackPhase torna a Chase/Idle.
                motor.Stop();
                break;
        }
    }

    private void HandleChase(float dist, float dt)
    {
        if (_target == null)
        {
            motor.Stop();
            SetState(BrainState.Idle);
            return;
        }

        // 1. In range e attacco pronto: entra in Windup.
        if (attackTimeline != null && attackTimeline.CanStart && dist <= config.attackRange)
        {
            motor.Stop();
            if (attackTimeline.TryStart(config.windup, config.active, config.cooldown))
                SetState(BrainState.Windup);
            return;
        }

        // 2. Stop distance.
        if (dist <= config.stopDistance)
        {
            motor.Stop();
            return;
        }

        // 3. Chase.
        motor.MoveTowards(_target.position, config.moveSpeed, dt);
    }

    private void Retarget(float dt)
    {
        _retargetTimer -= dt;
        if (_retargetTimer > 0f) return;

        _retargetTimer = Mathf.Max(0.02f, config.retargetInterval);

        var candidate = sensor.FindClosestTarget(motor.Position, config.aggroRadius, config.playerMask);
        if (candidate == null) return;

        if (_target == null) { _target = candidate; return; }
        if (candidate == _target) return;

        float curDist  = XZDistance(motor.Position, _target.position);
        float candDist = XZDistance(motor.Position, candidate.position);

        if (candDist + config.switchTargetDistanceBias < curDist)
            _target = candidate;
    }

    private void SyncStateWithAttackPhase()
    {
        if (attackTimeline == null)
        {
            if (State != BrainState.Idle && State != BrainState.Chase)
                SetState(BrainState.Chase);
            return;
        }

        switch (attackTimeline.CurrentPhase)
        {
            case MeleeAttackTimeline.Phase.Windup:
                SetState(BrainState.Windup);
                break;

            case MeleeAttackTimeline.Phase.Active:
                SetState(BrainState.Active);
                break;

            case MeleeAttackTimeline.Phase.Recover:
                SetState(BrainState.Recover);
                break;

            case MeleeAttackTimeline.Phase.Ready:
                SetState(_target != null ? BrainState.Chase : BrainState.Idle);
                break;
        }
    }

    // ─── Gizmos ─────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || config == null) return;

        Vector3 pos = transform.position;
        Gizmos.DrawWireSphere(pos, config.aggroRadius);
        Gizmos.DrawWireSphere(pos, config.aggroRadius * config.loseTargetRadiusMultiplier);
        Gizmos.DrawWireSphere(pos, config.attackRange);

        if (_target != null)
            Gizmos.DrawLine(pos, _target.position);
    }
#endif
}
