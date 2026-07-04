using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// AI del nemico Disruptive.
///
/// Comportamento:
///   - Approch: si avvicina al centro del tether (punto medio tra i due player).
///   - Interpose: si posiziona sul punto medio del tether e ci rimane,
///     ostacolando fisicamente la coordinazione dei player.
///   - DashWindup: telegraph visivo (LineRenderer) prima della carica.
///   - Dash: carica lungo la linea del tether con alto knockback,
///     progettata per spingere un player oltre la distanza massima
///     e attivare il pull della corda sul compagno.
///   - Recover: pausa post-carica.
///
/// Nota sul tether:
///   Questo brain usa solo le posizioni dei due player (playerA, playerB).
///   Non dipende dall'implementazione della corda — quando il collega
///   integra il TetherSystem, non sarà necessario modificare questo script.
/// </summary>
public class EnemyDisruptiveBrain : NetworkBehaviour, IEnemyEntity
{
    // Stati
    private enum BrainState : byte
    {
        Idle = 0,
        Approach = 1,
        Interpose = 2,
        DashWindup = 3,
        Dash = 4,
        Recover = 5
    }

    // Inspector
    [Header("Config")]
    [SerializeField] private EnemyDisruptiveConfig config;

    [Header("Modules")]
    [SerializeField] private EnemyPerceptionSensor3D sensor;
    [SerializeField] private EnemyMotor3D motor;

    [Header("Hitbox (child DamageOnTouchNetwork)")]
    [SerializeField] private DamageOnTouchNetwork3D dashHitbox;

    [Header("Telegraph")]
    [SerializeField] private LineRenderer telegraphLine;
    [SerializeField] private Color telegraphColor = new Color(1f, 0.4f, 0f, 0.85f); // arancione
    [SerializeField] private float telegraphLength = 10f;

    [Header("Loot")]
    [SerializeField] private int currencyReward = 5;
    [SerializeField] private float energyReward = 10f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    // Stato interno
    private Transform _playerA;
    private Transform _playerB;
    private float _retargetTimer;

    // Interpose
    private float _interposeTimer;

    // Dash
    private float _dashWindupTimer;
    private float _dashTimer;
    private float _dashRecoverTimer;
    private Vector2 _dashOrigin;
    private Vector2 _dashDirection;
    private Vector2 _dashTarget;

    // NetworkVariables
    private readonly NetworkVariable<byte> _state =
        new NetworkVariable<byte>((byte)BrainState.Idle,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    // IEnemyEntity
    public bool IsFlying => false;
    public bool IsDiruptor => true;
    public int CurrencyReward => currencyReward;
    public float EnergyReward => energyReward;

    // Cache
    private HealthNetwork _health;

    // Lifecycle
    public override void OnNetworkSpawn()
    {
        // Cache salute (necessaria in TakeDamage, chiamato dal tether ogni frame)
        _health = GetComponent<HealthNetwork>();

        enabled = IsServer;
        if (!IsServer) return;

        if (motor == null) motor = GetComponent<EnemyMotor3D>();
        if (sensor == null) sensor = GetComponent<EnemyPerceptionSensor3D>();
        if (dashHitbox == null) dashHitbox = GetComponentInChildren<DamageOnTouchNetwork3D>(true);

        if (dashHitbox != null)
        {
            dashHitbox.Damage = config.dashDamage;
            dashHitbox.KnockbackForce = config.dashKnockbackForce;
            dashHitbox.TargetMask = config.damageTargetMask;
            dashHitbox.SetHitboxEnabledServer(false);
        }

        SetTelegraphClientRpc(false, Vector2.zero, Vector2.zero);
        SetState(BrainState.Idle);
    }

    public override void OnNetworkDespawn()
    {
        _health = null;
        base.OnNetworkDespawn();
    }

    public bool TakeDamage(float amount)
    {
        if (_health == null || _health.IsDead) return false;
        _health.ApplyDamageServer((int)amount, Vector2.zero, 0);
        return _health.IsDead;
    }

    // FixedUpdate
    private void FixedUpdate()
    {
        if (!IsServer) return;
        if (config == null || motor == null) return;

        float dt = Time.fixedDeltaTime;

        // retarget periodico (solo fuori dal Dash)
        BrainState current = (BrainState)_state.Value;
        bool inDashSequence = current == BrainState.DashWindup
                           || current == BrainState.Dash
                           || current == BrainState.Recover;

        if (!inDashSequence)
        {
            _retargetTimer -= dt;
            if (_retargetTimer <= 0f)
            {
                _retargetTimer = Mathf.Max(0.05f, config.retargetInterval);
                AcquirePlayers();
            }
        }
        
        // Senza almeno un player, rimani idle
        if (_playerA == null && _playerB == null)
        {
            motor.Stop();
            SetState(BrainState.Idle);
            SetTelegraphClientRpc(false, Vector2.zero, Vector2.zero);
            return;
        }

        // se c'è un solo player, usa quello come riferimento singolo
        Vector2 posA = _playerA != null ? ToXZ(_playerA.position) : ToXZ(_playerB.position);
        Vector2 posB = _playerB != null ? ToXZ(_playerB.position) : ToXZ(_playerA.position);

        Vector2 tetherMid = (posA + posB) * 0.5f;
        Vector2 tetherDir = (posB - posA).normalized;
        Vector2 myPos = ToXZ(motor.Position);
        float distToMid = Vector2.Distance(myPos, tetherMid);
        float aggroRange = config.aggroRadius;

        // Perde i player se troppo lontani dal centro del tether
        if (!inDashSequence && distToMid > aggroRange * config.loseTargetRadiusMultiplier)
        {
            _playerA = null;
            _playerB = null;
            motor.Stop();
            SetState(BrainState.Idle);
            return;
        }

        // State machine
        switch ((BrainState)_state.Value)
        {
            case BrainState.Idle:
                SetState(BrainState.Approach);
                goto case BrainState.Approach;

            case BrainState.Approach:
                TickApproach(myPos, tetherMid, dt);
                break;

            case BrainState.Interpose:
                TickInterpose(myPos, tetherMid, tetherDir, posA, posB, dt);
                break;

            case BrainState.DashWindup:
                TickDashWindup(dt);
                break;

            case BrainState.Dash:
                TickDash(dt);
                break;

            case BrainState.Recover:
                TickRecover(dt);
                break;
        }
    }

    // ─── Approach ─────────────────────────────────────────────────────────────
    private void TickApproach(Vector2 myPos, Vector2 tetherMid, float dt)
    {
        float dist = Vector2.Distance(myPos, tetherMid);

        if (dist <= config.interposeStopDistance)
        {
            // raggiunto il centro entra in Interpose
            motor.Stop();
            _interposeTimer = config.interposeDuration;
            SetState(BrainState.Interpose);
            return;
        }

        motor.MoveTowards(tetherMid, config.moveSpeed, dt);
    }

    // ─── Interpose ───────────────────────────────────────────────────────────
    /// <summary>
    /// Si mantiene sul punto medio del tether.
    /// Dopo interposeDuration secondi, decide se fare il Dash.
    /// </summary>
    private void TickInterpose(Vector2 myPos, Vector2 tetherMid, Vector2 tetherDir,
                                Vector2 posA, Vector2 posB, float dt)
    {
        // segue il punto medio se i player si muovono
        float distToMid = Vector2.Distance(myPos, tetherMid);
        if (distToMid > config.interposeReachedThreshold)
            motor.MoveTowards(tetherMid, config.moveSpeed, dt);
        else
            motor.Stop();

        _interposeTimer -= dt;
        if (_interposeTimer > 0f) return;

        // timer scaduto: prepara il dash
        // sceglie la direzione: verso il player più lontano dal nemico
        // massimizza il knockback utile per stressare il tether
        float dA = Vector2.Distance(myPos, posA);
        float dB = Vector2.Distance(myPos, posB);

        // direzione del dash: dalla parte del player più vicino verso quello più lontano
        Vector2 dashDir;
        Vector2 dashEndPlayer;

        if (dA <= dB)
        {
            // A è più vicino -> dash verso B
            dashDir = (posB - myPos).normalized;
            dashEndPlayer = posB;
        }
        else
        {
            dashDir = (posA - myPos).normalized;
            dashEndPlayer = posA;
        }

        _dashOrigin = myPos;
        _dashDirection = dashDir;
        _dashTarget = dashEndPlayer + dashDir * config.dashOvershoot;
        _dashWindupTimer = config.dashWindup;

        // mostra il telegraph
        Vector2 telegraphEnd = _dashOrigin + _dashDirection * telegraphLength;
        SetTelegraphClientRpc(true, _dashOrigin, telegraphEnd);

        motor.Stop();
        SetState(BrainState.DashWindup);
    }

    // ─── DashWindup ─────────────────────────────────────────────────────────
    private void TickDashWindup(float dt)
    {
        _dashWindupTimer -= dt;
        if (_dashWindupTimer > 0f) return;

        // windup finito: nasconde telegraph, attiva hitbox e inizia il dash
        SetTelegraphClientRpc(false, Vector2.zero, Vector2.zero);
        
        if (dashHitbox != null)
            dashHitbox.SetHitboxEnabledServer(true);

        _dashTimer = config.dashMaxDuration;
        SetState(BrainState.Dash);
    }

    // ─── Dash ───────────────────────────────────────────────────────────────
    private void TickDash(float dt)
    {
        _dashTimer -= dt;

        Vector2 myPos = motor.Position;
        float distToTarget = Vector2.Distance(myPos, _dashTarget);

        // termina il dash se raggiunge il target o scade il timer
        bool reachedTarget = distToTarget < 0.3f;
        bool timedOut = _dashTimer <= 0f;

        if (reachedTarget || timedOut)
        {
            if (dashHitbox != null)
                dashHitbox.SetHitboxEnabledServer(false);

            _dashRecoverTimer = config.dashRecover;
            motor.Stop();
            SetState(BrainState.Recover);
            return;
        }

        motor.MoveTowards(_dashTarget, config.dashSpeed, dt);
    }

    // ─── Recover ───────────────────────────────────────────────────────────
    private void TickRecover(float dt)
    {
        motor.Stop();
        _dashRecoverTimer -= dt;

        if (_dashRecoverTimer <= 0f)
            SetState(BrainState.Approach);
    }

    // ─── Player Acquisition ─────────────────────────────────────────────────
    // Buffer riutilizzato per evitare allocazioni ogni retarget
    private readonly List<Transform> _playerBuffer = new List<Transform>(2);

    /// <summary>
    /// Trova i due player tramite EnemyManager.
    /// Fallback su ConnectedClientsList se il manager non è disponibile.
    /// </summary>
    private void AcquirePlayers()
    {
        _playerA = null;
        _playerB = null;

        if (EnemyManager.Instance != null)
        {
            // Percorso normale: EnemyManager espone i player già filtrati
            EnemyManager.Instance.GetTwoClosestPlayers(motor.Position, _playerBuffer);

            if (_playerBuffer.Count > 0) _playerA = _playerBuffer[0];
            if (_playerBuffer.Count > 1) _playerB = _playerBuffer[1];
        }
        else
        {
            // Fallback: accede direttamente a ConnectedClientsList
            // (utile in testing standalone senza EnemyManager in scena)
            Debug.LogWarning("[DisruptiveBrain] EnemyManager non trovato, uso fallback.");

            if (NetworkManager.Singleton == null) return;

            float bestDistA = float.MaxValue;
            float bestDistB = float.MaxValue;
            Vector2 myPos = ToXZ(motor.Position);

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                Transform t = client.PlayerObject.transform;
                if ((config.playerMask.value & (1 << t.gameObject.layer)) == 0) continue;

                float d = Vector2.Distance(myPos, ToXZ(t.position));
                if (d < bestDistA)
                {
                    bestDistB = bestDistA; _playerB = _playerA;
                    bestDistA = d; _playerA = t;
                }
                else if (d < bestDistB)
                {
                    bestDistB = d; _playerB = t;
                }
            }
        }
    }


    // ─── Telegraph ────────────────────────────────────────────────────────────
    [ClientRpc]
    private void SetTelegraphClientRpc(bool visible, Vector2 start, Vector2 end)
    {
        if (telegraphLine == null) return;

        telegraphLine.enabled = visible;
        if (!visible) return;

        telegraphLine.startColor = telegraphColor;
        telegraphLine.endColor = new Color(telegraphColor.r, telegraphColor.g, telegraphColor.b, 0f);
        // start/end sono coordinate XZ (Vector2.x=worldX, Vector2.y=worldZ)
        float h = transform.position.y;
        telegraphLine.SetPosition(0, new Vector3(start.x, h, start.y));
        telegraphLine.SetPosition(1, new Vector3(end.x,   h, end.y));
    }

    // ─── Utility ──────────────────────────────────────────────────────────────
    private void SetState(BrainState s)
    {
        byte b = (byte)s;
        if (_state.Value == b) return;
        _state.Value = b;
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || config == null) return;

        Vector3 pos = transform.position;

        // Aggro radius
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(pos, config.aggroRadius);

        // Lose radius
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(pos, config.aggroRadius * config.loseTargetRadiusMultiplier);

        // Interpose stop distance
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(pos, config.interposeStopDistance);

        if (_playerA != null && _playerB != null)
        {
            // Linea del tether
            Gizmos.color = new Color(0.5f, 0.5f, 1f, 0.5f);
            Gizmos.DrawLine(_playerA.position, _playerB.position);

            // Centro del tether
            Vector3 mid = (_playerA.position + _playerB.position) * 0.5f;
            mid.y = pos.y;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(mid, 0.25f);

            // Linea nemico → centro
            Gizmos.color = Color.white;
            Gizmos.DrawLine(pos, mid);
        }

        // Dash target
        if ((BrainState)_state.Value == BrainState.DashWindup ||
            (BrainState)_state.Value == BrainState.Dash)
        {
            Gizmos.color = Color.red;
            Vector3 dashT3 = new Vector3(_dashTarget.x, pos.y, _dashTarget.y);
            Gizmos.DrawWireSphere(dashT3, 0.3f);
            Gizmos.DrawLine(pos, dashT3);
        }
    }
#endif

    private static Vector2 ToXZ(Vector3 v) => new Vector2(v.x, v.z);
}
