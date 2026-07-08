using MyGame.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Pozzo interagibile tramite il tether.
///
/// Funzionamento:
///   I due player devono posizionarsi su lati opposti del pozzo con il tether teso
///   in modo che la corda passi attraverso il trigger del pozzo.
///   Dopo <see cref="activationTime"/> secondi di contatto continuo, il pozzo si attiva:
///   rivela la scala e apre la porta verso la stanza sotterranea.
///
/// Setup scena:
///   1. Crea un GameObject "Well" con un Collider (trigger) che rappresenta la zona
///      di rilevamento del tether (sfera o capsule attorno al modello del pozzo).
///   2. Aggiungi questo componente + NetworkObject.
///   3. Assegna staircaseObject (inizialmente disattivato) e undergroundDoor (opzionale).
///   4. Assegna il layer del pozzo a <see cref="wellLayer"/> per filtrare l'OverlapCapsule.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class WellInteractable : NetworkBehaviour
{
    [Header("Rilevamento tether")]
    [Tooltip("Raggio della capsula del tether usata per il rilevamento. " +
             "Deve essere > 0 ma abbastanza piccolo da richiedere precisione.")]
    [SerializeField] private float tetherCapsuleRadius = 0.6f;

    [Tooltip("Layer mask che include solo il layer del pozzo. " +
             "Crea un layer 'Well' e assegnalo al Collider di questo GameObject.")]
    [SerializeField] private LayerMask wellLayer;

    [Header("Attivazione")]
    [Tooltip("Secondi di contatto continuo necessari per attivare il pozzo.")]
    [SerializeField] private float activationTime = 2f;

    [Header("Risultato attivazione")]
    [Tooltip("GameObject delle scale/accesso (inizialmente disattivato).")]
    [SerializeField] private GameObject staircaseObject;

    [Tooltip("Ladder (3) nella stanza sotterranea (inizialmente disattivata).")]
    [SerializeField] private GameObject ladderObject;

    [Tooltip("Porta verso la stanza sotterranea (opzionale).")]
    [SerializeField] private RoomDoor undergroundDoor;

    [Header("Feedback visivo")]
    [Tooltip("Anello a terra attorno al pozzo. Mostra l'area di interazione e si " +
             "riempie mentre il tether è a contatto. Nascosto dopo l'attivazione.")]
    [SerializeField] private RadialProgressRing progressRing;

    [Header("VFX")]
    [Tooltip("Loop. Segnala che il pozzo è interagibile. Fermato all'attivazione.")]
    [SerializeField] private ParticleSystem idleVfx;

    [Tooltip("Loop. Suonato solo mentre il tether sta caricando il pozzo.")]
    [SerializeField] private ParticleSystem chargingVfx;

    [Tooltip("Burst una tantum all'attivazione, sul pozzo.")]
    [FormerlySerializedAs("activationParticles")]   // era già assegnato a VFX_WellActivation
    [SerializeField] private ParticleSystem activationVfx;

    [Tooltip("Burst una tantum sulla scala che compare.")]
    [SerializeField] private ParticleSystem staircaseRevealVfx;

    // ─── NetworkVariables ─────────────────────────────────────────────────────

    /// <summary>Progresso attivazione [0, 1]. Sincronizzato per la UI.</summary>
    public readonly NetworkVariable<float> Progress = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>True quando il pozzo è stato attivato.</summary>
    public readonly NetworkVariable<bool> IsActivated = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ─── Privati ─────────────────────────────────────────────────────────────

    private TetherManager _tether;

    // Guardia contro la doppia applicazione: OnValueChanged e la vecchia ClientRpc
    // arrivavano entrambi, facendo partire le particelle di attivazione due volte.
    private bool _activatedApplied;

    // Buffer statico condiviso per OverlapCapsuleNonAlloc: evita allocazioni ogni Update.
    private static readonly Collider[] _overlapBuffer = new Collider[8];

    // ─── NGO ─────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            _tether = FindFirstObjectByType<TetherManager>();

        IsActivated.OnValueChanged += (_, activated) =>
        {
            if (activated) ApplyActivatedState();
        };

        if (IsActivated.Value)
        {
            // Late joiner: il pozzo è già attivo, niente VFX né anello.
            ApplyActivatedState(playVfx: false);
        }
        else
        {
            progressRing?.SetVisible(true);
            progressRing?.ResetProgress();
            PlayIfNotPlaying(idleVfx);
        }
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    private void Update()
    {
        // Feedback: tutti i client leggono Progress (NetworkVariable sincronizzata)
        UpdateFeedback();

        // Timer: solo server
        if (!IsServer || IsActivated.Value) return;

        bool isTetherThrough = IsTetherPassingThrough();

        if (isTetherThrough)
        {
            Progress.Value = Mathf.Min(Progress.Value + Time.deltaTime / activationTime, 1f);
            if (Progress.Value >= 1f)
                Activate();
        }
        else
        {
            Progress.Value = Mathf.Max(Progress.Value - Time.deltaTime / activationTime, 0f);
        }
    }

    private void UpdateFeedback()
    {
        if (IsActivated.Value) return;

        float p = Progress.Value;
        progressRing?.SetProgress(p);

        // chargingVfx segue lo stato di carica, su ogni client.
        if (chargingVfx != null)
        {
            bool charging = p > 0.001f;
            if (charging && !chargingVfx.isPlaying)      chargingVfx.Play();
            else if (!charging && chargingVfx.isPlaying) chargingVfx.Stop();
        }
    }

    // ─── Rilevamento ──────────────────────────────────────────────────────────

    /// <summary>
    /// True se la capsula del tether (da PlayerA a PlayerB) interseca il collider del pozzo.
    /// Usa il wellLayer per evitare falsi positivi con muri e pavimento.
    /// </summary>
    private bool IsTetherPassingThrough()
    {
        if (_tether == null)
        {
            _tether = FindFirstObjectByType<TetherManager>();
            return false;
        }

        Transform a = _tether.GetTransformA();
        Transform b = _tether.GetTransformB();
        if (a == null || b == null) return false;

        int hitCount = Physics.OverlapCapsuleNonAlloc(
            a.position, b.position, tetherCapsuleRadius, _overlapBuffer, wellLayer);
        return hitCount > 0;
    }

    // ─── Attivazione ─────────────────────────────────────────────────────────

    private void Activate()
    {
        // IsActivated è server-write: assegnarla replica lo stato a tutti i client
        // e fa scattare OnValueChanged ovunque, incluso qui sull'host.
        // Non serve una ClientRpc — anzi, prima ne arrivavano due e le particelle
        // di attivazione partivano doppie.
        IsActivated.Value = true;

        if (undergroundDoor != null)
            undergroundDoor.Open();

        Debug.Log("[WellInteractable] Pozzo attivato — scala rivelata, Bond Ability sbloccata.");
    }

    private void ApplyActivatedState(bool playVfx = true)
    {
        if (_activatedApplied) return;
        _activatedApplied = true;

        if (staircaseObject != null) staircaseObject.SetActive(true);
        if (ladderObject    != null) ladderObject.SetActive(true);

        // L'anello ha finito il suo lavoro: il pozzo non è più interagibile.
        progressRing?.SetVisible(false);

        if (idleVfx     != null && idleVfx.isPlaying)     idleVfx.Stop();
        if (chargingVfx != null && chargingVfx.isPlaying) chargingVfx.Stop();

        if (!playVfx) return;

        activationVfx?.Play();
        staircaseRevealVfx?.Play();
    }

    private static void PlayIfNotPlaying(ParticleSystem ps)
    {
        if (ps != null && !ps.isPlaying) ps.Play();
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsActivated.Value ? Color.green : new Color(0f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, tetherCapsuleRadius * 1.5f);

        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 1.5f,
            $"Pozzo [{(IsActivated.Value ? "ATTIVATO" : $"{Progress.Value * 100f:F0}%")}]");
    }
#endif
}
