//using System.Collections;
//using UnityEngine;

///// <summary>
///// Flash del materiale quando il GameObject subisce danno.
/////
///// SETUP:
/////   1. Aggiungi questo componente allo stesso GameObject (o prefab) che ha HealthNetwork.
/////   2. In Inspector assegna:
/////        • Renderers   → tutti i SpriteRenderer / MeshRenderer / SkinnedMeshRenderer del prefab.
/////        • Flash Material → un materiale con colore bianco (o il colore di hit che vuoi).
/////        • Flash Duration → durata del flash in secondi (default 0.1 s).
/////   3. Chiama TriggerFlash() da HealthNetwork.OnHitClientRpc (vedi nota lì sotto).
/////
///// Come funziona:
/////   Salva i materiali originali di ogni renderer, li sostituisce con flashMaterial
/////   per flashDuration secondi, poi li ripristina.
/////   Usa una sola coroutine con flag _isFlashing per evitare sovrapposizioni.
///// </summary>
//public class HitFlashHandler : MonoBehaviour
//{
//    [Header("Renderers")]
//    [Tooltip("Trascina qui tutti i SpriteRenderer / MeshRenderer / SkinnedMeshRenderer del prefab.")]
//    [SerializeField] private Renderer[] renderers;

//    [Header("Flash Settings")]
//    [Tooltip("Materiale bianco (o colore hit) usato durante il flash.")]
//    [SerializeField] private Material flashMaterial;

//    [Tooltip("Durata del flash in secondi.")]
//    [SerializeField] private float flashDuration = 0.08f;

//    // Materiali originali per ogni renderer (array di array)
//    private Material[][] _originalMaterials;
//    private bool _isFlashing;

//    private void Awake()
//    {
//        // Se non assegnati manualmente, cerca in automatico
//        if (renderers == null || renderers.Length == 0)
//            renderers = GetComponentsInChildren<Renderer>(includeInactive: true);

//        CacheOriginalMaterials();
//    }

//    private void CacheOriginalMaterials()
//    {
//        _originalMaterials = new Material[renderers.Length][];
//        for (int i = 0; i < renderers.Length; i++)
//        {
//            if (renderers[i] == null) continue;
//            // .materials restituisce una copia → sicuro da salvare
//            _originalMaterials[i] = renderers[i].materials;
//        }
//    }

//    /// <summary>
//    /// Avvia il flash. Chiamato da HealthNetwork.OnHitClientRpc su ogni client.
//    /// </summary>
//    public void TriggerFlash()
//    {
//        if (flashMaterial == null) return;
//        if (_isFlashing) return; // evita sovrapposizioni: il flash precedente è ancora in corso

//        StartCoroutine(FlashCoroutine());
//    }

//    private IEnumerator FlashCoroutine()
//    {
//        _isFlashing = true;

//        // Sostituisce tutti i materiali con flashMaterial
//        for (int i = 0; i < renderers.Length; i++)
//        {
//            if (renderers[i] == null) continue;

//            int matCount = _originalMaterials[i]?.Length ?? 0;
//            if (matCount == 0) continue;

//            Material[] flashArray = new Material[matCount];
//            for (int m = 0; m < matCount; m++)
//                flashArray[m] = flashMaterial;

//            renderers[i].materials = flashArray;
//        }

//        yield return new WaitForSeconds(flashDuration);

//        // Ripristina i materiali originali
//        for (int i = 0; i < renderers.Length; i++)
//        {
//            if (renderers[i] == null) continue;
//            if (_originalMaterials[i] == null) continue;
//            renderers[i].materials = _originalMaterials[i];
//        }

//        _isFlashing = false;
//    }
//}

//using UnityEngine;
//using System.Collections;
//using System.Collections.Generic;

//public class DamageFlash : MonoBehaviour
//{

//    public Renderer rend;
//    public Color flashColor = Color.red;
//    public float flashDuration = 0.1f;

//    private Color originalColor;

//    void Start()
//    {
//        originalColor = rend.material.color;
//    }

//    private IEnumerator DoFlash()
//    {
//        rend.material.color = flashColor;
//        yield return new WaitForSeconds(flashDuration);
//        rend.material.color = originalColor;
//    }
//    public void Flash()
//    {
//        StopAllCoroutines();
//        StartCoroutine(DoFlash());
//    }

//    // Update is called once per frame
//    void Update()
//    {
//        if (Input.GetKeyDown(KeyCode.Space))
//        {
//            Flash();
//        }

//    }
//}

/// <summary>
/// Gestisce il flash visivo quando il GameObject subisce danno.
/// Unisce la gestione di renderer multipli al test rapido tramite input.
/// </summary>
using System.Collections;
using UnityEngine;

public class DamageFlashHandler : MonoBehaviour
{
    [Header("Renderers")]
    [Tooltip("Trascina qui tutti i Renderer del prefab. Se vuoto, li cerca in automatico nei figli.")]
    [SerializeField] private Renderer[] renderers;

    [Header("Flash Settings")]
    [Tooltip("Materiale base usato durante il flash.")]
    [SerializeField] private Material flashMaterial;

    [Tooltip("Il colore del flash (es. Rosso per il danno).")]
    [SerializeField] private Color flashColor = Color.red;

    [Tooltip("Durata del flash in secondi.")]
    [SerializeField] private float flashDuration = 0.08f;

    [Header("Debug/Test")]
    [Tooltip("Se attivo, premendo Spazio sulla tastiera attiverà il flash per test.")]
    [SerializeField] private bool testWithSpaceKey = true;

    private Material[][] _originalMaterials;
    private Coroutine _flashCoroutine;
    private bool _isFlashing;

    // Stringa ID per ottimizzare il cambio colore dello Shader
    private static readonly int ColorPropertyId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        if (renderers == null || renderers.Length == 0)
        {
            renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        }

        CacheOriginalMaterials();

        // Applica il colore scelto al materiale del flash al volo
        if (flashMaterial != null)
        {
            // Nota: se flashMaterial è un asset di progetto, questa riga lo modificherà globalmente.
            // Se usi uno shader URP/HDRP che non usa "_Color", cambia la stringa sopra con il nome corretto (es. "_BaseColor")
            if (flashMaterial.HasProperty(ColorPropertyId))
            {
                flashMaterial.SetColor(ColorPropertyId, flashColor);
            }
        }
    }

    private void Update()
    {
        if (testWithSpaceKey && Input.GetKeyDown(KeyCode.Space))
        {
            TriggerFlash();
        }
    }

    private void CacheOriginalMaterials()
    {
        _originalMaterials = new Material[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            _originalMaterials[i] = renderers[i].materials;
        }
    }

    public void TriggerFlash()
    {
        if (flashMaterial == null)
        {
            Debug.LogWarning($"[DamageFlashHandler] Manca il Flash Material su {gameObject.name}!", this);
            return;
        }

        if (_isFlashing && _flashCoroutine != null)
        {
            StopCoroutine(_flashCoroutine);
            ResetToOriginalMaterials();
        }

        _flashCoroutine = StartCoroutine(FlashCoroutine());
    }

    private IEnumerator FlashCoroutine()
    {
        _isFlashing = true;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;

            int matCount = _originalMaterials[i]?.Length ?? 0;
            if (matCount == 0) continue;

            Material[] flashArray = new Material[matCount];
            for (int m = 0; m < matCount; m++)
            {
                flashArray[m] = flashMaterial;
            }

            renderers[i].materials = flashArray;
        }

        yield return new WaitForSeconds(flashDuration);

        ResetToOriginalMaterials();
        _isFlashing = false;
    }

    private void ResetToOriginalMaterials()
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null || _originalMaterials[i] == null) continue;
            renderers[i].materials = _originalMaterials[i];
        }
    }
}