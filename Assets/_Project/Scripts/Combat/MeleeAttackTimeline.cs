using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-side deterministic melee attack timeline (Windup -> Active -> Recover).
/// No coroutines. Driven by Tick(dt) from a deterministic loop (typically FixedUpdate).
/// </summary>
public sealed class MeleeAttackTimeline : NetworkBehaviour
{
    public enum Phase : byte {
        Ready = 0,
        Windup = 1,
        Active = 2,
        Recover = 3
    }

    public event Action AttackStarted;
    public event Action HitWindowOpened;
    public event Action HitWindowClosed;
    public event Action AttackEnded;

    [Header("Debug (read-only)")]
    [SerializeField] private Phase phase = Phase.Ready;
    [SerializeField] private float phaseTimer;

    private float _windup;
    private float _active;
    private float _recover;

    public Phase CurrentPhase => phase;
    public float PhaseTimer => phaseTimer;

    public bool IsAttacking => phase != Phase.Ready;
    public bool InHitWindow => phase == Phase.Active;
    public bool CanStart => phase == Phase.Ready;

    public override void OnNetworkSpawn() {
        // Timeline is server authoritative. CLients may still read state from a brain NetworkVariable.
        enabled = IsServer;
        if (!IsServer) {
            phase = Phase.Ready;
            phaseTimer = 0f;
        }
    }

    /// <summary>
    /// Attempts to start the attack sequence with the specified windup, active, and recovery durations.
    /// </summary>
    /// <param name="windup">Duration of the windup phase, in seconds.</param>
    /// <param name="active">Duration of the active phase, in seconds.</param>
    /// <param name="recover">Duration of the recovery phase, in seconds.</param>
    /// <returns>true if the attack sequence was started; otherwise, false.</returns>
    public bool TryStart(float windup, float active, float recover) {
        if (!IsServer) return false;
        if (!CanStart) return false;

        _windup = Mathf.Max(0f, windup);
        _active = Mathf.Max(0.01f, active); // evita finestra nulla
        _recover = Mathf.Max(0f, recover);

        phase = Phase.Windup;
        phaseTimer = _windup;
        AttackStarted?.Invoke();
        return true;
    }

    /// <summary>
    /// Cancels the current attack immediately.
    /// </summary>
    public void Cancel() {
        if (!IsServer) return;

        bool wasActive = phase == Phase.Active;
        phase = Phase.Ready;
        phaseTimer = 0f;

        if (wasActive) HitWindowClosed?.Invoke();
        AttackEnded?.Invoke();
    }

    public void Tick(float dt) {
        if (!IsServer) return;
        if (phase == Phase.Ready) return;

        phaseTimer -= dt;
        if (phaseTimer > 0f) return;

        // transition
        switch (phase) {
            case Phase.Windup:
                phase = Phase.Active;
                phaseTimer = _active;
                HitWindowOpened?.Invoke();
                break;

            case Phase.Active:
                phase = Phase.Recover;
                phaseTimer = _recover;
                HitWindowClosed?.Invoke();
                break;

            case Phase.Recover:
                phase = Phase.Ready;
                phaseTimer = 0f;
                AttackEnded?.Invoke();
                break;
        }
    }
}
