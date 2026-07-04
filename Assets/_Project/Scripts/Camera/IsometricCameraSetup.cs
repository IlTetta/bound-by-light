using UnityEngine;

/// <summary>
/// Helper per configurare la camera in modalità isometrica 2.5D stile Hades.
///
/// Come funziona la visuale Hades:
///   • Camera ortografica inclinata di ~30° sull'asse X
///   • Il movimento del player avviene nel piano X/Y (2D)
///   • L'inclinazione crea l'illusione di profondità
///   • Lo sprite sorting usa il valore Y (più in basso = davanti)
///
/// Setup MANUALE consigliato in Unity:
///   1. Seleziona la Main Camera nella scena
///   2. Imposta Projection = Orthographic
///   3. Imposta Rotation X = 30 (o tra 25 e 35 a gusto)
///   4. Imposta Rotation Y = 0, Z = 0
///   5. In CoopCameraController, imposta:
///         followOffset = (0, 6, -12)   ← offset di posizione sopra/dietro i player
///   6. Il renderer di ogni sprite/mesh deve usare "Sprite Default" o shader compatibile
///   7. In Project Settings → Graphics → Transparency Sort Mode = Custom Axis
///      Axis = (0, 1, 0) ← sorting per Y (chi è più in basso è davanti)
///
/// Questo script applica automaticamente i valori consigliati nell'Inspector
/// e può essere rimosso dopo aver fatto il setup manuale.
/// </summary>
[RequireComponent(typeof(Camera))]
public class IsometricCameraSetup : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Preset isometrico")]
    [Tooltip("Angolo di inclinazione sull'asse X. 30° = Hades. 45° = isometrico classico.")]
    [Range(20f, 60f)]
    [SerializeField] private float pitchAngle = 30f;

    [Tooltip("Rotazione sull'asse Y. 0 = guarda verso +Z. 180 = guarda verso -Z.")]
    [Range(-180f, 180f)]
    [SerializeField] private float yawAngle = 0f;

    [Tooltip("Orthographic size (metà altezza visibile in unità Unity).")]
    [SerializeField] private float orthographicSize = 7f;

    [Tooltip("Se true, applica i valori all'avvio. Utile per setup rapido in Play Mode.")]
    [SerializeField] private bool applyOnStart = true;

    [Header("Follow offset per CoopCameraController")]
    [Tooltip("Offset suggerito per CoopCameraController.followOffset con questo angolo.")]
    [SerializeField] private Vector3 suggestedFollowOffset = new(0f, 6f, -12f);

    // ─── Unity ────────────────────────────────────────────────────────────────

    private Camera _cam;

    private void Awake() => _cam = GetComponent<Camera>();

    private void Start()
    {
        if (applyOnStart)
            Apply();
    }

    // ─── API ──────────────────────────────────────────────────────────────────

    [ContextMenu("Applica setup isometrico")]
    public void Apply()
    {
        if (_cam == null) _cam = GetComponent<Camera>();

        // Camera ortografica
        _cam.orthographic      = true;
        _cam.orthographicSize  = orthographicSize;

        // Rotazione: solo X inclinata
        // Composizione esplicita: prima yaw attorno a Y (mondo), poi pitch attorno a X (mondo)
        // Evita il gimbal lock di Quaternion.Euler con yaw=180
        transform.rotation = Quaternion.AngleAxis(yawAngle, Vector3.up)
                           * Quaternion.AngleAxis(pitchAngle, Vector3.right);

        Debug.Log($"[IsometricCameraSetup] Applicato: pitch={pitchAngle}°, " +
                  $"orthoSize={orthographicSize}. " +
                  $"Imposta CoopCameraController.followOffset = {suggestedFollowOffset}");

        // Suggerimento per CoopCameraController
        var coop = FindFirstObjectByType<CoopCameraController>();
        if (coop != null)
            Debug.Log($"[IsometricCameraSetup] Trovato CoopCameraController. " +
                      $"Imposta manualmente followOffset = {suggestedFollowOffset} nell'Inspector.");
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Visualizza il cono di vista isometrico
        if (_cam == null) _cam = GetComponent<Camera>();
        if (_cam == null) return;

        Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
        float halfH = _cam.orthographic ? _cam.orthographicSize : 5f;
        float halfW = halfH * _cam.aspect;

        // Quattro angoli del frustum sul near plane
        Vector3 pos = transform.position;
        Vector3 fwd = transform.forward * 20f;

        Gizmos.DrawLine(pos, pos + transform.TransformDirection(new Vector3(-halfW, -halfH, 1f)));
        Gizmos.DrawLine(pos, pos + transform.TransformDirection(new Vector3( halfW, -halfH, 1f)));
        Gizmos.DrawLine(pos, pos + transform.TransformDirection(new Vector3(-halfW,  halfH, 1f)));
        Gizmos.DrawLine(pos, pos + transform.TransformDirection(new Vector3( halfW,  halfH, 1f)));
    }
#endif
}
