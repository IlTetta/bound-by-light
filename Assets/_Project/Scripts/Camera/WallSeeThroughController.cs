using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Alimenta lo shader "BoundByLight/SeeThroughWall" con le posizioni dei player,
/// così i muri si "bucano" (dithering) attorno ai personaggi quando li occludono.
///
/// Funzionamento:
///   • Ogni frame calcola la posizione VIEWPORT (0..1) + la distanza dalla camera
///     di ciascun player e le scrive in proprietà GLOBALI dello shader.
///   • Essendo per-camera/per-client, funziona in automatico su entrambi i client:
///     ognuno rivela i player rispetto alla propria camera.
///
/// Setup:
///   1. Aggiungi questo componente al GameObject della Main Camera
///      (accanto a CoopCameraController).
///   2. Assegna lo shader "BoundByLight/SeeThroughWall" ai materiali dei MURI.
///   3. Regola heightOffset per centrare il buco sul torso invece che sui piedi.
/// </summary>
[DefaultExecutionOrder(100)] // dopo che i player si sono mossi/registrati
public sealed class WallSeeThroughController : MonoBehaviour
{
    [Header("Riferimenti")]
    [Tooltip("Se null usa CoopCameraController.Instance.Cam, poi Camera.main.")]
    [SerializeField] private Camera cam;

    [Header("Tuning")]
    [Tooltip("Offset verticale (m) del centro del buco: alza il cerchio dai piedi al torso.")]
    [SerializeField] private float heightOffset = 1.0f;

    // Deve combaciare con la dimensione dell'array nello shader (_SeeThroughPositions[8]).
    private const int MaxPlayers = 8;

    private static readonly int PositionsID = Shader.PropertyToID("_SeeThroughPositions");
    private static readonly int CountID     = Shader.PropertyToID("_SeeThroughCount");

    private readonly Vector4[] _positions = new Vector4[MaxPlayers];

    private void OnEnable()
    {
        // Stato pulito finché non abbiamo dati validi
        Shader.SetGlobalFloat(CountID, 0f);
    }

    private void OnDisable()
    {
        // Disattiva l'effetto: nessun buco
        Shader.SetGlobalFloat(CountID, 0f);
    }

    private void LateUpdate()
    {
        Camera c = ResolveCamera();
        if (c == null) return;

        var players = CoopCameraController.Instance != null
            ? CoopCameraController.Instance.Players
            : null;

        int count = 0;

        if (players != null)
        {
            for (int i = 0; i < players.Count && count < MaxPlayers; i++)
            {
                Transform p = players[i];
                if (p == null) continue;

                Vector3 world = p.position + Vector3.up * heightOffset;
                Vector3 vp    = c.WorldToViewportPoint(world);

                if (vp.z <= 0f) continue; // dietro la camera

                // xy = viewport (0..1), z = distanza dalla camera, w = libero
                _positions[count] = new Vector4(vp.x, vp.y, vp.z, 0f);
                count++;
            }
        }

        // Azzera gli slot inutilizzati (evita dati stantii)
        for (int i = count; i < MaxPlayers; i++)
            _positions[i] = Vector4.zero;

        Shader.SetGlobalVectorArray(PositionsID, _positions);
        Shader.SetGlobalFloat(CountID, count);
    }

    private Camera ResolveCamera()
    {
        if (cam != null) return cam;
        if (CoopCameraController.Instance != null && CoopCameraController.Instance.Cam != null)
            cam = CoopCameraController.Instance.Cam;
        else
            cam = Camera.main;
        return cam;
    }
}
