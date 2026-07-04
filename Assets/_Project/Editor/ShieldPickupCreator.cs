using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools → BoundByLight → Create Shield Pickup Prefab
/// Crea ShieldPickup.prefab in Assets/_Project/Prefabs/
/// </summary>
public static class ShieldPickupCreator
{
    [MenuItem("Tools/BoundByLight/Create Shield Pickup Prefab")]
    public static void Create()
    {
        // ── Materiale core (azzurro emissivo) ─────────────────────────────────
        const string corePath = "Assets/_Project/Materials/ShieldPickupCore.mat";
        var coreMat = AssetDatabase.LoadAssetAtPath<Material>(corePath);
        if (coreMat == null)
        {
            coreMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            coreMat.SetColor("_BaseColor", new Color(0.1f, 0.75f, 1f, 1f));
            coreMat.SetFloat("_Metallic",   0.3f);
            coreMat.SetFloat("_Smoothness", 0.9f);
            coreMat.EnableKeyword("_EMISSION");
            coreMat.SetColor("_EmissionColor", new Color(0.05f, 0.6f, 1f) * 3f);
            AssetDatabase.CreateAsset(coreMat, corePath);
        }

        // ── Materiale glow (additivo) ─────────────────────────────────────────
        const string glowPath = "Assets/_Project/Materials/ShieldPickupGlow.mat";
        var glowMat = AssetDatabase.LoadAssetAtPath<Material>(glowPath);
        if (glowMat == null)
        {
            glowMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            glowMat.SetColor("_BaseColor", new Color(0.05f, 0.6f, 1f, 0.15f));
            glowMat.EnableKeyword("_EMISSION");
            glowMat.SetColor("_EmissionColor", new Color(0.05f, 0.5f, 1f) * 1.8f);
            glowMat.SetFloat("_Surface", 1f);
            glowMat.SetFloat("_Blend",   3f);
            glowMat.renderQueue = 3000;
            AssetDatabase.CreateAsset(glowMat, glowPath);
        }

        AssetDatabase.SaveAssets();

        // ── Root ──────────────────────────────────────────────────────────────
        var root = new GameObject("ShieldPickup");

        int pickupLayer = LayerMask.NameToLayer("Pickup");
        if (pickupLayer >= 0) root.layer = pickupLayer;

        var col       = root.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius    = 0.7f;

        root.AddComponent<NetworkObject>();
        root.AddComponent<ShieldPickup>();

        // ── Visual root ───────────────────────────────────────────────────────
        var vis      = new GameObject("Visual");
        vis.transform.SetParent(root.transform, false);
        vis.transform.localPosition = new Vector3(0f, 0.6f, 0f);

        // Sfera core
        var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.name = "Core";
        core.transform.SetParent(vis.transform, false);
        core.transform.localScale = Vector3.one * 0.4f;
        Object.DestroyImmediate(core.GetComponent<Collider>());
        core.GetComponent<Renderer>().sharedMaterial = coreMat;
        if (pickupLayer >= 0) core.layer = pickupLayer;

        // Sfera glow
        var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glow.name = "Glow";
        glow.transform.SetParent(core.transform, false);
        glow.transform.localScale = Vector3.one * 1.8f;
        Object.DestroyImmediate(glow.GetComponent<Collider>());
        glow.GetComponent<Renderer>().sharedMaterial = glowMat;

        // Animazione: aggiungi SimpleGemsAnim manualmente sul Core dopo la creazione

        // ── Wiring ShieldPickup → visual ──────────────────────────────────────
        var pickup = new SerializedObject(root.GetComponent<ShieldPickup>());
        pickup.FindProperty("visual").objectReferenceValue = vis;
        pickup.ApplyModifiedPropertiesWithoutUndo();

        // ── Salva prefab ──────────────────────────────────────────────────────
        const string prefabPath = "Assets/_Project/Prefabs/ShieldPickup.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[ShieldPickupCreator] Prefab creato: " + prefabPath);
        EditorGUIUtility.PingObject(prefab);
        Selection.activeObject = prefab;
    }

}
