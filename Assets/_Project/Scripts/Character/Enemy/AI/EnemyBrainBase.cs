using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Classe base di tutti i "brain" nemici (melee, ranged, disruptive, ...).
///
/// Centralizza l'impalcatura comune che prima ogni brain duplicava:
///   - applicazione del config in Awake (le stat devono arrivare ai componenti
///     PRIMA di qualsiasi OnNetworkSpawn: HealthNetwork legge maxHealth allo spawn);
///   - le NetworkVariable condivise (stato replicato, id del target, angolo di facing);
///   - il lifecycle NGO (subscribe/unsubscribe, enabled = IsServer, auto-wiring moduli);
///   - implementazione di IEnemyEntity (TakeDamage + reward dal config);
///   - gestione della morte e del despawn (UNICO percorso: HealthNetwork.despawnOnDeath
///     va lasciato a false sui nemici, ci pensa qui il brain con il linger di morte);
///   - il facing del modello verso un punto, replicato a tutti i client;
///   - helper matematici sul piano XZ.
///
/// Ogni brain concreto implementa solo la propria macchina a stati tramite gli hook:
///   - <see cref="OnServerTick"/>       : il corpo del FixedUpdate lato server;
///   - <see cref="OnServerSpawn"/>      : wiring server-only (timeline, hitbox, telegraph);
///   - <see cref="MapStateToAnimInt"/>  : traduzione dello stato interno nel parametro Animator.
///
/// Il tipo generico TConfig dà a ogni sottoclasse il config già tipizzato (niente cast).
/// La classe concreta è non-generica, quindi la serializzazione Unity e la code-gen NGO
/// funzionano regolarmente; le RPC restano nelle sottoclassi concrete.
///
/// CONVENZIONE: in ogni brain lo stato Idle DEVE valere 0 (lo spawn imposta lo stato a 0).
/// </summary>
public abstract class EnemyBrainBase<TConfig> : NetworkBehaviour, IEnemyEntity
    where TConfig : EnemyBaseConfig
{
    [Header("Config")]
    [SerializeField] protected TConfig config;

    [Header("Modules")]
    [SerializeField] protected EnemyPerceptionSensor3D sensor;
    [SerializeField] protected EnemyMotor3D motor;

    [Header("Visual / Facing")]
    [Tooltip("Root del modello 3D. Usato per girare il personaggio verso il target.")]
    [SerializeField] protected Transform modelTransform;

    [Header("Animation")]
    [SerializeField] protected Animator animator;
    [SerializeField] protected float deathLingerSeconds = 3f;

    [Header("Debug")]
    [SerializeField] protected bool drawGizmos = true;

    // Cache: HealthNetwork serve in TakeDamage, chiamato ogni frame dal tether.
    protected HealthNetwork Health { get; private set; }
    protected bool IsDead { get; private set; }

    protected static readonly int AnimState  = Animator.StringToHash("State");
    protected static readonly int AnimIsDead = Animator.StringToHash("IsDead");

    // ─── NetworkVariables condivise ─────────────────────────────────────────────

    /// <summary>Stato interno replicato (byte dell'enum specifico della sottoclasse).</summary>
    private readonly NetworkVariable<byte> _state = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Id del NetworkObject bersaglio, replicato per debug/animazioni.</summary>
    private readonly NetworkVariable<ulong> _targetNetworkObjectId = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Angolo Y del modello replicato a tutti i client per il facing.</summary>
    private readonly NetworkVariable<float> _facingAngleY = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    protected byte StateByte => _state.Value;
    public ulong TargetNetworkObjectId => _targetNetworkObjectId.Value;

    // ─── IEnemyEntity ───────────────────────────────────────────────────────────
    // Le reward vivono nel config, non sul prefab.
    public virtual bool IsFlying => false;
    public int CurrencyReward => config != null ? config.currencyReward : 0;
    public float EnergyReward => config != null ? config.energyReward : 0f;

    public bool TakeDamage(float amount)
    {
        if (Health == null || Health.IsDead) return false;
        Health.ApplyDamageServer((int)amount, Vector2.zero, 0);
        return Health.IsDead;
    }

    // ─── Lifecycle ──────────────────────────────────────────────────────────────

    protected virtual void Awake()
    {
        // Auto-wiring: evita null su prefab/varianti.
        if (motor == null)  motor  = GetComponent<EnemyMotor3D>();
        if (sensor == null) sensor = GetComponent<EnemyPerceptionSensor3D>();
        Health = GetComponent<HealthNetwork>();

        // Le stat di design vivono nel config. Applicarle QUI (non in OnNetworkSpawn)
        // è essenziale: HealthNetwork.OnNetworkSpawn inizializza CurrentHealth con
        // maxHealth, e tutti gli Awake girano prima di qualsiasi OnNetworkSpawn.
        if (config != null)
            config.ApplyTo(gameObject);
        else
            Debug.LogError($"[{GetType().Name}] Config non assegnato su {name}: " +
                           "il nemico userà i valori di default dei componenti.", this);

        OnBrainAwake();
    }

    public override void OnNetworkSpawn()
    {
        // Animazioni e facing girano su TUTTI i client: subscribe prima del guard IsServer.
        _state.OnValueChanged        += HandleStateChanged;
        _facingAngleY.OnValueChanged += HandleFacingChanged;
        ApplyFacingAngle(_facingAngleY.Value); // applica subito per chi si connette tardi

        if (Health == null) Health = GetComponent<HealthNetwork>();
        if (Health != null)
            Health.CurrentHealth.OnValueChanged += HandleHealthChanged;

        enabled = IsServer; // solo il server esegue la logica dell'AI

        // Navigazione (NavMeshAgent) attiva solo sul server; i client replicano
        // la posizione via NetworkTransform.
        if (motor != null) motor.ConfigureNavigation(IsServer);

        if (!IsServer) return;

        OnServerSpawn();
        SetState(0); // Idle
    }

    public override void OnNetworkDespawn()
    {
        _state.OnValueChanged        -= HandleStateChanged;
        _facingAngleY.OnValueChanged -= HandleFacingChanged;
        if (Health != null)
            Health.CurrentHealth.OnValueChanged -= HandleHealthChanged;

        OnBrainDespawn();
    }

    private void FixedUpdate()
    {
        if (!IsServer || IsDead) return;
        if (config == null || motor == null) return;

        OnServerTick(Time.fixedDeltaTime);
    }

    // ─── Hook per le sottoclassi ────────────────────────────────────────────────

    /// <summary>Wiring aggiuntivo in Awake (opzionale).</summary>
    protected virtual void OnBrainAwake() { }

    /// <summary>Wiring server-only allo spawn (timeline, hitbox, telegraph iniziale, ...).</summary>
    protected abstract void OnServerSpawn();

    /// <summary>Cleanup al despawn (unsubscribe eventi della timeline, ...).</summary>
    protected virtual void OnBrainDespawn() { }

    /// <summary>Cleanup lato server al momento della morte, prima del linger.</summary>
    protected virtual void OnServerDied() { }

    /// <summary>Corpo del FixedUpdate lato server: la macchina a stati del brain.</summary>
    protected abstract void OnServerTick(float dt);

    /// <summary>Traduce lo stato interno (byte dell'enum della sottoclasse) nel parametro
    /// Animator "State". Chiamato quando lo stato replicato cambia.</summary>
    protected abstract int MapStateToAnimInt(byte state);

    // ─── Stato / animazioni ─────────────────────────────────────────────────────

    protected void SetState(byte s)
    {
        if (_state.Value == s) return;
        _state.Value = s;
    }

    private void HandleStateChanged(byte prev, byte next)
    {
        if (animator == null || IsDead) return;
        animator.SetInteger(AnimState, MapStateToAnimInt(next));
    }

    private void HandleHealthChanged(int prev, int next)
    {
        if (next > 0 || prev <= 0) return;

        IsDead = true;

        if (animator != null)
        {
            animator.SetInteger(AnimState, -1); // invalida State: AnyState->Running non fa match
            animator.SetBool(AnimIsDead, true);
        }

        if (!IsServer) return;

        motor?.Stop();
        OnServerDied();
        StartCoroutine(DespawnAfterLinger());
    }

    private IEnumerator DespawnAfterLinger()
    {
        motor?.Stop();
        yield return new WaitForSeconds(deathLingerSeconds);
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(destroy: true);
    }

    // ─── Target replicato ───────────────────────────────────────────────────────

    protected void SetReplicatedTargetId(ulong id)
    {
        if (_targetNetworkObjectId.Value != id)
            _targetNetworkObjectId.Value = id;
    }

    protected static ulong GetTargetNetworkObjectId(Transform t)
    {
        if (t == null) return 0;
        var no = t.GetComponentInParent<NetworkObject>();
        return (no != null && no.IsSpawned) ? no.NetworkObjectId : 0;
    }

    /// <summary>
    /// True se il target è assente, morto o a terra. Un player fainted resta a HP 0
    /// (IsDead == true) finché non viene rianimato, quindi questo copre anche il faint:
    /// il brain deve mollare un compagno a terra e ripuntare un player ancora in piedi.
    /// </summary>
    protected static bool IsTargetIncapacitated(Transform target)
    {
        if (target == null) return true;
        var h = target.GetComponent<HealthNetwork>();
        return h != null && h.IsDead;
    }

    // ─── Facing ─────────────────────────────────────────────────────────────────

    /// <summary>Calcola l'angolo Y verso targetPos nel piano XZ e lo replica ai client.</summary>
    protected void FaceTarget(Vector3 targetPos)
    {
        Vector3 dir = targetPos - motor.Position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0025f) return; // troppo vicino per determinare la direzione

        float angleY = Quaternion.LookRotation(dir.normalized).eulerAngles.y;
        if (Mathf.Abs(Mathf.DeltaAngle(angleY, _facingAngleY.Value)) < 0.5f) return;

        _facingAngleY.Value = angleY;
        ApplyFacingAngle(angleY); // applica subito sul server senza attendere il sync
    }

    private void HandleFacingChanged(float prev, float next) => ApplyFacingAngle(next);

    private void ApplyFacingAngle(float angleY)
    {
        if (modelTransform != null)
            modelTransform.localRotation = Quaternion.Euler(0f, angleY, 0f);
    }

    // ─── Utility XZ ─────────────────────────────────────────────────────────────

    protected static Vector2 ToXZ(Vector3 v) => new(v.x, v.z);

    protected static float XZDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
