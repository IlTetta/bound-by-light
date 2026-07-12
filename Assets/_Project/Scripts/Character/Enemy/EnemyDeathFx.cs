using FMODUnity;
using UnityEngine;

/// <summary>
/// Reazione audiovisiva alla morte di un nemico (SFX + VFX).
/// Si sottoscrive a <see cref="HealthNetwork.OnDeathClient"/> (fired su TUTTI i client)
/// e riproduce localmente suono ed effetto particellare.
///
/// Sostituisce i campi deathSfxEmitter/deathVfxPrefab che prima vivevano dentro
/// HealthNetwork: erano reazioni specifiche dei nemici e non appartenevano al core.
///
/// Setup prefab: aggiungi questo componente accanto a HealthNetwork e assegna
/// l'emitter FMOD e il prefab VFX (istanziato standalone, così sopravvive al despawn).
/// </summary>
[RequireComponent(typeof(HealthNetwork))]
public sealed class EnemyDeathFx : MonoBehaviour
{
    [Tooltip("Emitter FMOD per il suono di morte.")]
    [SerializeField] private StudioEventEmitter deathSfxEmitter;

    [Tooltip("Effetto particellare alla morte (istanziato localmente su ogni client).")]
    [SerializeField] private GameObject deathVfxPrefab;

    private HealthNetwork _health;

    private void Awake() => _health = GetComponent<HealthNetwork>();

    private void OnEnable()
    {
        if (_health != null) _health.OnDeathClient += PlayFx;
    }

    private void OnDisable()
    {
        if (_health != null) _health.OnDeathClient -= PlayFx;
    }

    private void PlayFx()
    {
        deathSfxEmitter?.Play();

        if (deathVfxPrefab != null)
        {
            var vfx = Instantiate(deathVfxPrefab, transform.position, Quaternion.identity);
            Destroy(vfx, 3f); // fallback se il particle system non si autodistrugge
        }
    }
}
