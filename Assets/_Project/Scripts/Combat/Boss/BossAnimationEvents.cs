using FMODUnity;
using UnityEngine;

/// <summary>
/// Ricevitore degli Animation Events integrati nelle clip del boss.
/// Deve stare sullo stesso GameObject che ha l'Animator (BossVisual).
///
/// Events gestiti:
///   FootR / FootL  — passo del piede destro/sinistro (SFX footstep)
///   Hit            — momento d'impatto nell'animazione melee (SFX colpo)
///
/// Se non hai ancora gli eventi FMOD, lascia i campi vuoti:
/// i metodi silenziosi eliminano il warning senza crashare.
/// </summary>
public class BossAnimationEvents : MonoBehaviour
{
    [Header("SFX Footstep")]
    [Tooltip("Evento FMOD per il passo del piede (FootR e FootL usano lo stesso).")]
    [SerializeField] private EventReference _footstepSfx;

    [Header("SFX Hit")]
    [Tooltip("Evento FMOD per il momento d'impatto melee (swing dell'arma).")]
    [SerializeField] private EventReference _hitSfx;

    // ── Receivers ─────────────────────────────────────────────────────────────

    /// <summary>Chiamato dalle clip di camminata/attacco sul passo del piede destro.</summary>
    private void FootR() => PlaySfx(_footstepSfx);

    /// <summary>Chiamato dalle clip di camminata/attacco sul passo del piede sinistro.</summary>
    private void FootL() => PlaySfx(_footstepSfx);

    /// <summary>Chiamato dalle clip di attacco melee al momento dell'impatto visivo.</summary>
    private void Hit() => PlaySfx(_hitSfx);

    // ── Helper ────────────────────────────────────────────────────────────────

    private void PlaySfx(EventReference sfx)
    {
        if (sfx.IsNull) return;

        var inst = RuntimeManager.CreateInstance(sfx);
        inst.set3DAttributes(RuntimeUtils.To3DAttributes(transform.position));
        inst.start();
        inst.release();
    }
}
