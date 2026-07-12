using FMODUnity;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Pedana 3D per il puzzle di Cathedral.
/// Non richiede NetworkObject: rileva player con Physics3D.CheckSphere.
/// Quando entrambe le pedane sono occupate, attiva il PuzzleEmitter3D (solo server).
/// </summary>
public class BeamPedestal3D : MonoBehaviour
{
    [Header("References")]
    [SerializeField] public BeamPedestal3D otherPedestal;
    [SerializeField] public PuzzleEmitter3D emitter;

    [Header("Trigger")]
    [SerializeField] private float triggerRadius = 1f;
    [SerializeField] private LayerMask playerLayer;

    [Header("SFX")]
    [Tooltip("Emitter FMOD riprodotto quando un player sale sulla pedana (Pedana).")]
    [SerializeField] private StudioEventEmitter stepSfxEmitter;

    [Header("Visual (opzionale)")]
    [SerializeField] private Renderer visual;
    [SerializeField] private Color idleColor = new Color(0.3f, 0.3f, 0.8f, 1f);
    [SerializeField] private Color occupiedColor = new Color(0.2f, 1f, 0.4f, 1f);

    private bool _isOccupied;
    private MaterialPropertyBlock _mpb;

    public bool IsOccupied => _isOccupied;

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
    }

    private void Start()
    {
        if (emitter == null)
            InvokeRepeating(nameof(TryFindEmitter), 0.5f, 0.5f);
        ApplyVisual(false);

        // Imposta playerLayer a "Player" se non configurato dall'inspector
        if (playerLayer == 0)
            playerLayer = LayerMask.GetMask("Player");
    }

    private void TryFindEmitter()
    {
        emitter = FindFirstObjectByType<PuzzleEmitter3D>();
        if (emitter != null) CancelInvoke(nameof(TryFindEmitter));
    }

    private void Update()
    {
        bool occupied = Physics.CheckSphere(transform.position, triggerRadius, playerLayer);
        if (occupied == _isOccupied) return;

        _isOccupied = occupied;
        if (_isOccupied) stepSfxEmitter?.Play();
        ApplyVisual(_isOccupied);
        EvaluateBeam();
    }

    private void EvaluateBeam()
    {
        if (emitter == null) return;
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        bool both = _isOccupied && otherPedestal != null && otherPedestal.IsOccupied;
        emitter.SetBeamActive(both);
    }

    private void ApplyVisual(bool occupied)
    {
        if (visual == null) return;
        visual.GetPropertyBlock(_mpb);
        Color c = occupied ? occupiedColor : idleColor;
        _mpb.SetColor("_BaseColor", c);
        _mpb.SetColor("_Color", c);
        visual.SetPropertyBlock(_mpb);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 0.3f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, triggerRadius);
    }
}
