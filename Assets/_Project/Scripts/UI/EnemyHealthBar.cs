using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Barra della vita sopra la testa di un nemico. Billboard verso la camera.
///
/// Nascosta finché il nemico non subisce il primo colpo, poi svanisce dopo
/// hideDelay secondi dall'ultimo danno: con RoomWaveController che spawna ondate,
/// barre sempre accese sarebbero un muro di rosso.
///
/// Non serve nessuna RPC: HealthNetwork.CurrentHealth è già una NetworkVariable
/// con read permission Everyone, quindi OnValueChanged arriva a tutti i client.
///
/// Setup prefab: aggiungi il componente allo stesso GameObject che ha HealthNetwork.
/// Tutta la gerarchia UI viene generata a runtime.
/// </summary>
[RequireComponent(typeof(HealthNetwork))]
[DisallowMultipleComponent]
public class EnemyHealthBar : MonoBehaviour
{
    [Header("Posizione")]
    [Tooltip("Offset locale rispetto al pivot del nemico. Alzalo finché la barra " +
             "non sta sopra la testa.")]
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 2.4f, 0f);

    [Tooltip("Dimensione della barra in world units.")]
    [SerializeField] private Vector2 size = new Vector2(1.3f, 0.15f);

    [Header("Colori")]
    [SerializeField] private Color fillColor      = new Color(0.85f, 0.15f, 0.15f, 1f);
    [SerializeField] private Color lowHealthColor = new Color(0.45f, 0.05f, 0.05f, 1f);
    [SerializeField] private Color backColor      = new Color(0f, 0f, 0f, 0.65f);

    [Header("Comportamento")]
    [Tooltip("Secondi di visibilità dopo l'ultimo danno. Il timer riparte a ogni colpo.")]
    [SerializeField] private float hideDelay = 3f;

    [Tooltip("Sempre visibile, ignora hideDelay. Attivalo sui boss.")]
    [SerializeField] private bool alwaysVisible = false;

    [Tooltip("Velocità con cui il fill insegue il valore reale. 0 = istantaneo.")]
    [SerializeField] private float fillSmoothing = 14f;

    // Risoluzione interna del RectTransform. Arbitraria: la scala del canvas la
    // riporta a 'size' in world units.
    private const float PixelWidth  = 100f;
    private const float PixelHeight = 12f;

    private HealthNetwork _health;
    private GameObject    _root;
    private RectTransform _fillRect;
    private Image         _fillImage;
    private Camera        _cam;

    private float _hideAt;
    private float _displayedFill = 1f;

    private void Awake()
    {
        _health = GetComponent<HealthNetwork>();
        BuildBar();
        SetVisible(alwaysVisible);
    }

    private void OnEnable()  => _health.CurrentHealth.OnValueChanged += OnHealthChanged;
    private void OnDisable() => _health.CurrentHealth.OnValueChanged -= OnHealthChanged;

    // ─── Costruzione runtime ──────────────────────────────────────────────────

    private void BuildBar()
    {
        _root = new GameObject("HealthBar");
        _root.transform.SetParent(transform, worldPositionStays: false);

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var canvasRect = (RectTransform)_root.transform;
        canvasRect.sizeDelta = new Vector2(PixelWidth, PixelHeight);

        // Il nemico può avere una scale non unitaria: compensiamo la lossyScale,
        // altrimenti né 'size' né 'localOffset' sarebbero in world units.
        Vector3 s = transform.lossyScale;
        float sx = s.x != 0f ? s.x : 1f;
        float sy = s.y != 0f ? s.y : 1f;
        float sz = s.z != 0f ? s.z : 1f;

        canvasRect.localScale    = new Vector3(size.x / PixelWidth / sx, size.y / PixelHeight / sy, 1f);
        canvasRect.localPosition = new Vector3(localOffset.x / sx, localOffset.y / sy, localOffset.z / sz);

        // Image senza sprite disegna un rettangolo pieno: nessun asset richiesto.
        CreateStretchedImage("Background", canvasRect, backColor);
        _fillImage = CreateStretchedImage("Fill", canvasRect, fillColor);
        _fillRect  = _fillImage.rectTransform;
    }

    private static Image CreateStretchedImage(string goName, RectTransform parent, Color color)
    {
        var go = new GameObject(goName, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, worldPositionStays: false);

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    // ─── Stato ────────────────────────────────────────────────────────────────

    private void OnHealthChanged(int previous, int current)
    {
        // Il primo assegnamento server-side è 0 → maxHealth: non è un danno.
        // Mostriamo solo quando la vita scende davvero.
        if (current < previous && current > 0)
            _hideAt = Time.time + hideDelay;

        if (current <= 0)
        {
            SetVisible(false);
            return;
        }

        if (_hideAt > Time.time || alwaysVisible)
            SetVisible(true);
    }

    private void LateUpdate()
    {
        if (_root == null || !_root.activeSelf) return;

        if (!alwaysVisible && Time.time >= _hideAt)
        {
            SetVisible(false);
            return;
        }

        // Billboard: copiare la rotazione della camera (non LookAt) mantiene la
        // barra parallela allo schermo anche con camera ortografica.
        if (_cam == null) _cam = Camera.main;
        if (_cam != null) _root.transform.rotation = _cam.transform.rotation;

        float target = _health.MaxHealth > 0
            ? Mathf.Clamp01((float)_health.CurrentHealth.Value / _health.MaxHealth)
            : 0f;

        _displayedFill = fillSmoothing > 0f
            ? Mathf.Lerp(_displayedFill, target, 1f - Mathf.Exp(-fillSmoothing * Time.deltaTime))
            : target;

        // Cambiare gli anchor lascia intatti sizeDelta/offset: vanno riazzerati,
        // altrimenti il fill "sbava" fuori dal background.
        _fillRect.anchorMax = new Vector2(_displayedFill, 1f);
        _fillRect.offsetMin = Vector2.zero;
        _fillRect.offsetMax = Vector2.zero;

        _fillImage.color = Color.Lerp(lowHealthColor, fillColor, _displayedFill);
    }

    private void SetVisible(bool visible)
    {
        if (_root != null) _root.SetActive(visible);
    }
}
