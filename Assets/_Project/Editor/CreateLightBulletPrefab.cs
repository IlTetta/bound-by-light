using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Tools > BoundByLight > Create Light Bullet Prefab   — proiettile player (giallo)
/// Tools > BoundByLight > Create Enemy Bullet Prefab   — proiettile nemico (arancio-rosso)
/// Genera prefab stile "luce": capsula emissiva + trail + point light.
/// Dopo la creazione aggiungi manualmente NetworkObject al prefab root.
/// </summary>
public static class CreateLightBulletPrefab
{
    [MenuItem("Tools/BoundByLight/Create Light Bullet Prefab")]
    static void CreatePlayer()
    {
        var prefab = BuildBulletPrefab(
            goName:      "Bullet_Light",
            prefabPath:  "Assets/_Project/Prefabs/Weapon/Bullet_Light.prefab",
            matPath:     "Assets/_Project/Materials/LightBullet.mat",
            trailPath:   "Assets/_Project/Materials/LightBulletTrail.mat",
            bodyColor:   new Color(1f,  0.90f, 0.10f),   // giallo
            trailColorA: new Color(1f,  0.95f, 0.30f),
            trailColorB: new Color(1f,  0.60f, 0.00f),
            lightColor:  new Color(1f,  0.85f, 0.10f)
        );
        Ping(prefab, "Bullet_Light");
    }

    [MenuItem("Tools/BoundByLight/Create Enemy Bullet Prefab")]
    static void CreateEnemy()
    {
        var prefab = BuildBulletPrefab(
            goName:      "Bullet_Enemy_Light",
            prefabPath:  "Assets/_Project/Prefabs/Weapon/Bullet_Enemy_Light.prefab",
            matPath:     "Assets/_Project/Materials/EnemyLightBullet.mat",
            trailPath:   "Assets/_Project/Materials/EnemyLightBulletTrail.mat",
            bodyColor:   new Color(1f,  0.25f, 0.00f),   // arancio-rosso
            trailColorA: new Color(1f,  0.40f, 0.05f),
            trailColorB: new Color(0.8f,0.05f, 0.00f),
            lightColor:  new Color(1f,  0.20f, 0.00f)
        );
        Ping(prefab, "Bullet_Enemy_Light");
    }

    // ── Builder condiviso ─────────────────────────────────────────────────────

    static GameObject BuildBulletPrefab(
        string goName,
        string prefabPath,
        string matPath,
        string trailPath,
        Color  bodyColor,
        Color  trailColorA,
        Color  trailColorB,
        Color  lightColor)
    {
        Directory.CreateDirectory("Assets/_Project/Materials");
        Directory.CreateDirectory("Assets/_Project/Prefabs/Weapon");

        Material mat      = CreateUnlitMaterial(matPath,  bodyColor);
        Material trailMat = CreateUnlitMaterial(trailPath, trailColorA);
        SetMaterialTransparent(trailMat);
        AssetDatabase.SaveAssets();

        // ── Root ──────────────────────────────────────────────────────────────
        var root = new GameObject(goName);

        var rb            = root.AddComponent<Rigidbody>();
        rb.useGravity     = false;
        rb.isKinematic    = true;
        rb.freezeRotation = true;

        var col       = root.AddComponent<CapsuleCollider>();
        col.isTrigger = true;
        col.direction = 2;        // Z axis = direzione di volo
        col.radius    = 0.04f;
        col.height    = 0.35f;

        root.AddComponent<Projectile3D>();

        // ── Visual: capsula elongata lungo Z ──────────────────────────────────
        var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);
        // 90° su X → asse lungo punta in avanti (Z locale)
        visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        visual.transform.localScale    = new Vector3(0.06f, 0.18f, 0.06f);
        visual.GetComponent<MeshRenderer>().sharedMaterial = mat;
        Object.DestroyImmediate(visual.GetComponent<CapsuleCollider>());

        // ── Trail ─────────────────────────────────────────────────────────────
        var trailGO = new GameObject("Trail");
        trailGO.transform.SetParent(root.transform, false);
        var trail               = trailGO.AddComponent<TrailRenderer>();
        trail.time              = 0.12f;
        trail.startWidth        = 0.06f;
        trail.endWidth          = 0f;
        trail.minVertexDistance = 0.02f;
        trail.sharedMaterial    = trailMat;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(trailColorA, 0f),
                    new GradientColorKey(trailColorB,  1f) },
            new[] { new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f) }
        );
        trail.colorGradient = grad;

        // ── Point light ───────────────────────────────────────────────────────
        var glowGO = new GameObject("Glow");
        glowGO.transform.SetParent(root.transform, false);
        var light       = glowGO.AddComponent<Light>();
        light.type      = LightType.Point;
        light.color     = lightColor;
        light.intensity = 3f;
        light.range     = 1.5f;
        light.shadows   = LightShadows.None;

        // ── Salva prefab ──────────────────────────────────────────────────────
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return prefab;
    }

    static void Ping(GameObject prefab, string name)
    {
        Debug.Log($"[LightBullet] Prefab '{name}' creato.\n" +
                  "IMPORTANTE: apri il prefab e aggiungi manualmente il componente NetworkObject!");
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }

    // ── Helpers materiali ─────────────────────────────────────────────────────

    static Material CreateUnlitMaterial(string path, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Standard");

        var mat = new Material(shader) { color = color };

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);

        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 2f);
        }

        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
            AssetDatabase.DeleteAsset(path);

        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void SetMaterialTransparent(Material mat)
    {
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else if (mat.HasProperty("_Mode"))
        {
            mat.SetFloat("_Mode", 3f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_ALPHABLEND_ON");
    }
}
