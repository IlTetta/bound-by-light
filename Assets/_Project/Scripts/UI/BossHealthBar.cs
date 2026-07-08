using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Barra del boss in HUD, stile Souls/Hades: sottile, centrata in alto, con
/// nome e chip damage.
///
/// Va sul prefab del boss, accanto a HealthNetwork. La vista (BossHealthBarView)
/// è creata a runtime come GameObject di root, NON come figlio del boss: alla
/// morte HealthNetwork fa Despawn(destroy: true), e una vista figlia sparirebbe
/// istantaneamente senza fade-out.
///
/// Setup prefab MiniBoss:
///   1. Add Component → Boss Health Bar.
///   2. Scrivi Boss Name.
///   3. Rimuovi Enemy Health Bar se l'avevi aggiunto: sono due letture diverse.
/// </summary>
[RequireComponent(typeof(HealthNetwork))]
[DisallowMultipleComponent]
public class BossHealthBar : MonoBehaviour
{
    [Serializable]
    public class Style
    {
        [Header("Layout")]
        [Tooltip("Dimensione della barra in pixel, a risoluzione di riferimento 1920x1080.")]
        public Vector2 barSize = new Vector2(900f, 18f);

        [Tooltip("Distanza dal bordo superiore dello schermo.")]
        public float topMargin = 70f;

        [Tooltip("Spessore della cornice attorno alla barra.")]
        public float frameThickness = 2f;

        [Header("Colori")]
        public Color fillColor      = new Color(0.72f, 0.10f, 0.10f, 1f);
        public Color lowHealthColor = new Color(0.35f, 0.03f, 0.03f, 1f);
        public Color chipColor      = new Color(0.95f, 0.85f, 0.60f, 0.9f);
        public Color backColor      = new Color(0.05f, 0.04f, 0.03f, 0.85f);
        public Color frameColor     = new Color(0.55f, 0.47f, 0.30f, 1f);

        [Header("Nome")]
        public TMP_FontAsset font;   // null → TMP_Settings.defaultFontAsset
        public float nameFontSize = 26f;
        [Tooltip("Distanza del nome dal bordo superiore della barra.")]
        public float nameSpacing  = 6f;
        public Color nameColor    = new Color(0.90f, 0.86f, 0.75f, 1f);

        [Header("Animazione")]
        [Tooltip("Velocità con cui il fill rosso insegue la vita reale.")]
        public float mainSmoothing = 14f;

        [Tooltip("Secondi di attesa prima che il chip inizi a calare.")]
        public float chipDelay = 0.45f;

        [Tooltip("Frazione di barra al secondo con cui il chip recupera.")]
        public float chipSpeed = 0.35f;

        public float fadeSpeed = 3f;

        [Header("Rendering")]
        [Tooltip("Sorting order del Canvas. Tienilo sotto quello del menu di pausa.")]
        public int sortingOrder = 5;
    }

    [Header("Boss")]
    [Tooltip("Nome mostrato sopra la barra. Vuoto = nessuna scritta.")]
    [SerializeField] private string bossName = "Bishop";

    [Tooltip("Se true la barra appare appena il boss è spawnato. " +
             "Se false appare al primo danno subito (utile se il boss è in attesa).")]
    [SerializeField] private bool showOnSpawn = true;

    [SerializeField] private Style style = new Style();

    private HealthNetwork      _health;
    private BossHealthBarView  _view;
    private bool _quitting;

    // CurrentHealth vale 0 finché il server non la inizializza a maxHealth in
    // OnNetworkSpawn. Prima di allora non spingiamo nulla, altrimenti la barra
    // apparirebbe vuota per un frame e poi si riempirebbe.
    private bool _seenAlive;

    private void Awake() => _health = GetComponent<HealthNetwork>();

    private void OnApplicationQuit() => _quitting = true;

    private void OnEnable()
    {
        _health.CurrentHealth.OnValueChanged += OnHealthChanged;
        if (showOnSpawn) EnsureView();
    }

    private void OnDisable()
    {
        _health.CurrentHealth.OnValueChanged -= OnHealthChanged;
    }

    private void OnDestroy()
    {
        if (_view == null) return;

        // In uscita dal play mode o durante l'unload della scena non ha senso animare:
        // gli oggetti stanno già venendo distrutti sotto di noi.
        if (_quitting || !gameObject.scene.isLoaded) _view.DestroyNow();
        else                                         _view.FadeOutAndDestroy();

        _view = null;
    }

    private void OnHealthChanged(int previous, int current)
    {
        // Il primo set server-side è 0 → maxHealth: non è un colpo incassato.
        if (!showOnSpawn && current < previous && current > 0)
            EnsureView();

        PushFill();
    }

    private void EnsureView()
    {
        if (_view != null) return;
        _view = BossHealthBarView.Create(style, bossName);
        PushFill();
    }

    private void PushFill()
    {
        if (_view == null || _health.MaxHealth <= 0) return;

        int hp = _health.CurrentHealth.Value;
        if (!_seenAlive)
        {
            if (hp <= 0) return;   // valore non ancora replicato
            _seenAlive = true;
        }

        _view.SetFill((float)hp / _health.MaxHealth);
    }

    private void LateUpdate()
    {
        // CurrentHealth arriva ai client con lo spawn payload, dopo OnEnable:
        // senza questo, la barra resterebbe piena finché non arriva il primo colpo.
        PushFill();
    }
}
