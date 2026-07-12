using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Anello di progresso disegnato a terra: un cerchio di base che indica
/// "qui puoi interagire", e un arco che si riempie in senso orario.
///
/// Componente generico, pilotato da chi lo possiede tramite SetProgress().
/// Non conosce né il tether né il revive.
///
/// Setup: aggiungilo a un GameObject (tipicamente figlio dell'oggetto interagibile)
/// e assegna Line Material — consigliato BoundByLight/ReviveIndicator, che si vede
/// anche attraverso i muri.
/// </summary>
[DisallowMultipleComponent]
public class RadialProgressRing : MonoBehaviour
{
    [Header("Geometria")]
    [Tooltip("Raggio dell'anello di base, in world units.")]
    [SerializeField] private float baseRadius = 2f;

    [Tooltip("Raggio dell'arco di progresso. Di solito leggermente più piccolo.")]
    [SerializeField] private float progressRadius = 1.7f;

    [Tooltip("Altezza da terra, per evitare z-fighting col pavimento.")]
    [SerializeField] private float groundOffset = 0.06f;

    [Tooltip("Sposta il centro dell'anello rispetto al transform. Serve quando il pivot " +
             "dell'oggetto (es. pozzo con origine sballata) non è al centro visivo: " +
             "nudgia X/Z finché l'anello è centrato sotto il modello.")]
    [SerializeField] private Vector3 centerOffset = Vector3.zero;

    [SerializeField] private float baseWidth     = 0.08f;
    [SerializeField] private float progressWidth = 0.16f;

    [Header("Colori")]
    [SerializeField] private Color baseColor     = new Color(0.35f, 0.85f, 1f, 0.35f);
    [SerializeField] private Color progressColor = new Color(0.65f, 0.95f, 1f, 0.95f);

    [Header("Pulsazione")]
    [Tooltip("Oscillazione dell'opacità dell'anello di base. Pulsa l'alpha, non il " +
             "raggio: un raggio pulsante mentirebbe sull'area di interazione.")]
    [Range(0f, 1f)]
    [SerializeField] private float pulseAmount = 0.35f;
    [SerializeField] private float pulseSpeed  = 2.2f;

    [Header("Smoothing")]
    [Tooltip("Il progresso può arrivare dalla rete a scatti. 0 = nessuna interpolazione.")]
    [SerializeField] private float smoothing = 12f;

    [Header("Rendering")]
    [Tooltip("Materiale delle linee. Usa BoundByLight/ReviveIndicator per vederlo " +
             "attraverso i muri. Se vuoto, materiale di ripiego a runtime.")]
    [SerializeField] private Material lineMaterial;

    private const int BaseSegments     = 72;
    private const int ProgressSegments = 96;

    // Il cerchio giace sul piano XZ del mondo: LineAlignment.TransformZ usa il
    // piano XY locale del LineRenderer, quindi lo teniamo ruotato di 90° su X.
    private static readonly Quaternion Flat = Quaternion.Euler(90f, 0f, 0f);

    private LineRenderer _baseRing;
    private LineRenderer _progressArc;
    private bool  _visible;
    private float _target;
    private float _displayed;

    // ─── API ──────────────────────────────────────────────────────────────────

    /// <summary>Progresso 0..1. Chiamalo ogni frame o solo quando cambia.</summary>
    public void SetProgress(float normalized) => _target = Mathf.Clamp01(normalized);

    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (_baseRing    != null) _baseRing.enabled    = visible;
        if (_progressArc != null) _progressArc.enabled = visible;

        if (!visible && _progressArc != null)
        {
            _progressArc.positionCount = 0;
            _displayed = 0f;
        }
    }

    /// <summary>Azzera il progresso senza animazione (es. dopo un reset).</summary>
    public void ResetProgress()
    {
        _target = _displayed = 0f;
        if (_progressArc != null) _progressArc.positionCount = 0;
    }

    // ─── Unity ────────────────────────────────────────────────────────────────

    private void Awake()
    {
        Material mat = lineMaterial;
        if (mat == null)
        {
            Shader fallback = Shader.Find("BoundByLight/ReviveIndicator")
                              ?? Shader.Find("Sprites/Default");
            mat = new Material(fallback);
            Debug.LogWarning($"[RadialProgressRing] lineMaterial non assegnato su {name}: " +
                             "uso un materiale di ripiego.", this);
        }

        _baseRing    = CreateLine("BaseRing",    mat, baseWidth,     baseColor);
        _progressArc = CreateLine("ProgressArc", mat, progressWidth, progressColor);

        SetVisible(true);
    }

    private LineRenderer CreateLine(string goName, Material mat, float width, Color color)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(transform, worldPositionStays: false);

        // Se il parent ha una scale non unitaria, widthMultiplier verrebbe scalato:
        // compensiamo così i width in Inspector sono in world units.
        Vector3 s = transform.lossyScale;
        go.transform.localScale = new Vector3(
            s.x != 0f ? 1f / s.x : 1f,
            s.y != 0f ? 1f / s.y : 1f,
            s.z != 0f ? 1f / s.z : 1f);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace        = true; // punti già in world: immune a scale/rotazione
        lr.material             = mat;
        lr.widthMultiplier      = width;
        lr.startColor           = lr.endColor = color;
        lr.alignment            = LineAlignment.TransformZ;
        lr.textureMode          = LineTextureMode.Stretch;
        lr.numCornerVertices    = 2;
        lr.numCapVertices       = 2;
        lr.shadowCastingMode    = ShadowCastingMode.Off;
        lr.receiveShadows       = false;
        lr.lightProbeUsage      = LightProbeUsage.Off;
        lr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return lr;
    }

    private void LateUpdate()
    {
        if (!_visible) return;

        Vector3 center = transform.position
                       + transform.rotation * centerOffset
                       + Vector3.up * groundOffset;

        _baseRing.transform.rotation    = Flat;
        _progressArc.transform.rotation = Flat;

        Color bc = baseColor;
        bc.a *= 1f - pulseAmount * 0.5f * (1f + Mathf.Sin(Time.time * pulseSpeed));
        _baseRing.startColor = _baseRing.endColor = bc;

        DrawArc(_baseRing, center, baseRadius, 1f, BaseSegments);

        _displayed = smoothing > 0f
            ? Mathf.Lerp(_displayed, _target, 1f - Mathf.Exp(-smoothing * Time.deltaTime))
            : _target;

        if (_displayed <= 0.002f)
            _progressArc.positionCount = 0;
        else
            DrawArc(_progressArc, center, progressRadius, _displayed, ProgressSegments);
    }

    /// <summary>Arco sul piano XZ, da +Z in senso orario. fraction01 = 1 chiude il cerchio.</summary>
    private static void DrawArc(LineRenderer lr, Vector3 center, float radius,
                                float fraction01, int maxSegments)
    {
        bool closed  = fraction01 >= 0.999f;
        int segments = Mathf.Max(2, Mathf.CeilToInt(maxSegments * Mathf.Clamp01(fraction01)));

        lr.loop          = closed;
        lr.positionCount = closed ? segments : segments + 1;

        float sweep = Mathf.PI * 2f * Mathf.Clamp01(fraction01);

        for (int i = 0; i < lr.positionCount; i++)
        {
            float t   = (float)i / segments;
            float ang = Mathf.PI * 0.5f - sweep * t;
            lr.SetPosition(i, center + new Vector3(
                Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius));
        }
    }

    // ─── Preview in editor ──────────────────────────────────────────────────────
    // I LineRenderer si creano solo a runtime, quindi in edit mode l'anello non si
    // vede. Questa preview disegna i due cerchi alla posizione REALE (centerOffset +
    // groundOffset inclusi) così puoi centrarlo dalla Scene senza entrare in Play.
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 center = transform.position
                       + transform.rotation * centerOffset
                       + Vector3.up * groundOffset;

        UnityEditor.Handles.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.9f);
        UnityEditor.Handles.DrawWireDisc(center, Vector3.up, baseRadius);

        UnityEditor.Handles.color = new Color(progressColor.r, progressColor.g, progressColor.b, 0.9f);
        UnityEditor.Handles.DrawWireDisc(center, Vector3.up, progressRadius);
    }
#endif
}
