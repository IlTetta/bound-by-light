using UnityEngine;
using Unity.Netcode;

public class LightReceiver : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private PuzzleRoomManager puzzleManager;

    [Header("Visual Feedback")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Color idleColor = new Color(0.3f, 0.3f, 0.3f);
    [SerializeField] private Color activatedColor = new Color(1f, 0.95f, 0.4f);

    [Header("Particle (opzionale)")]
    [SerializeField] private ParticleSystem activationParticles;

    private bool _hitThisFrame = false;
    private bool _wasActiveLastFrame = false;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
            spriteRenderer.color = idleColor;
    }

    public override void OnNetworkSpawn()
    {
        // Qui la rete è già attiva: PuzzleRoomManager è in scena
        if (puzzleManager == null)
            puzzleManager = FindFirstObjectByType<PuzzleRoomManager>();

        if (puzzleManager == null)
            Debug.LogWarning("[LightReceiver] PuzzleRoomManager non trovato!");
        else
            Debug.Log("[LightReceiver] PuzzleRoomManager trovato: " + puzzleManager.name);
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
        if (spriteRenderer != null) spriteRenderer.color = activatedColor;
        if (activationParticles != null) activationParticles.Play();

        Debug.Log("[LightReceiver] Attivato! puzzleManager=" + (puzzleManager != null ? puzzleManager.name : "NULL"));
        if (puzzleManager != null)
            puzzleManager.OnReceiverActivated(this);
    }

    private void OnDeactivated()
    {
        if (spriteRenderer != null) spriteRenderer.color = idleColor;
        if (activationParticles != null) activationParticles.Stop();
        if (puzzleManager != null)
            puzzleManager.OnReceiverDeactivated(this);
    }
}