using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Power-up scudo: assorbe fino a 50 danni per stanza e si ricarica al clear di ogni stanza.
/// Si attiva tramite ShieldPickup. Va aggiunto al prefab Player.
///
/// Setup prefab Player:
///   1. Aggiungi questo componente al root del Player.
///   2. Crea un GameObject figlio "ShieldBubble": sfera primitiva leggermente più grande del
///      modello (scala ~2.5), materiale ShieldBubble.mat (additivo azzurro), Collider rimosso.
///   3. Assegna shieldBubbleRenderer con il Renderer di ShieldBubble.
///   4. ShieldBubble parte disattivato — PlayerShield lo gestisce.
/// </summary>
[RequireComponent(typeof(HealthNetwork))]
public class PlayerShield : NetworkBehaviour
{
    public const float MaxShieldHp = 50f;

    [Header("Visual")]
    [Tooltip("Renderer della sfera-bolla azzurra figlia del player. " +
             "Usa un materiale additivo trasparente (es. ShieldBubble.mat).")]
    [SerializeField] private Renderer shieldBubbleRenderer;
    [SerializeField] private Color shieldColor = new Color(0.1f, 0.75f, 1f);
    [SerializeField] private float maxAlpha    = 0.55f;

    // ─── NetworkVariables ─────────────────────────────────────────────────────

    /// <summary>HP dello scudo [0, 50]. 0 = scudo esaurito.</summary>
    public readonly NetworkVariable<float> ShieldHp = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>True se il player ha raccolto il pickup scudo.</summary>
    public readonly NetworkVariable<bool> HasShield = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ─── Privati ──────────────────────────────────────────────────────────────

    private MaterialPropertyBlock _mpb;

    // ─── NGO ─────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        _mpb = new MaterialPropertyBlock();

        if (shieldBubbleRenderer != null)
            shieldBubbleRenderer.gameObject.SetActive(false);

        ShieldHp.OnValueChanged  += (_, _) => UpdateGlow();
        HasShield.OnValueChanged += (_, _) => UpdateGlow();

        // Solo il server deve ricaricare lo scudo al clear della stanza.
        // Iscriversi su tutti i client fa girare FindObjectsByType inutilmente
        // e mantiene subscription che non fanno nulla (HandleRoomCleared ha il guard IsServer).
        if (IsServer)
        {
            foreach (var room in FindObjectsByType<Room>(FindObjectsSortMode.None))
                room.OnRoomCleared += HandleRoomCleared;
        }

        UpdateGlow();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            foreach (var room in FindObjectsByType<Room>(FindObjectsSortMode.None))
                room.OnRoomCleared -= HandleRoomCleared;
        }
    }

    // ─── API server ───────────────────────────────────────────────────────────

    /// <summary>Attiva lo scudo (chiamato da ShieldPickup sul server).</summary>
    public void Activate()
    {
        if (!IsServer) return;
        HasShield.Value = true;
        ShieldHp.Value  = MaxShieldHp;
    }

    /// <summary>
    /// Assorbe il danno in arrivo. Restituisce il danno residuo dopo l'assorbimento.
    /// Chiamato da HealthNetwork.ApplyDamageInternal sul server.
    /// </summary>
    public int AbsorbDamage(int amount)
    {
        if (!IsServer)          return amount;
        if (!HasShield.Value)   return amount;
        if (ShieldHp.Value <= 0) return amount;

        float absorbed  = Mathf.Min(amount, ShieldHp.Value);
        ShieldHp.Value -= absorbed;
        return Mathf.Max(0, amount - (int)absorbed);
    }

    // ─── Privati ──────────────────────────────────────────────────────────────

    private void HandleRoomCleared(Room _)
    {
        if (!IsServer || !HasShield.Value) return;
        ShieldHp.Value = MaxShieldHp;
    }

    private void UpdateGlow()
    {
        if (shieldBubbleRenderer == null || _mpb == null) return;

        bool active = HasShield.Value && ShieldHp.Value > 0;
        shieldBubbleRenderer.gameObject.SetActive(active);

        if (!active) return;

        // Per shader additivo: intensità glow = scala RGB × t
        float t = ShieldHp.Value / MaxShieldHp;
        Color color = shieldColor * (t * maxAlpha);
        color.a = t * maxAlpha;

        shieldBubbleRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor("_BaseColor", color);
        shieldBubbleRenderer.SetPropertyBlock(_mpb);
    }
}
