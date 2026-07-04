using System;
using FMODUnity;
using UnityEngine;
using Unity.Netcode;

public class HealthNetwork : NetworkBehaviour
{
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private float invincibilitySeconds = 0.25f;

    [Header("Death")]
    [Tooltip("Se true, il NetworkObject viene despawnato (distrutto su tutti i client) " +
            "quando la vita arriva a zero. Attivare sui nemici, NON sui player.")]
    [SerializeField] private bool despawnOnDeath = false;

    [Header("Camera Shake")]
    [Tooltip("Abilita lo shake della camera quando questo GameObject subisce danno. " +
             "Attivare solo sui prefab Player, NON sui nemici.")]
    [SerializeField] private bool shakeCameraOnHit = false;

    [Header("SFX")]
    [Tooltip("Emitter FMOD per il suono di morte (assegna solo sui prefab nemici/miniboss, NON sui player).")]
    [SerializeField] private StudioEventEmitter deathSfxEmitter;

    [Header("VFX")]
    [Tooltip("Effetto particellare alla morte (istanziato localmente su ogni client).")]
    [SerializeField] private GameObject deathVfxPrefab;

    [Header("Hit Flash")]
    [Tooltip("DamageFlashHandler sul prefab. Se null viene cercato in automatico.")]
    // AGGIORNATO: Cambiato da HitFlashHandler a DamageFlashHandler
    [SerializeField] private DamageFlashHandler hitFlash;

    public NetworkVariable<int> CurrentHealth = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
        );

    /// <summary>
    /// Fired sul server appena prima del despawn/faint.
    /// Usato da EnemyRewardOnDeath per assegnare currency ed energia.
    /// </summary>
    public event Action OnServerDeath;

    private PlayerFaintHandler _faintHandler;
    private PlayerShield       _shield; // cachato in OnNetworkSpawn — evita GetComponent ad ogni hit

    private double _invincibleUntilServerTime;

    public bool IsDead => CurrentHealth.Value <= 0;
    //TODO: dopo il faint la vita rimane a 0 finch� non viene rianimati
    // quindi TetherComabat e i nemici non continueranno ad applicare danno su un player gi� fianted
    // grazie al check if(IsDead) return; in ApplyDamageInternal.
    
    public int MaxHealth => maxHealth;

    public override void OnNetworkSpawn() {
        _faintHandler = GetComponent<PlayerFaintHandler>();
        _shield       = GetComponent<PlayerShield>(); // null sui nemici, null-safe nell'uso

        if (hitFlash == null) hitFlash = GetComponent<DamageFlashHandler>();

        if (IsServer) {
            if (CurrentHealth.Value <= 0)
                CurrentHealth.Value = maxHealth;
        }
    }

    /// <summary>Imposta la salute a un valore specifico (usato dopo reconnessione).</summary>
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

    // Solo server: applica danno se non è invincibile
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ApplyDamageServerRpc(int amount, Vector2 knockback, ulong attackerId) {
        if (!IsServer) return;
        ApplyDamageInternal(amount, knockback, attackerId);
    }

    // comodo da chiamare direttamente da server (es. nemici)
    public void ApplyDamageServer(int amount, Vector2 knockback, ulong attackerId) {
        if (!IsServer) return;
        ApplyDamageInternal(amount, knockback, attackerId);
    }

    private void ApplyDamageInternal(int amount, Vector2 knockback, ulong attackerId) {
        if (amount <= 0) return;
        if (IsDead) return;

        double now = NetworkManager.Singleton.ServerTime.Time;
        if (now < _invincibleUntilServerTime) return;

        // Scudo: assorbe parte o tutto il danno prima che arrivi alla salute
        if (_shield != null)
            amount = _shield.AbsorbDamage(amount);
        if (amount <= 0) return;

        CurrentHealth.Value = Mathf.Max(CurrentHealth.Value - amount, 0);
        _invincibleUntilServerTime = (float)(now + invincibilitySeconds);

        ApplyKnockbackClientRpc(knockback);
        OnHitClientRpc(shakeCameraOnHit);

        if (CurrentHealth.Value == 0)
            HandleDeath();
    }

    private void HandleDeath()
    {
        // Notifica subscriber server-side (es. EnemyRewardOnDeath) prima del despawn
        OnServerDeath?.Invoke();

        OnDeathClientRpc();

        if (_faintHandler != null)
        {
            // Player: entra in stato faint, nessun despawn
            _faintHandler.TriggerFaint();
        }
        else if (despawnOnDeath)
        {
            NetworkObject.Despawn(destroy: true);
        }
    }

    [ClientRpc]
    private void ApplyKnockbackClientRpc(Vector2 impulse) {
        if (!IsOwner) return; // solo il proprietario applica il knockback al proprio motor

        // Player — prova 3D, poi 2D
        var playerMotor3D = GetComponent<PlayerMovementMotor3D>();
        if (playerMotor3D != null) { playerMotor3D.AddImpact(impulse); return; }

        var playerMotor2D = GetComponent<PlayerMovementMotor2D>();
        if (playerMotor2D != null) { playerMotor2D.AddImpact(impulse); return; }

        // Nemico: il server è owner, AddKnockback viene chiamato lì — prova 3D, poi 2D
        var enemyMotor3D = GetComponent<EnemyMotor3D>();
        if (enemyMotor3D != null) { enemyMotor3D.AddKnockback(impulse); return; }

        var enemyMotor2D = GetComponent<EnemyMotor2D>();
        if (enemyMotor2D != null) enemyMotor2D.AddKnockback(impulse);
    }

    [ClientRpc]
    private void OnHitClientRpc(bool doShake) {
        // TODO: flash sprite/suono/camera shake (solo owner)

        // ── Flash materiale (su tutti i client così nemici e player flikkano) ──
        hitFlash?.TriggerFlash();


        // Solo il client che possiede questo player vede lo shake
        if (!IsOwner) return;
        if (doShake)
            CoopCameraController.Instance?.Shake();
    }

    [ClientRpc]
    private void OnDeathClientRpc()
    {
        // Solo per i nemici (non player)
        if (_faintHandler != null) return;

        // SFX morte nemico
        deathSfxEmitter?.Play();

        // VFX morte nemico
        if (deathVfxPrefab != null)
        {
            var vfx = Instantiate(deathVfxPrefab, transform.position, Quaternion.identity);
            // Auto-distruggi dopo 3 secondi se il particle system non si distrugge da solo
            Destroy(vfx, 3f);
        }
    }
    
}
