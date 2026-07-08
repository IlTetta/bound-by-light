using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Vista HUD della barra del boss. Generata interamente a runtime da BossHealthBar,
/// come GameObject di root: deve sopravvivere al despawn del boss per poter fare
/// il fade-out.
///
/// Due fill sovrapposti, come in Souls/Hades:
///   - "chip"  (sotto, chiaro) scende con ritardo → mostra quanto danno hai appena fatto
///   - "main"  (sopra, rosso)  segue subito la vita reale
/// La porzione di chip che sporge a destra del main è il danno recente.
/// </summary>
[DisallowMultipleComponent]
public class BossHealthBarView : MonoBehaviour
{
    private CanvasGroup   _group;
    private RectTransform _mainRect;
    private RectTransform _chipRect;
    private Image         _mainImage;
    private TextMeshProUGUI _label;

    private float _target = 1f;
    private float _main   = 1f;
    private float _chip   = 1f;

    private float _chipDelayUntil;
    private float _chipSpeed  = 0.35f;
    private float _chipDelay  = 0.45f;
    private float _mainSpeed  = 14f;
    private float _fadeSpeed  = 3f;
    private float _targetAlpha;

    private Color _fillColor;
    private Color _lowColor;

    private bool _dying;

    // ─── Costruzione ──────────────────────────────────────────────────────────

    public static BossHealthBarView Create(BossHealthBar.Style s, string bossName)
    {
        var go     = new GameObject("BossHealthBar");
        var view   = go.AddComponent<BossHealthBarView>();
        var canvas = go.AddComponent<Canvas>();

        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = s.sortingOrder;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        view._group = go.AddComponent<CanvasGroup>();
        view._group.alpha          = 0f;
        view._group.blocksRaycasts = false;
        view._group.interactable   = false;

        view.Build(s, bossName);
        view._targetAlpha = 1f;
        return view;
    }

    private void Build(BossHealthBar.Style s, string bossName)
    {
        _fillColor  = s.fillColor;
        _lowColor   = s.lowHealthColor;
        _chipSpeed  = s.chipSpeed;
        _chipDelay  = s.chipDelay;
        _mainSpeed  = s.mainSmoothing;
        _fadeSpeed  = s.fadeSpeed;

        // Contenitore ancorato in alto al centro.
        var container = NewRect("Container", (RectTransform)transform);
        container.anchorMin        = new Vector2(0.5f, 1f);
        container.anchorMax        = new Vector2(0.5f, 1f);
        container.pivot            = new Vector2(0.5f, 1f);
        container.anchoredPosition = new Vector2(0f, -s.topMargin);
        container.sizeDelta        = s.barSize;

        // Ordine di creazione = ordine di disegno: il Fill finisce sopra il Chip,
        // e la porzione di Chip che sporge a destra è il danno recente.
        Stretch(NewImage("Frame", container, s.frameColor).rectTransform, -s.frameThickness);
        Stretch(NewImage("Background", container, s.backColor).rectTransform, 0f);

        _chipRect  = NewImage("Chip", container, s.chipColor).rectTransform;
        _mainImage = NewImage("Fill", container, s.fillColor);
        _mainRect  = _mainImage.rectTransform;

        Stretch(_chipRect, 0f);
        Stretch(_mainRect, 0f);

        if (!string.IsNullOrEmpty(bossName))
            _label = BuildLabel(container, bossName, s);
    }

    private static TextMeshProUGUI BuildLabel(RectTransform parent, string bossName,
                                              BossHealthBar.Style s)
    {
        TMP_FontAsset font = s.font != null ? s.font : TMP_Settings.defaultFontAsset;
        if (font == null) return null; // nessun font: meglio niente testo che un crash

        var rect = NewRect("Name", parent);
        rect.anchorMin        = new Vector2(0.5f, 1f);
        rect.anchorMax        = new Vector2(0.5f, 1f);
        rect.pivot            = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, s.nameSpacing);
        rect.sizeDelta        = new Vector2(s.barSize.x, s.nameFontSize * 1.6f);

        var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font          = font;
        tmp.text          = bossName;
        tmp.fontSize      = s.nameFontSize;
        tmp.color         = s.nameColor;
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static RectTransform NewRect(string goName, RectTransform parent)
    {
        var go = new GameObject(goName, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, worldPositionStays: false);
        return rt;
    }

    private static Image NewImage(string goName, RectTransform parent, Color color)
    {
        var rt  = NewRect(goName, parent);
        var img = rt.gameObject.AddComponent<Image>(); // sprite null = rettangolo pieno
        img.color         = color;
        img.raycastTarget = false;
        return img;
    }

    private static void Stretch(RectTransform rt, float padding)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padding, padding);
        rt.offsetMax = new Vector2(-padding, -padding);
    }

    /// <summary>Larghezza del fill come frazione 0..1 del contenitore.</summary>
    private static void SetWidth(RectTransform rt, float fraction)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ─── API ──────────────────────────────────────────────────────────────────

    public void SetFill(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);
        if (normalized < _target)
            _chipDelayUntil = Time.time + _chipDelay; // il chip aspetta prima di inseguire
        _target = normalized;
    }

    public void SetName(string bossName)
    {
        if (_label != null) _label.text = bossName;
    }

    /// <summary>Fade-out e distruzione. Il boss può già essere stato despawnato.</summary>
    public void FadeOutAndDestroy()
    {
        _dying       = true;
        _targetAlpha = 0f;
    }

    /// <summary>Distruzione immediata (quit, cambio scena): niente coroutine in volo.</summary>
    public void DestroyNow()
    {
        if (this != null) Destroy(gameObject);
    }

    // ─── Loop ─────────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        _group.alpha = Mathf.MoveTowards(_group.alpha, _targetAlpha, _fadeSpeed * Time.deltaTime);

        if (_dying && _group.alpha <= 0.001f)
        {
            Destroy(gameObject);
            return;
        }

        // Il main insegue subito, il chip resta indietro e cala dopo chipDelay.
        _main = Mathf.Lerp(_main, _target, 1f - Mathf.Exp(-_mainSpeed * Time.deltaTime));

        if (Time.time >= _chipDelayUntil)
            _chip = Mathf.MoveTowards(_chip, _target, _chipSpeed * Time.deltaTime);
        else
            _chip = Mathf.Max(_chip, _target); // il chip non risale mai sotto il main

        SetWidth(_mainRect, _main);
        SetWidth(_chipRect, Mathf.Max(_chip, _main));

        _mainImage.color = Color.Lerp(_lowColor, _fillColor, _main);
    }
}
