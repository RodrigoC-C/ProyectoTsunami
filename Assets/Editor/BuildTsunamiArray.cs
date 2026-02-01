using UnityEngine;
using UnityEditor;
using System.IO;

public class BuildTsunamiArray : EditorWindow
{
    public string folder = "Assets/Resources/TsunamiFrames";   // donde están tus 181 png
    public string pattern = "heightmap_";                      // prefijo
    public int count = 181;
    public int digits = 4;
    public string savePath = "Assets/TsunamiHeightArray.asset";

    [MenuItem("Tools/Tsunami/Build HeightArray")]
    static void Open() => GetWindow<BuildTsunamiArray>("Build Tsunami Array");

    void OnGUI()
    {
        folder = EditorGUILayout.TextField("Folder", folder);
        pattern = EditorGUILayout.TextField("Pattern", pattern);
        count = EditorGUILayout.IntField("Count", count);
        digits = EditorGUILayout.IntField("Digits", digits);
        savePath = EditorGUILayout.TextField("Save Path", savePath);

        if (GUILayout.Button("Build"))
            Build();
    }

    void Build()
    {
        Texture2D[] texs = new Texture2D[count];
        for (int i = 0; i < count; i++)
        {
            string idx = (i + 1).ToString().PadLeft(digits, '0');
            string path = $"{folder}/{pattern}{idx}.png";
            texs[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!texs[i])
            {
                Debug.LogError("No se encontró " + path);
                return;
            }
        }

        int w = texs[0].width;
        int h = texs[0].height;
        var array = new Texture2DArray(w, h, count, TextureFormat.R16, false, true);
        array.wrapMode = TextureWrapMode.Clamp;
        array.filterMode = FilterMode.Bilinear;

        for (int i = 0; i < count; i++)
        {
            var tex = texs[i];
            if (tex.width != w || tex.height != h)
            {
                Debug.LogError($"La textura {tex.name} tiene tamaño distinto ({tex.width}x{tex.height})");
                return;
            }
            Graphics.CopyTexture(tex, 0, 0, array, i, 0);
        }

        AssetDatabase.CreateAsset(array, savePath);
        AssetDatabase.SaveAssets();
        Debug.Log("✅ HeightArray creado en: " + savePath);
    }
}
