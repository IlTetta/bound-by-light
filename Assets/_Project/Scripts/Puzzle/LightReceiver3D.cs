using FMODUnity;
using UnityEngine;

/// <summary>
/// Receiver 3D per il puzzle di Cathedral.
/// Non richiede NetworkObject: la sincronizzazione è gestita da PuzzleRoomManager.
/// Deve avere un BoxCollider su layer "PuzzleLight" affinché il raggio lo colpisca.
/// </summary>
public class LightReceiver3D : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PuzzleRoomManager puzzleManager;

    [Header("SFX")]
    [Tooltip("Emitter FMOD riprodotto quando la pedana viene attivata dal raggio (Pedana).")]
    [SerializeField] private StudioEventEmitter activationSfxEmitter;

    [Header("Visual")]
    [SerializeField] private Renderer receiverRenderer;
    [SerializeField] private Color idleColor = new Color(0.3f, 0.3f, 0.3f);
    [SerializeField] private Color activatedColor = new Color(1f, 0.95f, 0.4f);

    [Header("Particle (opzionale)")]
    [SerializeField] private ParticleSystem activationParticles;

    private bool _hitThisFrame;
    private bool _wasActiveLastFrame;
    private MaterialPropertyBlock _mpb;

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        if (receiverRenderer == null)
            receiverRenderer = GetComponent<Renderer>();
        SetColor(idleColor);
    }

    private void Start()
    {
        if (puzzleManager == null)
            puzzleManager = FindFirstObjectByType<PuzzleRoomManager>();

        if (puzzleManager == null)
            Debug.LogWarning("[LightReceiver3D] PuzzleRoomManager non trovato!");
    }

    private void LateUpdate()
    {
        if (_hitThisFrame && !_wasActiveLastFrame) OnActivated();
        if (!_hitThisFrame && _wasActiveLastFrame) OnDeactivated();
        _wasActiveLastFrame = _hitThisFrame;
        _hitThisFrame = false;
    }

    public void OnBeamReceived() => _hitThisFrame = true;

    private void OnActivated()
    {
        SetColor(activatedColor);
        activationParticles?.Play();
        activationSfxEmitter?.Play();
        puzzleManager?.OnReceiverActivated(this);
    }

    private void OnDeactivated()
    {
        SetColor(idleColor);
        activationParticles?.Stop();
        puzzleManager?.OnReceiverDeactivated(this);
    }

    private void SetColor(Color c)
    {
        if (receiverRenderer == null) return;
        receiverRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor("_BaseColor", c);
        _mpb.SetColor("_Color", c);
        receiverRenderer.SetPropertyBlock(_mpb);
    }
}
