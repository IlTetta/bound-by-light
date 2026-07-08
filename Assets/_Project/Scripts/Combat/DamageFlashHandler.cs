using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Flash del materiale quando il GameObject subisce danno.
/// Chiamato da HealthNetwork.OnHitClientRpc → gira su TUTTI i client.
///
/// Setup prefab:
///   1. Aggiungi questo componente allo stesso GameObject che ha HealthNetwork.
///   2. Assegna Flash Material (es. Flashdmg). Il COLORE si imposta NEL materiale,
///      non su questo componente: un campo che riscrive un asset condiviso è una
///      trappola (modifica il .mat su disco per tutti).
///   3. Renderers: lascia vuoto per l'auto-discovery, oppure assegna a mano.
///
/// L'auto-discovery esclude UI, minimappa, particelle e line/trail renderer:
/// senza questo filtro, sul player il flash colorerebbe anche il modello del
/// medikit nella HUD e gli anelli del ReviveIndicator.
/// </summary>
[DisallowMultipleComponent]
public class DamageFlashHandler : MonoBehaviour
{
    [Header("Renderers")]
    [Tooltip("Renderer da far lampeggiare. Se vuoto, cercati nei figli con filtro " +
             "(niente UI / minimappa / particelle / line renderer).")]
    [SerializeField] private Renderer[] renderers;

    [Header("Flash")]
    [Tooltip("Materiale usato durante il flash. Il colore si imposta NEL materiale.")]
    [SerializeField] private Material flashMaterial;

    [Tooltip("Durata del flash in secondi.")]
    [SerializeField] private float flashDuration = 0.08f;

    // Layer esclusi dall'auto-discovery. Risolti per nome: se un layer non esiste
    // NameToLayer restituisce -1, che non corrisponde a nessun gameObject.layer.
    private static readonly string[] ExcludedLayerNames = { "UI", "ItemHUD", "Minimap" };

    // sharedMaterials, non materials: il getter di .materials CLONA i materiali,
    // istanziandone uno per renderer per nemico e rompendo il batching per sempre.
    private Material[][] _originalMaterials;
    private Material[][] _flashMaterials;   // pre-allocati: nessuna alloc per colpo

    private Coroutine _flashRoutine;

    private void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = CollectRenderers();

        CacheMaterials();
    }

    private void OnDisable()
    {
        // Se il GameObject viene disattivato a metà flash la coroutine muore, e al
        // riattivo i renderer resterebbero col materiale del flash.
        if (_flashRoutine != null)
        {
            StopCoroutine(_flashRoutine);
            _flashRoutine = null;
            RestoreMaterials();
        }
    }

    // ─── Setup ────────────────────────────────────────────────────────────────

    private Renderer[] CollectRenderers()
    {
        var excluded = new HashSet<int>();
        foreach (string n in ExcludedLayerNames)
        {
            int layer = LayerMask.NameToLayer(n);
            if (layer >= 0) excluded.Add(layer);
        }

        var found = GetComponentsInChildren<Renderer>(includeInactive: true);
        var keep  = new List<Renderer>(found.Length);

        foreach (var r in found)
        {
            if (r == null) continue;
            if (r is LineRenderer || r is TrailRenderer || r is ParticleSystemRenderer) continue;
            if (excluded.Contains(r.gameObject.layer)) continue;
            if (r.GetComponentInParent<Canvas>() != null) continue;

            keep.Add(r);
        }
        return keep.ToArray();
    }

    private void CacheMaterials()
    {
        _originalMaterials = new Material[renderers.Length][];
        _flashMaterials    = new Material[renderers.Length][];

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;

            Material[] shared = renderers[i].sharedMaterials;
            _originalMaterials[i] = shared;

            if (flashMaterial == null || shared.Length == 0) continue;

            var flash = new Material[shared.Length];
            for (int m = 0; m < shared.Length; m++)
                flash[m] = flashMaterial;

            _flashMaterials[i] = flash;
        }
    }

    // ─── API ──────────────────────────────────────────────────────────────────

    /// <summary>Avvia il flash. Chiamato da HealthNetwork.OnHitClientRpc su ogni client.</summary>
    public void TriggerFlash()
    {
        if (flashMaterial == null)
        {
            Debug.LogWarning($"[DamageFlashHandler] Flash Material mancante su {name}.", this);
            return;
        }
        if (!isActiveAndEnabled) return;

        // Colpo su colpo: riavvia il timer invece di ignorare il nuovo hit.
        if (_flashRoutine != null) StopCoroutine(_flashRoutine);
        _flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && _flashMaterials[i] != null)
                renderers[i].sharedMaterials = _flashMaterials[i];
        }

        yield return new WaitForSeconds(flashDuration);

        RestoreMaterials();
        _flashRoutine = null;
    }

    private void RestoreMaterials()
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && _originalMaterials[i] != null)
                renderers[i].sharedMaterials = _originalMaterials[i];
        }
    }
}
