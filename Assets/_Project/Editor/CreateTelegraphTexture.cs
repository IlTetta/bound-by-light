using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Tools → BoundByLight → Create Telegraph Gradient Texture
/// Genera una texture PNG 128×16 con gradiente orizzontale:
///   sinistra = bianco opaco (origine del telegraph, vicino al nemico)
///   destra   = bianco trasparente (punta del telegraph, verso il target)
/// Da usare come Base Map su un materiale URP Unlit Transparent/Additive.
/// </summary>
public static class CreateTelegraphTexture
{
    private const string OutputPath = "Assets/_Project/Art/Texture/Telegraph_Gradient.png";
    private const int Width  = 16;
    private const int Height = 128;

    [MenuItem("Tools/BoundByLight/Create Telegraph Gradient Texture")]
    public static void Create()
    {
        // Assicura che la cartella esista
        string dir = Path.GetDirectoryName(OutputPath);
        if (!AssetDatabase.IsValidFolder(dir))
        {
            string parent = Path.GetDirectoryName(dir);
            string folder = Path.GetFileName(dir);
            AssetDatabase.CreateFolder(parent, folder);
        }

        var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);

        // Gradiente lungo Y (V direction): y=0 opaco (origine nemico), y=Height-1 trasparente (punta)
        for (int y = 0; y < Height; y++)
        {
            float t = (float)y / (Height - 1);
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, t);

            // Edge fade lungo X (U direction): bordi più trasparenti del centro
            for (int x = 0; x < Width; x++)
            {
                float xNorm = Mathf.Abs((float)x / (Width - 1) - 0.5f) * 2f; // 0 al centro, 1 ai bordi
                float edgeFade = 1f - Mathf.Pow(xNorm, 2f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * edgeFade));
            }
        }

        tex.Apply();

        // Salva come PNG
        byte[] bytes = tex.EncodeToPNG();
        File.WriteAllBytes(Path.GetFullPath(OutputPath), bytes);
        Object.DestroyImmediate(tex);

        AssetDatabase.Refresh();

        // Configura l'import: texture 2D, alpha trasparente, no mipmap (è piccola)
        var importer = AssetImporter.GetAtPath(OutputPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType        = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled      = false;
            importer.filterMode         = FilterMode.Bilinear;
            importer.wrapMode           = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(OutputPath);
        EditorGUIUtility.PingObject(asset);
        Debug.Log($"[Telegraph] Texture creata: {OutputPath}");
    }
}
