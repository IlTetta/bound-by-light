using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gestisce l'inventario medikit del player.
///
/// Setup prefab:
///   1. Aggiungi questo componente al root del PlayerPrefab (accanto ad AmmoDisplay, HealthNetwork, ecc.).
///   2. Imposta healAmount (default 50) e useKey (default H) nell'Inspector.
///
/// Flusso:
///   - MedikitPickup chiama AddMedikit() lato server.
///   - Il player owner preme H → UseMedikitServerRpc() → server decrementa e cura.
///   - MedikitDisplay ascolta OnCountChanged per aggiornare la UI.
/// </summary>
public class PlayerMedikit : NetworkBehaviour
{
    [Header("Config")]
    [Tooltip("HP ripristinati usando un medikit.")]
    [SerializeField] private int healAmount = 50;

    [Tooltip("Tasto per usare il medikit.")]
    [SerializeField] private KeyCode useKey = KeyCode.Q;

    [Tooltip("Numero massimo di medikit trasportabili.")]
    [SerializeField] private int maxMedikits = 9;

    // ─── NetworkVariable ──────────────────────────────────────────────────────

    /// <summary>Numero di medikit in inventario. Leggibile da tutti, scritto solo dal server.</summary>
    public readonly NetworkVariable<int> MedikitCount = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ─── Evento UI ────────────────────────────────────────────────────────────

    /// <summary>Sparato (owner-side) quando il conteggio cambia. MedikitDisplay lo ascolta.</summary>
    public event Action<int> OnCountChanged;

    // ─── Privati ─────────────────────────────────────────────────────────────

    private HealthNetwork _health;

    // ─── NGO ─────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        _health = GetComponent<HealthNetwork>();
        MedikitCount.OnValueChanged += HandleCountChanged;

        // Inizializza la UI con il valore corrente
        if (IsOwner)
            OnCountChanged?.Invoke(MedikitCount.Value);
    }

    public override void OnNetworkDespawn()
    {
        MedikitCount.OnValueChanged -= HandleCountChanged;
    }

    // ─── Input ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!IsOwner) return;
        if (Input.GetKeyDown(useKey))
            UseMedikitServerRpc();
    }

    // ─── RPC ─────────────────────────────────────────────────────────────────

    [ServerRpc]
    private void UseMedikitServerRpc()
    {
        if (MedikitCount.Value <= 0)
        {
            Debug.Log("[PlayerMedikit] Tentativo uso medikit: inventario vuoto.");
            return;
        }

        if (_health == null)
        {
            Debug.LogWarning("[PlayerMedikit] _health è null sul server — medikit non usato.");
            return;
        }

        if (_health.CurrentHealth.Value >= _health.MaxHealth)
        {
            Debug.Log("[PlayerMedikit] HP già al massimo — medikit non consumato.");
            return;
        }

        if (_health.IsDead)
        {
            Debug.Log("[PlayerMedikit] Player è fainted — medikit non consumato (usa la rianimazione).");
            return;
        }

        MedikitCount.Value--;
        _health.HealBy(healAmount);

        Debug.Log($"[PlayerMedikit] Medikit usato. Rimanenti: {MedikitCount.Value}. " +
                  $"HP: {_health.CurrentHealth.Value}/{_health.MaxHealth}");
    }

    // ─── API server ───────────────────────────────────────────────────────────

    /// <summary>
    /// Aggiunge medikit all'inventario. Chiamato da MedikitPickup lato server.
    /// </summary>
    public void AddMedikit(int amount = 1)
    {
        if (!IsServer) return;
        MedikitCount.Value = Mathf.Min(MedikitCount.Value + amount, maxMedikits);
        Debug.Log($"[PlayerMedikit] +{amount} medikit. Totale: {MedikitCount.Value}");
    }

    /// <summary>
    /// Ripristina il conteggio medikit (usato da CheckpointManager al respawn).
    /// </summary>
    public void RestoreMedikits(int count)
    {
        if (!IsServer) return;
        MedikitCount.Value = Mathf.Clamp(count, 0, maxMedikits);
    }

    // ─── Privato ─────────────────────────────────────────────────────────────

    private void HandleCountChanged(int _, int newValue)
    {
        if (IsOwner)
            OnCountChanged?.Invoke(newValue);
    }
}
