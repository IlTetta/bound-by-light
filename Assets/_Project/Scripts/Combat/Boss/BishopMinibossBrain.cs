using FMOD.Studio;
using FMODUnity;
using MyGame.Core;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// AI server-side del miniboss Vescovo (chiesa, prototipo).
/// Versione aggiornata con supporto animazioni via BishopAnimationController.
/// 
/// MODIFICHE RISPETTO ALLA VERSIONE PRECEDENTE:
///   • Aggiunta reference a BishopAnimationController (_anim)
///   • StartAttack() chiama _anim.PlayAttackAnimation(...)
///   • UpdateChase() chiama _anim.SetMoving(true/false)
///   • TransitionTo() chiama _anim.SetMoving(false) quando si esce da Chase
/// </summary>
[RequireComponent(typeof(MeleeAttackTimeline))]
[RequireComponent(typeof(HealthNetwork))]
[RequireComponent(typeof(EnemyMotor3D))]
[RequireComponent(typeof(BishopAnimationController))]   // ← NUOVO
public sealed class BishopMinibossBrain : NetworkBehaviour, IEnemyEntity
{
    // ── Inspector ────────────────────────────────────────────────────────────
    [Header("References")]
    [SerializeField] private MeleeAttackTimeline _meleeTimeline;
    [SerializeField] private NetworkProjectileWeapon _rangedWeapon;
    [SerializeField] private HealthNetwork _health;
    [SerializeField] private BishopAnimationController _anim;
    [Tooltip("Transform del MuzzlePoint (figlio del root). " +
             "Usato per calcolare la direzione 3D verso il player per i tiri inclinati.")]
    [SerializeField] private Transform _muzzlePoint;

    [Tooltip("Hitbox circolare per Heavy Smash (SphereCollider).")]
    [SerializeField] private DamageOnTouchNetwork3D _smashHitbox;

    [Tooltip("Hitbox a ventaglio per Sweep (MeshCollider/BoxCollider).")]
    [SerializeField] private DamageOnTouchNetwork3D _sweepHitbox;

    [Header("Stats")]
    [Tooltip("Tutti i numeri di bilanciamento del boss. Obbligatorio.")]
    [SerializeField] private EnemyBossConfig config;

    [Header("SFX / Music")]
    [Tooltip("OST del boss (loop). Parte al primo aggro, si ferma alla morte.")]
    [SerializeField] private EventReference bossOst;

    // ── Stat di design ────────────────────────────────────────────────────────
    // NON serializzate: riempite da ApplyConfig() in Awake leggendo EnemyBossConfig.
    // I nomi restano invariati, così il resto del brain non cambia.
    private float _moveSpeed  = 2.5f;
    private float _aggroRange = 8f;
    private float _attackRange = 4f;
    private float _meleeProbability = 0.5f;

    private int   _smashDamage    = 20;
    private float _smashKnockback = 8f;
    private float _smashWindup    = 0.7f;
    private float _smashActive    = 0.25f;
    private float _smashRecover   = 0.5f;

    private int   _sweepDamage    = 12;
    private float _sweepKnockback = 5f;
    private float _sweepWindup    = 0.4f;
    private float _sweepActive    = 0.3f;
    private float _sweepRecover   = 0.4f;

    private int   _boltDamage   = 15;
    private float _boltCooldown = 1.5f;

    private int   _tripleDamage   = 10;
    private float _tripleCooldown = 2.2f;
    private float _tripleSpread   = 30f;

    private float _rangedFireDelay   = 0.6f;
    private float _attackCooldownMin = 1.0f;
    private float _attackCooldownMax = 2.0f;

    private int       _currencyReward = 50;
    private float     _energyReward   = 1f;
    private LayerMask _playerLayer;

    // ── Stato interno ─────────────────────────────────────────────────────────
    private enum State { Idle, Chase, Attacking, Cooldown }
    private EventInstance _bossOstInstance;
    private bool _ostStarted;
    private State _state = State.Idle;
    private Attack _currentAttack = Attack.None;
    private float _cooldownTimer = 0f;
    private bool _meleeEventsHooked = false;
    private bool _wasMoving = false;    // ← traccia per evitare RPC ridondanti

    private EnemyMotor3D _motor;

    private enum Attack { None, HeavySmash, Sweep, HolyBolt, TripleShot }

    private readonly List<Transform> _players = new();

    // ── Unity / Netcode ───────────────────────────────────────────────────────
    private void Awake()
    {
        _motor = GetComponent<EnemyMotor3D>();

        if (_meleeTimeline == null) _meleeTimeline = GetComponent<MeleeAttackTimeline>();
        if (_health == null) _health = GetComponent<HealthNetwork>();
        if (_rangedWeapon == null) _rangedWeapon = GetComponent<NetworkProjectileWeapon>();
        if (_anim == null) _anim = GetComponent<BishopAnimationController>(); // ← NUOVO

        // Le stat arrivano dal config. Qui e non in OnNetworkSpawn: HealthNetwork
        // inizializza CurrentHealth con maxHealth nel proprio OnNetworkSpawn, che
        // gira dopo tutti gli Awake.
        if (config != null)
        {
            config.ApplyTo(gameObject);   // HealthNetwork, EnemyMotor3D, EnemyAmmoDropper
            ApplyConfig();                // campi interni del brain
        }
        else
        {
            Debug.LogError($"[BishopMinibossBrain] Config non assegnato su {name}: " +
                           "il boss userà i valori di default del codice, non quelli " +
                           "bilanciati.", this);
        }
    }

    /// <summary>Copia le stat del config nei campi interni. Solo Awake.</summary>
    private void ApplyConfig()
    {
        _moveSpeed        = config.moveSpeed;
        _aggroRange       = config.aggroRadius;
        _attackRange      = config.attackRange;
        _meleeProbability = config.meleeProbability;
        _playerLayer      = config.playerMask;

        _smashDamage    = config.smashDamage;
        _smashKnockback = config.smashKnockback;
        _smashWindup    = config.smashWindup;
        _smashActive    = config.smashActive;
        _smashRecover   = config.smashRecover;

        _sweepDamage    = config.sweepDamage;
        _sweepKnockback = config.sweepKnockback;
        _sweepWindup    = config.sweepWindup;
        _sweepActive    = config.sweepActive;
        _sweepRecover   = config.sweepRecover;

        _boltDamage   = config.boltDamage;
        _boltCooldown = config.boltCooldown;

        _tripleDamage   = config.tripleDamage;
        _tripleCooldown = config.tripleCooldown;
        _tripleSpread   = config.tripleSpread;

        _rangedFireDelay   = config.rangedFireDelay;
        _attackCooldownMin = config.attackCooldownMin;
        _attackCooldownMax = config.attackCooldownMax;

        _currencyReward = config.currencyReward;
        _energyReward   = config.energyReward;
    }

    public override void OnNetworkSpawn()
    {
        // Navigazione (NavMeshAgent) attiva solo sul server; i client replicano
        // la posizione via NetworkTransform.
        if (_motor != null) _motor.ConfigureNavigation(IsServer);

        if (!IsServer) { enabled = false; return; }

        ConfigureHitbox(_smashHitbox, _smashDamage, _smashKnockback);
        ConfigureHitbox(_sweepHitbox, _sweepDamage, _sweepKnockback);

        if (!_meleeEventsHooked)
        {
            _meleeTimeline.HitWindowOpened += OnHitWindowOpened;
            _meleeTimeline.HitWindowClosed += OnHitWindowClosed;
            _meleeTimeline.AttackEnded += OnMeleeAttackEnded;
            _meleeEventsHooked = true;
        }

        _health.OnServerDeath += OnDeath;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;

        if (_meleeEventsHooked)
        {
            _meleeTimeline.HitWindowOpened -= OnHitWindowOpened;
            _meleeTimeline.HitWindowClosed -= OnHitWindowClosed;
            _meleeTimeline.AttackEnded -= OnMeleeAttackEnded;
            _meleeEventsHooked = false;
        }

        _health.OnServerDeath -= OnDeath;
    }

    private void FixedUpdate()
    {
        if (!IsSpawned) return; // oggetto in scena: aspetta che NGO lo spawni prima di fare RPC
        if (!IsServer) return;
        if (_health.IsDead) return;

        float dt = Time.fixedDeltaTime;
        _meleeTimeline.Tick(dt);

        switch (_state)
        {
            case State.Idle:      UpdateIdle();        break;
            case State.Chase:     UpdateChase(dt);     break;
            case State.Cooldown:  UpdateCooldown(dt);  break;
            case State.Attacking:
                // Stop ogni frame: annulla la velocità residua dalla corsa
                // ed evita che la fisica spinga il boss dentro il player.
                _motor.Stop();
                break;
        }
    }

    // ── States ────────────────────────────────────────────────────────────────

    private void UpdateIdle()
    {
        SetMovingAnim(false);

        Transform nearest = FindNearestPlayer();
        if (nearest == null) return;

        float dist = XZDistance(transform.position, nearest.position);
        if (dist <= _aggroRange)
        {
            StartBossOst();
            TransitionTo(State.Chase);
        }
    }

    private void UpdateChase(float dt)
    {
        Transform target = FindNearestPlayer();
        if (target == null) { TransitionTo(State.Idle); return; }

        float dist = XZDistance(transform.position, target.position);

        if (dist > _aggroRange * 1.5f) { TransitionTo(State.Idle); return; }

        // ── ATTACCO ──────────────────────────────────────────────────────────
        // Dentro _attackRange: sceglie casualmente uno dei 4 attacchi.
        // _meleeProbability controlla il bilanciamento melee/ranged.
        if (dist <= _attackRange)
        {
            SetMovingAnim(false);
            _motor.Stop();

            Attack chosen;
            if (Random.value < _meleeProbability)
                chosen = Random.value < 0.5f ? Attack.HeavySmash : Attack.Sweep;
            else
                chosen = Random.value < 0.5f ? Attack.HolyBolt   : Attack.TripleShot;

            StartAttack(chosen, target);
            return;
        }

        // ── INSEGUI ──────────────────────────────────────────────────────────
        SetMovingAnim(true);
        _motor.MoveTowards(target.position, _moveSpeed, dt);
        FaceTarget(target.position);
    }

    private void UpdateCooldown(float dt)
    {
        SetMovingAnim(false);
        _cooldownTimer -= dt;
        if (_cooldownTimer <= 0f)
            TransitionTo(State.Chase);
    }

    // ── Avvio attacco ─────────────────────────────────────────────────────────

    private void StartAttack(Attack attack, Transform target)
    {
        // Ferma subito il motor: annulla la velocità residua dalla corsa
        // così la fisica non spinge il boss nel player durante l'attacco.
        _motor.Stop();

        _currentAttack = attack;
        TransitionTo(State.Attacking);

        // ── ANIMAZIONE ────────────────────────────────────────────────────────
        BishopAttackType animType = attack switch
        {
            Attack.HeavySmash => BishopAttackType.HeavySmash,
            Attack.Sweep => BishopAttackType.Sweep,
            Attack.HolyBolt => BishopAttackType.HolyBolt,
            Attack.TripleShot => BishopAttackType.TripleShot,
            _ => BishopAttackType.None
        };
        if (animType != BishopAttackType.None)
            _anim.PlayAttackAnimation(animType);
        // ─────────────────────────────────────────────────────────────────────

        switch (attack)
        {
            case Attack.HeavySmash:
                // Orienta il boss verso il target prima dello smash
                FaceTarget(target.position);
                _meleeTimeline.TryStart(_smashWindup, _smashActive, _smashRecover);
                break;

            case Attack.Sweep:
                // Orienta il boss: il collider sul BossWeapon seguirà l'animazione
                FaceTarget(target.position);
                _meleeTimeline.TryStart(_sweepWindup, _sweepActive, _sweepRecover);
                break;

            case Attack.HolyBolt:
                // Spara dopo il delay: il proiettile esce all'apice del movimento dell'arma
                StartCoroutine(DelayedRangedFire(target, () => FireHolyBolt(target)));
                break;

            case Attack.TripleShot:
                StartCoroutine(DelayedRangedFire(target, () => FireTripleShot(target)));
                break;
        }
    }

    // ── Melee – eventi timeline ───────────────────────────────────────────────

    private void OnHitWindowOpened()
    {
        switch (_currentAttack)
        {
        case Attack.HeavySmash: _smashHitbox?.SetHitboxEnabledServer(true); break;         
        case Attack.Sweep: _sweepHitbox?.SetHitboxEnabledServer(true); break;
        }
    }

    private void OnHitWindowClosed()
    {
        _smashHitbox?.SetHitboxEnabledServer(false);
        _sweepHitbox?.SetHitboxEnabledServer(false);
    }

    private void OnMeleeAttackEnded()
    {
        _smashHitbox?.SetHitboxEnabledServer(false);
        _sweepHitbox?.SetHitboxEnabledServer(false);
        StartCooldown();
    }

    // ── Ranged ────────────────────────────────────────────────────────────────

    private void FireHolyBolt(Transform target)
    {
        if (_rangedWeapon == null) return;
        _rangedWeapon.ProjectilesPerShot = 1;
        _rangedWeapon.Spread = 0f;
        _rangedWeapon.RandomSpread = false;
        _rangedWeapon.SetDamageOverride(_boltDamage);
        _rangedWeapon.TryFire3D(DirToTarget3D(target));
    }

    private void FireTripleShot(Transform target)
    {
        if (_rangedWeapon == null) return;
        _rangedWeapon.ProjectilesPerShot = 3;
        _rangedWeapon.Spread = _tripleSpread;
        _rangedWeapon.RandomSpread = false;
        _rangedWeapon.SetDamageOverride(_tripleDamage);
        _rangedWeapon.TryFire3D(DirToTarget3D(target));
    }

    private void EndRangedAttack() => StartCooldown();

    /// <summary>
    /// Aspetta _rangedFireDelay secondi, poi esegue l'azione di fuoco e avvia il cooldown.
    /// Il delay allinea lo sparo con l'apice dell'animazione (braccio/arma al massimo alzato).
    /// Se il boss muore durante l'attesa, il coroutine termina senza sparare.
    /// </summary>
    private System.Collections.IEnumerator DelayedRangedFire(Transform target, System.Action fireAction)
    {
        yield return new WaitForSeconds(_rangedFireDelay);

        // Sicurezza: non sparare se il boss è morto nel frattempo
        if (_health == null || _health.IsDead) yield break;

        // Riorient verso il target (potrebbe essersi spostato durante il delay)
        if (target != null)
            FaceTarget(target.position);

        fireAction?.Invoke();
        EndRangedAttack();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void TransitionTo(State next) => _state = next;

    private void StartCooldown()
    {
        _currentAttack = Attack.None;
        _cooldownTimer = Random.Range(_attackCooldownMin, _attackCooldownMax);
        TransitionTo(State.Cooldown);
    }

    private void OnDeath()
    {
        _smashHitbox?.SetHitboxEnabledServer(false);
        _sweepHitbox?.SetHitboxEnabledServer(false);
        _meleeTimeline.Cancel();
        enabled = false;
        _motor?.Stop();
        StopBossOstClientRpc();

        // La coroutine gira su GameManager (persistente) così non viene annullata
        // quando questo NetworkObject viene despawnato.
        GameManager.Instance?.StartGameEndingSequence(NetworkObject, 3.5f);
    }

    private void StartBossOst()
            {
            if (_ostStarted || bossOst.IsNull) return;       
            _ostStarted = true;         
            StartBossOstClientRpc();
           
        }

    [ClientRpc]
    private void StartBossOstClientRpc()
    {
        // Ferma la OST di gioco (c'è un solo StudioEventEmitter in scena)
        var ostEmitter = FindFirstObjectByType<FMODUnity.StudioEventEmitter>();
        ostEmitter?.Stop();
        if (bossOst.IsNull) return;
        _bossOstInstance = RuntimeManager.CreateInstance(bossOst);
        _bossOstInstance.start();
    }
    [ClientRpc]
    private void StopBossOstClientRpc()
    {
        if (_bossOstInstance.isValid())
        {
            _bossOstInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            _bossOstInstance.release();
        }
    }
    /// <summary>Evita di inviare RPC SetMoving ridondanti ogni FixedUpdate.</summary>
    private void SetMovingAnim(bool moving)
    {
        if (_wasMoving == moving) return;
        _wasMoving = moving;
        _anim.SetMoving(moving);
    }

    private Transform FindNearestPlayer()
    {
        _players.Clear();
        if (NetworkManager.Singleton == null) return null;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            var faint = client.PlayerObject.GetComponent<PlayerFaintHandler>();
            if (faint != null && faint.IsFainted) continue;
            _players.Add(client.PlayerObject.transform);
        }

        if (_players.Count == 0) return null;

        Transform nearest = null;
        float minDist = float.MaxValue;
        foreach (var p in _players)
        {
            float d = Vector3.Distance(transform.position, p.position);
            if (d < minDist) { minDist = d; nearest = p; }
        }
        return nearest;
    }

    private void ConfigureHitbox(DamageOnTouchNetwork3D hitbox, int damage, float knockback)
    {
        if (hitbox == null) return;
        hitbox.Damage = damage;
        hitbox.KnockbackForce = knockback;
        hitbox.TargetMask = _playerLayer;
    }
    private Vector2 DirToTargetXZ(Vector3 targetPos)
    {
        Vector3 d = targetPos - transform.position;
        d.y = 0f;
        d.Normalize();
        return new Vector2(d.x, d.z);
    }

    /// <summary>
    /// Direzione 3D dal MuzzlePoint al centro del player (include componente Y).
    /// Consente ai proiettili di inclinarsi verso il basso quando il boss è più alto.
    /// </summary>
    private Vector3 DirToTarget3D(Transform target)
    {
        // Origine: MuzzlePoint se assegnato, altrimenti root del boss
        Vector3 from = _muzzlePoint != null ? _muzzlePoint.position : transform.position;

        // Destinazione: centro del player (mezzo della capsula, ~metà altezza)
        // target.position è ai piedi del player — aggiungiamo metà altezza stimata
        Vector3 to = target.position + Vector3.up * 0.6f;

        Vector3 dir = to - from;
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
    }

    /// <summary>Distanza nel piano XZ (ignora la differenza di altezza Y).</summary>
    private static float XZDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private void FaceTarget(Vector3 targetPos)
    {
        Vector3 dir = targetPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dir);
    }

    // ── IEnemyEntity ──────────────────────────────────────────────────────────
    // Necessario perché Projectile3D usa GetComponentInParent<IEnemyEntity>()
    // per distinguere "proiettile del player" da "proiettile del nemico".
    // Senza questa implementazione _ownerIsEnemy = false e i bolt non colpirebbero i player.

    public bool  IsFlying       => false;
    public int   CurrencyReward => _currencyReward;
    public float EnergyReward   => _energyReward;

    public bool TakeDamage(float amount)
    {
        if (_health == null || _health.IsDead) return false;
        _health.ApplyDamageServer((int)amount, Vector2.zero, 0);
        return true;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, _aggroRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, _attackRange);
        Gizmos.color = Color.cyan;
        // (rangedRange rimosso — ora c'è solo _attackRange)
    }
#endif
}
