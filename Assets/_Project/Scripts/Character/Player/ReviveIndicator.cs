using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Feedback visivo del revive, disegnato a terra attorno al player fainted:
///   - un anello dorato che segna il raggio entro cui il compagno può rianimare
///   - un arco più stretto e brillante che si riempie mentre il compagno tiene H
///
/// Legge lo stato da PlayerFaintHandler (IsFainted / ReviveProgress01 / ReviveRadius),
/// entrambi replicati via NetworkVariable: l'indicatore è quindi identico su tutti
/// i client senza bisogno di RPC dedicate.
///
/// Setup prefab:
///   1. Aggiungi questo componente al root del PlayerPrefab (accanto a PlayerFaintHandler).
///   2. Assegna lineMaterial (vedi tooltip) — consigliato per evitare Shader.Find in build.
/// </summary>
[RequireComponent(typeof(PlayerFaintHandler))]
public class ReviveIndicator : MonoBehaviour
{
    [Header("Colori")]
    [Tooltip("Anello che indica il raggio di revive.")]
    [SerializeField] private Color rangeColor = new Color(1f, 0.78f, 0.25f, 0.35f);

    [Tooltip("Arco di progresso che si riempie tenendo premuto H.")]
    [SerializeField] private Color progressColor = new Color(1f, 0.92f, 0.55f, 0.95f);

    [Header("Geometria")]
    [Tooltip("Raggio dell'arco di progresso, attorno al modello. " +
             "Volutamente più piccolo del raggio di revive: lo slider deve stare " +
             "addosso al player, non sul bordo dell'area.")]
    [SerializeField] private float progressRadius = 1.6f;

    [Tooltip("Altezza da terra dei due anelli, per evitare z-fighting col pavimento.")]
    [SerializeField] private float groundOffset = 0.06f;

    [SerializeField] private float rangeWidth    = 0.08f;
    [SerializeField] private float progressWidth = 0.16f;

    [Header("Pulsazione")]
    [Tooltip("Oscillazione dell'opacità dell'anello di range. 0 = statico. " +
             "Nota: pulsa l'alpha, non il raggio — un raggio pulsante mentirebbe " +
             "sull'area reale in cui il revive funziona.")]
    [Range(0f, 1f)]
    [SerializeField] private float pulseAmount = 0.3f;
    [SerializeField] private float pulseSpeed  = 2.2f;

    [Header("Smoothing")]
    [Tooltip("Il server replica il progresso solo 10 volte al secondo (throttle RPC in " +
             "PlayerFaintHandler), quindi l'arco avanzerebbe a scatti. Questo lo " +
             "interpola. Valori alti = più reattivo, più scattoso.")]
    [SerializeField] private float progressSmoothing = 12f;

    [Header("Rendering")]
    [Tooltip("Materiale delle linee. Usa lo shader BoundByLight/ReviveIndicator per " +
             "vedere l'anello anche attraverso i muri.\n" +
             "Se lasciato vuoto viene creato a runtime un materiale di ripiego, ma " +
             "assegnalo: uno shader entra nella build solo se un materiale " +
             "referenziato da un prefab o da una scena lo usa — Shader.Find non basta.")]
    [SerializeField] private Material lineMaterial;

    private const int RangeSegments    = 72;
    private const int ProgressSegments = 96;

    // Il cerchio giace sul piano XZ del mondo: LineAlignment.TransformZ usa il piano
    // XY locale del LineRenderer, quindi lo ruotiamo di 90° su X.
    private static readonly Quaternion Flat = Quaternion.Euler(90f, 0f, 0f);

    private PlayerFaintHandler _faint;
    private LineRenderer _rangeRing;
    private LineRenderer _progressArc;
    private bool _visible;
    private float _displayedProgress;

    private void Awake()
    {
        _faint = GetComponent<PlayerFaintHandler>();

        Material mat = lineMaterial;
        if (mat == null)
        {
            Shader fallback = Shader.Find("BoundByLight/ReviveIndicator")
                              ?? Shader.Find("Sprites/Default");
            mat = new Material(fallback);
            Debug.LogWarning("[ReviveIndicator] lineMaterial non assegnato: uso un " +
                             "materiale di ripiego. Assegnalo nel prefab, o in build " +
                             "lo shader potrebbe non essere incluso.", this);
        }

        _rangeRing   = CreateLine("ReviveRangeRing",   mat, rangeWidth,    rangeColor);
        _progressArc = CreateLine("ReviveProgressArc", mat, progressWidth, progressColor);

        SetVisible(false);
    }

    private LineRenderer CreateLine(string goName, Material mat, float width, Color color)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(transform, worldPositionStays: false);

        // Il player root ha scale 0.2: senza compensare, widthMultiplier verrebbe
        // moltiplicato per la lossyScale e le linee risulterebbero 5x più sottili.
        Vector3 s = transform.lossyScale;
        go.transform.localScale = new Vector3(
            s.x != 0f ? 1f / s.x : 1f,
            s.y != 0f ? 1f / s.y : 1f,
            s.z != 0f ? 1f / s.z : 1f);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace      = true; // i punti sono già in world: immune a scale/rotazione del player
        lr.material           = mat;
        lr.widthMultiplier    = width;
        lr.startColor         = lr.endColor = color;
        lr.alignment          = LineAlignment.TransformZ;
        lr.textureMode        = LineTextureMode.Stretch;
        lr.numCornerVertices  = 2;
        lr.numCapVertices     = 2;
        lr.shadowCastingMode  = ShadowCastingMode.Off;
        lr.receiveShadows     = false;
        lr.lightProbeUsage    = LightProbeUsage.Off;
        lr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return lr;
    }

    private void LateUpdate()
    {
        // Prima dello spawn le NetworkVariable non hanno un valore significativo.
        if (!_faint.IsSpawned || !_faint.IsFainted)
        {
            if (_visible) SetVisible(false);
            return;
        }

        if (!_visible) SetVisible(true);

        // L'anello è centrato su transform.position perché è esattamente il punto
        // da cui FindFaintedCompanionInRange() misura la distanza. Centrarlo sul
        // modello lo renderebbe più bello e meno veritiero.
        Vector3 center = transform.position + Vector3.up * groundOffset;

        // La rotazione del player non deve inclinare gli anelli.
        _rangeRing.transform.rotation   = Flat;
        _progressArc.transform.rotation = Flat;

        Color rc = rangeColor;
        rc.a *= 1f - pulseAmount * 0.5f * (1f + Mathf.Sin(Time.time * pulseSpeed));
        _rangeRing.startColor = _rangeRing.endColor = rc;

        DrawArc(_rangeRing, center, _faint.ReviveRadius, 1f, RangeSegments);

        // Interpolazione esponenziale, frame-rate independent.
        _displayedProgress = Mathf.Lerp(
            _displayedProgress,
            _faint.ReviveProgress01,
            1f - Mathf.Exp(-progressSmoothing * Time.deltaTime));

        if (_displayedProgress <= 0.002f)
            _progressArc.positionCount = 0;
        else
            DrawArc(_progressArc, center, progressRadius, _displayedProgress, ProgressSegments);
    }

    /// <summary>
    /// Disegna un arco sul piano XZ, partendo dall'alto (+Z) e procedendo in senso orario.
    /// fraction01 = 1 chiude il cerchio.
    /// </summary>
    private static void DrawArc(LineRenderer lr, Vector3 center, float radius,
                                float fraction01, int maxSegments)
    {
        bool closed = fraction01 >= 0.999f;
        int segments = Mathf.Max(2, Mathf.CeilToInt(maxSegments * Mathf.Clamp01(fraction01)));

        lr.loop = closed;
        lr.positionCount = closed ? segments : segments + 1;

        float sweep = Mathf.PI * 2f * Mathf.Clamp01(fraction01);

        for (int i = 0; i < lr.positionCount; i++)
        {
            float t   = (float)i / segments;
            float ang = Mathf.PI * 0.5f - sweep * t; // parte da +Z, gira in senso orario
            lr.SetPosition(i, center + new Vector3(
                Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius));
        }
    }

    private void SetVisible(bool visible)
    {
        _visible = visible;
        _rangeRing.enabled   = visible;
        _progressArc.enabled = visible;

        if (!visible)
        {
            // Reset immediato: al prossimo faint l'arco deve ripartire da zero,
            // non riprendere l'interpolazione da dov'era rimasto.
            _progressArc.positionCount = 0;
            _displayedProgress = 0f;
        }
    }
}
