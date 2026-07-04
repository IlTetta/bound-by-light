using UnityEngine;

/// <summary>
/// Anima il LineRenderer del telegraph nemico durante il Windup:
/// - Larghezza che cresce nel tempo (effetto "carica")
/// - Pulsazione dell'alpha (effetto "laser che si carica")
/// - Shift del colore verso il bianco all'avvicinarsi dello sparo
///
/// Setup: aggiungi questo script sullo stesso GameObject del LineRenderer del telegraph.
/// Il LineRenderer viene trovato automaticamente.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public sealed class TelegraphAnimator : MonoBehaviour
{
    [Header("Larghezza")]
    [SerializeField] private float startWidth = 0.02f;
    [SerializeField] private float maxWidth   = 0.12f;

    [Header("Pulsazione alpha")]
    [SerializeField] private float pulseFrequency = 6f;   // Hz — più alto = più rapido
    [SerializeField] private float pulseMinAlpha  = 0.3f;
    [SerializeField] private float pulseMaxAlpha  = 1f;

    [Header("Durata carica")]
    [Tooltip("Deve corrispondere al windup del nemico (EnemyRangedConfig.windup).")]
    [SerializeField] private float windupDuration = 1f;

    private LineRenderer _line;
    private float        _elapsed;
    private Color        _baseColor;

    private void Awake()
    {
        _line = GetComponent<LineRenderer>();
    }

    private void OnEnable()
    {
        _elapsed = 0f;
        if (_line != null)
        {
            _baseColor = _line.startColor;
            _line.startWidth = startWidth;
            _line.endWidth   = 0f;
        }
    }

    private void Update()
    {
        if (_line == null || !_line.enabled) return;

        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / windupDuration); // 0→1 durante il windup

        // Larghezza cresce con t (ease-in quadratico per effetto più drammatico sul finale)
        float width = Mathf.Lerp(startWidth, maxWidth, t * t);
        _line.startWidth = width;
        _line.endWidth   = 0f;

        // Alpha pulsa sempre più veloce man mano che si avvicina lo sparo
        float dynamicFreq = Mathf.Lerp(pulseFrequency, pulseFrequency * 3f, t);
        float alpha = Mathf.Lerp(
            pulseMinAlpha,
            pulseMaxAlpha,
            (Mathf.Sin(Time.time * dynamicFreq * Mathf.PI * 2f) + 1f) * 0.5f
        );

        // Shift verso il bianco sul finale (effetto "esplosione imminente")
        Color current = Color.Lerp(_baseColor, Color.white, t * t * 0.6f);
        current.a = alpha;
        _line.startColor = current;

        Color endCol = current;
        endCol.a = 0f;
        _line.endColor = endCol;
    }

    private void OnDisable()
    {
        // Resetta quando il telegraph viene spento
        if (_line != null)
        {
            _line.startWidth = startWidth;
            _line.endWidth   = 0f;
        }
    }
}
