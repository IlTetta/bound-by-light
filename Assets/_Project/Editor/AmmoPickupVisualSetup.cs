using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools → BoundByLight → Setup Ammo Pickup Visual
///
/// Apri il prefab AmmoPickup in Prefab Edit Mode, poi esegui.
/// Sostituisce il materiale della sfera con un gold emissivo URP
/// e aggiunge un alone glow additivo + rotazione.
/// </summary>
public static class AmmoPickupVisualSetup
{
    [MenuItem("Tools/BoundByLight/Setup Ammo Pickup Visual")]
    public static void Run()
    {
        var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        if (stage == null)
        {
            EditorUtility.DisplayDialog("Ammo Pickup Visual Setup",
                "Apri prima il prefab AmmoPickup in Prefab Edit Mode\n" +
                "(doppio click su AmmoPickup.prefab nel Project).", "OK");
            return;
        }

        GameObject root = stage.prefabContentsRoot;

        // ── Materiale core (gold emissivo) ────────────────────────────────────
        const string corePath = "Assets/_Project/Materials/AmmoPickupCore.mat";
        var coreMat = AssetDatabase.LoadAssetAtPath<Material>(corePath);
        if (coreMat == null)
        {
            coreMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            coreMat.SetColor("_BaseColor", new Color(1f, 0.78f, 0.05f, 1f));      // gold
            coreMat.SetFloat("_Metallic", 0.7f);
            coreMat.SetFloat("_Smoothness", 0.85f);
            coreMat.EnableKeyword("_EMISSION");
            coreMat.SetColor("_EmissionColor", new Color(1f, 0.6f, 0f) * 2.5f);  // arancio caldo
            AssetDatabase.CreateAsset(coreMat, corePath);
        }

        // ── Materiale glow (additivo semi-trasparente) ────────────────────────
        const string glowPath = "Assets/_Project/Materials/AmmoPickupGlow.mat";
        var glowMat = AssetDatabase.LoadAssetAtPath<Material>(glowPath);
        if (glowMat == null)
        {
            glowMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            glowMat.SetColor("_BaseColor", new Color(1f, 0.65f, 0f, 0.18f));
            glowMat.EnableKeyword("_EMISSION");
            glowMat.SetColor("_EmissionColor", new Color(1f, 0.5f, 0f) * 1.5f);
            glowMat.SetFloat("_Surface", 1f);   // Transparent
            glowMat.SetFloat("_Blend", 3f);     // Additive
            glowMat.renderQueue = 3000;
            AssetDatabase.CreateAsset(glowMat, glowPath);
        }

        AssetDatabase.SaveAssets();

        // ── Sfera core (cerca quella esistente o crea) ────────────────────────
        Transform coreT = root.transform.Find("Visual") ?? root.transform.Find("Sphere");
        GameObject coreGo;
        if (coreT != null)
        {
            coreGo = coreT.gameObject;
            coreGo.name = "Visual";
        }
        else
        {
            coreGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            coreGo.name = "Visual";
            coreGo.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(coreGo.GetComponent<Collider>());
            Undo.RegisterCreatedObjectUndo(coreGo, "Create Visual");
        }

        coreGo.transform.localScale    = Vector3.one * 0.38f;
        coreGo.transform.localPosition = Vector3.zero;
        coreGo.GetComponent<Renderer>().sharedMaterial = coreMat;

        // Rimuovi collider residuo se presente
        var oldCol = coreGo.GetComponent<Collider>();
        if (oldCol != null) Object.DestroyImmediate(oldCol);

        // ── Sfera glow (figlia di Visual) ─────────────────────────────────────
        Transform glowT = coreGo.transform.Find("Glow");
        GameObject glowGo;
        if (glowT != null)
        {
            glowGo = glowT.gameObject;
        }
        else
        {
            glowGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glowGo.name = "Glow";
            glowGo.transform.SetParent(coreGo.transform, false);
            Object.DestroyImmediate(glowGo.GetComponent<Collider>());
            Undo.RegisterCreatedObjectUndo(glowGo, "Create Glow");
        }

        glowGo.transform.localScale    = Vector3.one * 1.7f;  // più grande della sfera core
        glowGo.transform.localPosition = Vector3.zero;
        glowGo.GetComponent<Renderer>().sharedMaterial = glowMat;

        // ── Rotazione lenta (SimpleRotate) ───────────────────────────────────
        if (coreGo.GetComponent<SimpleRotate>() == null)
        {
            var rot = Undo.AddComponent<SimpleRotate>(coreGo);
            // SimpleRotate ha un campo speed; se non esiste usa SerializedObject
            var so = new SerializedObject(rot);
            var speedProp = so.FindProperty("speed");
            if (speedProp != null) { speedProp.floatValue = 90f; so.ApplyModifiedProperties(); }
        }

        EditorUtility.SetDirty(root);
        Debug.Log("[AmmoPickupVisualSetup] Setup completato.");
        EditorUtility.DisplayDialog("Ammo Pickup Visual Setup",
            "Fatto!\n\n• AmmoPickupCore.mat (gold emissivo)\n• AmmoPickupGlow.mat (alone additivo)\n• Glow sphere aggiunto\n\nSalva con Ctrl+S.", "OK");
    }
}
