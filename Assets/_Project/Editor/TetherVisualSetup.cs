using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools → BoundByLight → Setup Tether Visuals
///
/// Apri il prefab Tether in modalità Prefab Edit, poi esegui questo tool.
/// Crea:
///   • Materiale TetherCore.mat  (linea principale – additive glow)
///   • Materiale TetherGlow.mat  (alone largo – additivo trasparente)
///   • GameObject figlio "GlowRenderer" con LineRenderer configurato
///   • Assegna i materiali a entrambi i LineRenderer
///   • Cablaggio del campo glowRenderer su TetherResize
/// </summary>
public static class TetherVisualSetup
{
    [MenuItem("Tools/BoundByLight/Setup Tether Visuals")]
    public static void Run()
    {
        // Il prefab deve essere aperto in Prefab Edit Mode
        var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        if (stage == null)
        {
            EditorUtility.DisplayDialog("Tether Visual Setup",
                "Apri prima il prefab Tether in modalità Prefab Edit\n" +
                "(doppio click su Tether.prefab nel Project).", "OK");
            return;
        }

        GameObject root = stage.prefabContentsRoot;

        // ── Materiali ─────────────────────────────────────────────────────────

        var core = CreateOrLoadMat(
            "Assets/_Project/Materials/TetherCore.mat",
            new Color(0.3f, 0.9f, 1f, 1f),
            new Color(0.2f, 0.8f, 1f) * 2f);

        var glow = CreateOrLoadMat(
            "Assets/_Project/Materials/TetherGlow.mat",
            new Color(0.2f, 0.7f, 1f, 0.25f),
            new Color(0.1f, 0.5f, 1f) * 1.2f);

        // ── LineRenderer principale ────────────────────────────────────────────

        var mainLr = root.GetComponent<LineRenderer>();
        if (mainLr != null)
        {
            mainLr.sharedMaterial    = core;
            mainLr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mainLr.receiveShadows    = false;
            mainLr.numCapVertices    = 4;
            mainLr.numCornerVertices = 4;
            mainLr.widthMultiplier   = 0.1f;   // preview pulito; runtime usa TetherResize
        }

        // ── GlowRenderer figlio ────────────────────────────────────────────────

        // Cerca se esiste già
        Transform glowT = root.transform.Find("GlowRenderer");
        GameObject glowGo;
        if (glowT != null)
        {
            glowGo = glowT.gameObject;
        }
        else
        {
            glowGo = new GameObject("GlowRenderer");
            glowGo.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(glowGo, "Create GlowRenderer");
        }

        var glowLr = glowGo.GetComponent<LineRenderer>();
        if (glowLr == null)
            glowLr = Undo.AddComponent<LineRenderer>(glowGo);

        glowLr.sharedMaterial    = glow;
        glowLr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        glowLr.receiveShadows    = false;
        glowLr.numCapVertices    = 4;
        glowLr.numCornerVertices = 4;
        glowLr.positionCount     = 2;
        // Disabilitato in edit mode: TetherResize lo abilita a runtime
        // solo quando entrambi i player Transform sono validi.
        glowLr.enabled           = false;

        // ── Wiring TetherResize → glowRenderer ────────────────────────────────

        var resize = root.GetComponent<TetherResize>();
        if (resize != null)
        {
            var so = new SerializedObject(resize);
            so.FindProperty("glowRenderer").objectReferenceValue = glowLr;
            so.ApplyModifiedProperties();
        }

        // ── Salva ─────────────────────────────────────────────────────────────

        AssetDatabase.SaveAssets();
        EditorUtility.SetDirty(root);

        Debug.Log("[TetherVisualSetup] Setup completato su " + root.name);
        EditorUtility.DisplayDialog("Tether Visual Setup",
            "Setup completato!\n\n" +
            "• TetherCore.mat e TetherGlow.mat creati\n" +
            "• GlowRenderer configurato\n" +
            "• Salva il prefab con Ctrl+S.",
            "OK");
    }

    private static Material CreateOrLoadMat(string path, Color baseColor, Color emission)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        mat.SetColor("_BaseColor", baseColor);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", emission);

        // Blending additivo per effetto glow
        mat.SetFloat("_Surface", 1f);          // Transparent
        mat.SetFloat("_Blend", 3f);            // Additive
        mat.renderQueue = 3000;

        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
