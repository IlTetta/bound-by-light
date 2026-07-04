using FMODUnity;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Coordina lo stato del puzzle di luce nella stanza.
/// Tiene traccia di quanti LightReceiver sono attivi e,
/// quando tutti sono attivi, triggera il completamento della stanza.
///
/// Setup nella scena:
///   - Un solo PuzzleRoomManager per stanza.
///   - Assegna tutti i LightReceiver della stanza in receivers[].
///   - Collega onPuzzleSolved all'apertura della porta / effetto di completamento.
/// </summary>
public class PuzzleRoomManager : NetworkBehaviour
{
    // --- Inspector ---

    // Riferimenti
    [Header("Receivers 2D (scena test)")]
    [Tooltip("LightReceiver 2D della scena di test.")]
    [SerializeField] private LightReceiver[] receivers;

    [Header("Receivers 3D (Cathedral)")]
    [Tooltip("LightReceiver3D della scena Cathedral.")]
    [SerializeField] private LightReceiver3D[] receivers3D;

    // Obiettivi
    [Header("Objectives")]
    [Tooltip("GameObject della porta da disabilitare alla risoluzione.")]
    [SerializeField] private GameObject doorObject;

    [Header("SFX")]
    [Tooltip("Emitter FMOD riprodotto quando il puzzle viene risolto (Puzzle finito).")]
    [SerializeField] private StudioEventEmitter puzzleSolvedEmitter;

    // Eventi
    [Header("Events")]
    [Tooltip("Evento richiamato quando il puzzle � risolto (una volta sola).")]
    public UnityEvent onPuzzleSolved;

    [Tooltip("Evento richiamato se il puzzle torna a uno stato non risolto.")]
    public UnityEvent onPuzzleReset;

    // --- Stato interno ---

    // readonly: evita che il campo venga riassegnato accidentalmente,
    // garantendo che OnValueChanged rimanga agganciato alla stessa istanza.
    private readonly NetworkVariable<bool> _isSolved = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private int _activeReceiverCount = 0;

    // --- Lifecycle ---

    public override void OnNetworkSpawn()
    {
        _isSolved.OnValueChanged += OnSolvedChanged;

        // Se la stanza era gi� risolta (es. riconnessione), applica subito lo stato
        if (_isSolved.Value)
            ApplySolvedState();
    }

    // --- API pubblica ---

    /// <summary>
    /// Forza il puzzle come risolto. Usato dal PuzzleSkipButton di debug.
    /// Solo server.
    /// </summary>
    public void ForceComplete()
    {
        if (!IsServer || _isSolved.Value) return;
        _activeReceiverCount = TotalReceivers();
        _isSolved.Value = true;
    }

    public void OnReceiverActivated(LightReceiver receiver)
    {
        // Il counter è fonte di verità solo sul server: i client non lo aggiornano.
        // Evita divergenze se il LightReceiver chiama questi metodi su ogni peer.
        if (!IsServer) return;
        _activeReceiverCount++;
        TryCheckCompletion();
    }

    public void OnReceiverDeactivated(LightReceiver receiver)
    {
        if (!IsServer) return;
        _activeReceiverCount = Mathf.Max(0, _activeReceiverCount - 1);
        if (_isSolved.Value) _isSolved.Value = false;
    }

    public void OnReceiverActivated(LightReceiver3D receiver)
    {
        if (!IsServer) return;
        _activeReceiverCount++;
        TryCheckCompletion();
    }

    public void OnReceiverDeactivated(LightReceiver3D receiver)
    {
        if (!IsServer) return;
        _activeReceiverCount = Mathf.Max(0, _activeReceiverCount - 1);
        if (_isSolved.Value) _isSolved.Value = false;
    }

    // --- Logica privata ---

    private int TotalReceivers()
        => (receivers?.Length ?? 0) + (receivers3D?.Length ?? 0);

    private void TryCheckCompletion()
    {
        // Chiamata solo dal server (guard negli OnReceiverActivated).
        if (_isSolved.Value) return;
        if (TotalReceivers() == 0) return;

        if (_activeReceiverCount >= TotalReceivers())
            _isSolved.Value = true;
    }

    // --- Reazioni a cambiamenti di stato ---

    private void OnSolvedChanged(bool oldValue, bool newValue)
    {
        if (newValue)
            ApplySolvedState();
        else
            ApplyResetState();
    }

    private void ApplySolvedState()
    {
        Debug.Log("[PuzzleRoomManager] Puzzle RISOLTO!");

        if (doorObject != null)
            doorObject.SetActive(false);

        puzzleSolvedEmitter?.Play();

        onPuzzleSolved?.Invoke();

        // Marca la stanza come Cleared (sblocca porte di uscita)
        if (IsServer)
            GetComponentInParent<Room>()?.SetCleared();
    }

    private void ApplyResetState()
    {
        Debug.Log("[PuzzleRoomManager] Puzzle RESETTATO.");

        if (doorObject != null)
            doorObject.SetActive(true);

        onPuzzleReset?.Invoke();
    }

    // --- Gizmo di debug ---

    private void OnDrawGizmos()
    {
        if (receivers == null) return;
        Gizmos.color = Color.green;
        foreach (var r in receivers)
        {
            if (r != null)
                Gizmos.DrawWireSphere(r.transform.position, 0.25f);
        }
    }
}
