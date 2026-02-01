#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

public class HeightmapArrayBuilder : ScriptableObject
{
    [MenuItem("Tsunami/Build Heightmap Array (from selection)")]
    public static void BuildArray()
    {
        var texs = Selection.objects.OfType<Texture2D>().ToArray();
        if (texs.Length == 0)
        {
            Debug.LogWarning("Selecciona en el Project tus heightmaps 16-bit (mismo tamaño).");
            return;
        }

        // Verifica tamaño/formato
        int w = texs[0].width, h = texs[0].height;
        if (texs.Any(t => t.width != w || t.height != h))
        {
            Debug.LogError("Todas las texturas deben tener el MISMO tamaño.");
            return;
        }

        // Crear array: R16 lineal
        var arr = new Texture2DArray(w, h, texs.Length, TextureFormat.R16, /*mipmaps*/ false, /*linear*/ true);
        arr.wrapMode = TextureWrapMode.Clamp;
        arr.filterMode = FilterMode.Bilinear;

        // Garantiza Read/Write para copiar datos
        for (int i = 0; i < texs.Length; i++)
        {
            string path = AssetDatabase.GetAssetPath(texs[i]);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.sRGBTexture = false;            // lineal
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            // Copia cruda (R16) al slice i
            var raw = texs[i].GetRawTextureData();      // 2 bytes por píxel
            arr.SetPixelData(raw, /*mip*/0, /*element*/ i);
        }

        arr.Apply(false, false);

        string save = EditorUtility.SaveFilePanelInProject(
            "Guardar Heightmap Array",
            "Valpo_18frames",
            "asset",
            "Elige dónde guardar el Texture2DArray"
        );
        if (!string.IsNullOrEmpty(save))
        {
            AssetDatabase.CreateAsset(arr, save);
            AssetDatabase.SaveAssets();
            Selection.activeObject = arr;
            Debug.Log($"✔ Heightmap Array creado: {save}  ({texs.Length} slices, {w}x{h}, R16)");
        }
    }
}
#endif
