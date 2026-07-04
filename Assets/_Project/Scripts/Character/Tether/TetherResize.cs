using MyGame.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gestisce il visual del tether: LineRenderer principale + LineRenderer di glow.
///
/// Funzionalità:
///   - Onda sinusoidale lungo la corda (sembra una corda energetica viva)
///   - Larghezza che si assottiglia alle estremità (AnimationCurve)
///   - Pulsazione ritmica della larghezza
///   - Colore interpolato fluido in base al livello di energia
///   - Secondo LineRenderer (glow) più largo e semi-trasparente
/// </summary>
[RequireComponent(typeof(LineRenderer), typeof(TetherManager))]
public class TetherResize : NetworkBehaviour
{
    [Header("References")]
    [Tooltip("Secondo LineRenderer (figlio) per l'effetto glow. Creato da TetherSetup.")]
    [SerializeField] private LineRenderer glowRenderer;

    [Header("Colori")]
    [SerializeField] private Color normalColor     = new Color(0.2f, 0.85f, 1f, 1f);
    [SerializeField] private Color energeticColor  = new Color(1f, 0.85f, 0.05f, 1f);
    [SerializeField] private float colorLerpSpeed  = 3f;

    [Header("Larghezza")]
    [SerializeField] private float baseWidth   = 0.1f;
    [SerializeField] private float glowWidth   = 0.35f;
    [SerializeField] private float pulseAmount = 0.04f;
    [SerializeField] private float pulseSpeed  = 5f;

    [Header("Offset Verticale")]
    [Tooltip("Altezza in world-units a cui il tether si aggancia al player (0 = piedi).")]
    [SerializeField] private float verticalOffset = 0.8f;

    [Header("Onda")]
    [Tooltip("Numero di segmenti intermedi. Più alto = onda più morbida.")]
    [SerializeField] private int   segments       = 20;
    [SerializeField] private float waveAmplitude  = 0.1f;
    [SerializeField] private float waveFrequency  = 2f;   // onde complete lungo la corda
    [SerializeField] private float waveSpeed      = 4f;

    // ── Stato interno ─────────────────────────────────────────────────────────

    private LineRenderer _lineRenderer;
    private TetherManager _manager;
    private Color _currentColor;
    private float _currentEnergy;

    // AnimationCurve larghezza: 0 alle estremità, 1 al centro
    private static readonly AnimationCurve WidthCurve = new AnimationCurve(
        new Keyframe(0f,   0.3f),
        new Keyframe(0.2f, 1f),
        new Keyframe(0.5f, 1f),
        new Keyframe(0.8f, 1f),
        new Keyframe(1f,   0.3f)
    );

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        _manager      = GetComponent<TetherManager>();

        _lineRenderer.positionCount = segments + 2;
        _lineRenderer.useWorldSpace = true;
        _lineRenderer.widthCurve    = WidthCurve;

        if (glowRenderer != null)
        {
            glowRenderer.positionCount = segments + 2;
            glowRenderer.useWorldSpace = true;
            glowRenderer.widthCurve    = WidthCurve;
        }

        _currentColor = normalColor;
        ApplyColor(_currentColor);
    }

    private void LateUpdate()
    {
        if (_manager == null) return;

        Transform p1 = _manager.GetTransformA();
        Transform p2 = _manager.GetTransformB();

        bool valid = p1 != null && p2 != null;
        _lineRenderer.enabled = valid;
        if (glowRenderer != null) glowRenderer.enabled = valid;
        if (!valid) return;

        SyncEnergyColor();
        Vector3 up = new Vector3(0f, verticalOffset, 0f);
        UpdatePositions(p1.position + up, p2.position + up);
        UpdateWidth();
    }

    // ── Colore ────────────────────────────────────────────────────────────────

    private void SyncEnergyColor()
    {
        float energy = GameManager.Instance != null
            ? GameManager.Instance.SharedBondEnergy.Value / 100f
            : 0f;

        _currentEnergy = energy;

        Color target   = Color.Lerp(normalColor, energeticColor, energy);
        _currentColor  = Color.Lerp(_currentColor, target, Time.deltaTime * colorLerpSpeed);

        ApplyColor(_currentColor);
    }

    private void ApplyColor(Color c)
    {
        _lineRenderer.startColor = c;
        _lineRenderer.endColor   = c;

        if (glowRenderer != null)
        {
            Color glow   = c;
            glow.a       = Mathf.Lerp(0.15f, 0.35f, _currentEnergy);
            glowRenderer.startColor = glow;
            glowRenderer.endColor   = glow;
        }
    }

    // Chiamata esternamente se si vuole forzare un colore (es. effetto speciale)
    public void SetTetherColor(Color newColor) => _currentColor = newColor;

    // ── Posizioni ─────────────────────────────────────────────────────────────

    private void UpdatePositions(Vector3 start, Vector3 end)
    {
        int total = segments + 2;
        Vector3 mid = (start + end) * 0.5f;

        // Asse perpendicolare nel piano XZ per l'onda
        Vector3 dir  = (end - start);
        Vector3 perp = Vector3.Cross(dir.normalized, Vector3.up).normalized;

        for (int i = 0; i < total; i++)
        {
            float t   = (float)i / (total - 1);
            Vector3 p = Vector3.Lerp(start, end, t);

            // Sinusoide: ampiezza massima al centro, zero alle estremità
            float envelope = Mathf.Sin(t * Mathf.PI);
            float wave     = Mathf.Sin(t * Mathf.PI * 2f * waveFrequency - Time.time * waveSpeed)
                             * waveAmplitude * envelope;

            // L'onda aumenta di ampiezza con l'energia
            wave *= Mathf.Lerp(0.5f, 1.5f, _currentEnergy);

            p += perp * wave;

            _lineRenderer.SetPosition(i, p);
            if (glowRenderer != null) glowRenderer.SetPosition(i, p);
        }
    }

    // ── Larghezza ─────────────────────────────────────────────────────────────

    private void UpdateWidth()
    {
        // Larghezza base che pulsa, amplificata quando l'energia è alta
        float energyBonus = Mathf.Lerp(0f, 0.04f, _currentEnergy);
        float w = baseWidth + energyBonus
                  + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;

        _lineRenderer.widthMultiplier = w;

        if (glowRenderer != null)
            glowRenderer.widthMultiplier = glowWidth + energyBonus * 2f;
    }
}
