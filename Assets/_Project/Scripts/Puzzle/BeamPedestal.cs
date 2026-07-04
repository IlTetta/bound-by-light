using Unity.Netcode;
using UnityEngine;

/// <summary>
/// NON richiede NetworkObject: è un oggetto statico di scena.
/// Usa Update() per controllare la presenza dei player nel raggio,
/// invece di OnTriggerEnter/Exit che richiedeva NetworkObject per funzionare.
/// </summary>
public class BeamPedestal : MonoBehaviour
{
    [Header("References")]
    [SerializeField] public BeamPedestal otherPedestal;
    [SerializeField] public TetherLightBeam tetherLightBeam;

    [Header("Trigger Settings")]
    [SerializeField] private float triggerRadius = 1f;

    [Header("Visual")]
    [SerializeField] private SpriteRenderer visual;
    [SerializeField] private Color idleColor = new Color(0.3f, 0.3f, 0.8f, 1f);
    [SerializeField] private Color occupiedColor = new Color(0.2f, 1f, 0.4f, 1f);

    private bool _isOccupied = false;
    public bool IsOccupied => _isOccupied;

    private void Start()
    {
        // Cerca TetherLightBeam se non assegnato (spawna a runtime)
        if (tetherLightBeam == null)
            InvokeRepeating(nameof(TryFindBeam), 0.5f, 0.5f);

        ApplyVisual(false);
    }

    private void TryFindBeam()
    {
        tetherLightBeam = FindObjectOfType<TetherLightBeam>();
        if (tetherLightBeam != null)
            CancelInvoke(nameof(TryFindBeam));
    }

    private void Update()
    {
        // Polling: controlla ogni frame se un player è nel raggio
        // Funziona senza NetworkObject e senza trigger
        bool occupied = IsPlayerInRange();

        if (occupied != _isOccupied)
        {
            _isOccupied = occupied;
            ApplyVisual(_isOccupied);
            EvaluateBeam();
        }
    }

    private bool IsPlayerInRange()
    {
        // Cerca tutti i collider nel raggio su layer Player
        var hits = Physics2D.OverlapCircleAll(transform.position, triggerRadius,
                                              LayerMask.GetMask("Player"));
        return hits.Length > 0;
    }

    private void EvaluateBeam()
    {
        if (tetherLightBeam == null) return;

        // Solo il server attiva il raggio
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        bool both = _isOccupied && otherPedestal != null && otherPedestal.IsOccupied;
        tetherLightBeam.SetBeamActive(both);
    }

    private void ApplyVisual(bool occupied)
    {
        if (visual != null)
            visual.color = occupied ? occupiedColor : idleColor;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 0.3f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, triggerRadius);
    }
}