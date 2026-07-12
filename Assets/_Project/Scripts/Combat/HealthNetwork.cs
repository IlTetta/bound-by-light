using System;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Autorità di vita CONDIVISA da ogni entità danneggiabile (player e nemici).
///
/// Core role-agnostic: valore vita in rete, pipeline del danno, invincibilità,
/// scudo, hit flash (universale) e despawn opzionale. Le reazioni SPECIFICHE DEL RUOLO
/// non stanno qui, ma in componenti dedicati che si sottoscrivono agli eventi:
///   - <see cref="OnServerDeath"/> (server): faint player (PlayerFaintHandler),
///     reward (EnemyRewardOnDeath), ammo (EnemyAmmoDropper), miniboss, ...
///   - <see cref="OnDeathClient"/> (tutti i client): SFX/VFX di morte (EnemyDeathFx).
///   - <see cref="OnHitClient"/> (tutti i client): reazioni al colpo (PlayerHitReaction → camera shake).
///
/// Il knockback viene instradato via <see cref="IKnockbackReceiver"/> (implementato dai motor),
/// senza più il probing di 4 GetComponent per colpo.
/// </summary>
public class HealthNetwork : NetworkBehaviour
{
    [Tooltip("Vita massima. Sui PLAYER è il valore effettivo.\n" +
             "Sui NEMICI viene SOVRASCRITTO in Awake da EnemyBaseConfig.maxHealth: " +
             "il valore che vedi qui è ignorato.")]
    [SerializeField] private int maxHealth = 100;

    [Tooltip("Sui nemici viene sovrascritto da EnemyBaseConfig.invincibilitySeconds.")]
    [SerializeField] private float invincibilitySeconds = 0.25f;

    [Header("Death")]
    [Tooltip("Se true, il NetworkObject viene despawnato quando la vita arriva a zero.\n" +
             "Usalo su entità che NON gestiscono il despawn nel proprio brain.\n" +
             "Su melee/ranged/miniboss lascialo FALSE: il despawn (con linger di morte) lo fa il brain.\n" +
             "Sui player lascialo FALSE (entrano in faint, non vengono despawnati).")]
    [SerializeField] private bool despawnOnDeath = false;

    [Header("Hit Flash")]
    [Tooltip("DamageFlashHandler sul prefab (reazione universale). Se null viene cercato in automatico.")]
    [SerializeField] private DamageFlashHandler hitFlash;

    public NetworkVariable<int> CurrentHealth = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Server: fired appena prima di despawn/faint. Reward, ammo, faint, ...</summary>
    public event Action OnServerDeath;

    /// <summary>Tutti i client (host incluso): fired alla morte. SFX/VFX di morte.</summary>
    public event Action OnDeathClient;

    /// <summary>Tutti i client (host incluso): fired ad ogni colpo subìto. Reazioni al colpo.</summary>
    public event Action OnHitClient;

    private PlayerShield       _shield;        // null sui nemici, null-safe nell'uso
    private IKnockbackReceiver _knockReceiver; // il motor (player o nemico)

    private double _invincibleUntilServerTime;

    public bool IsDead => CurrentHealth.Value <= 0;
    public int MaxHealth => maxHealth;

    /// <summary>
    /// Applica le stat dal config del nemico. Va chiamata in Awake: OnNetworkSpawn
    /// legge maxHealth per inizializzare CurrentHealth, e gira dopo tutti gli Awake.
    /// </summary>
    public void ConfigureStats(int newMaxHealth, float newInvincibilitySeconds)
    {
        if (IsSpawned)
        {
            Debug.LogWarning($"[HealthNetwork] ConfigureStats chiamata dopo lo spawn su " +
                             $"{name}: CurrentHealth è già stata inizializzata.", this);
        }

        maxHealth            = Mathf.Max(1, newMaxHealth);
        invincibilitySeconds = Mathf.Max(0f, newInvincibilitySeconds);
    }

    public override void OnNetworkSpawn()
    {
        _shield        = GetComponent<PlayerShield>();
        _knockReceiver = GetComponent<IKnockbackReceiver>();
        if (hitFlash == null) hitFlash = GetComponent<DamageFlashHandler>();

        if (IsServer && CurrentHealth.Value <= 0)
            CurrentHealth.Value = maxHealth;
    }

    /// <summary>Imposta la salute a un valore specifico (usato dopo riconnessione).</summary>
    public void RestoreHealth(int value)
    {
        if (!IsServer) return;
        CurrentHealth.Value = Mathf.Clamp(value, 1, maxHealth);
    }

    /// <summary>Aggiunge HP alla salute corrente (usato da medikit e cure).</summary>
    public void HealBy(int amount)
    {
        if (!IsServer) return;
        CurrentHealth.Value = Mathf.Clamp(CurrentHealth.Value + amount, 1, maxHealth);
    }

    // Solo server: applica danno se non è invincibile.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ApplyDamageServerRpc(int amount, Vector2 knockback, ulong attackerId)
    {
        if (!IsServer) return;
        ApplyDamageInternal(amount, knockback, attackerId);
    }

    // Comodo da chiamare direttamente da server (es. nemici).
    public void ApplyDamageServer(int amount, Vector2 knockback, ulong attackerId)
    {
        if (!IsServer) return;
        ApplyDamageInternal(amount, knockback, attackerId);
    }

    private void ApplyDamageInternal(int amount, Vector2 knockback, ulong attackerId)
    {
        if (amount <= 0) return;
        if (IsDead) return;

        double now = NetworkManager.Singleton.ServerTime.Time;
        if (now < _invincibleUntilServerTime) return;

        // Scudo: assorbe parte o tutto il danno prima che arrivi alla salute.
        if (_shield != null)
            amount = _shield.AbsorbDamage(amount);
        if (amount <= 0) return;

        CurrentHealth.Value = Mathf.Max(CurrentHealth.Value - amount, 0);
        _invincibleUntilServerTime = now + invincibilitySeconds;

        ApplyKnockbackClientRpc(knockback);
        OnHitClientRpc();

        if (CurrentHealth.Value == 0)
            HandleDeath();
    }

    private void HandleDeath()
    {
        // Logica server-side (reward, ammo, faint player, despawn miniboss, ...).
        OnServerDeath?.Invoke();

        // Reazioni audiovisive su tutti i client.
        OnDeathClientRpc();

        // Despawn generico opzionale (per entità che non lo gestiscono nel proprio brain).
        if (despawnOnDeath && NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(destroy: true);
    }

    [ClientRpc]
    private void ApplyKnockbackClientRpc(Vector2 impulse)
    {
        // Solo il proprietario applica il knockback al proprio motor.
        // Player: owner = client; Nemico: owner = server (host).
        if (!IsOwner) return;
        _knockReceiver?.ApplyKnockback(impulse);
    }

    [ClientRpc]
    private void OnHitClientRpc()
    {
        hitFlash?.TriggerFlash();  // reazione universale (player e nemici flikkano)
        OnHitClient?.Invoke();     // reazioni role-specific (es. camera shake del player owner)
    }

    [ClientRpc]
    private void OnDeathClientRpc()
    {
        OnDeathClient?.Invoke();
    }
}
