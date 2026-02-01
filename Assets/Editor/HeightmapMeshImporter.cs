using UnityEngine;
using UnityEditor;
using System.IO;
using UnityEngine.Rendering;

public class HeightmapMeshImporter : EditorWindow
{
    [Header("RAW (16-bit, little-endian)")]
    public TextAsset rawFile;       // arrastra tu .raw aquí
    public int width = 512;
    public int height = 750;

    [Header("Sampling / Downsample")]
    public int stride = 1;          // 1 = full res, 2 = la mitad, 4 ≈ 128x187 (≈24K vtx)

    [Header("World size (metros)")]
    public float sizeX = 150f;      // relación 0.015 : 0.022 → ajusta a tu caso
    public float sizeZ = 220f;

    [Header("Altura (metros)")]
    public float minHeight = -20f;  // debe coincidir con tu normalización
    public float maxHeight =  10f;

    [Header("Salida")]
    public string meshAssetName = "GeoClawPatch_Mesh";

    [MenuItem("Tools/Heightmap Mesh Importer")]
    public static void ShowWindow() => GetWindow<HeightmapMeshImporter>("Heightmap Mesh Importer");

    void OnGUI()
    {
        rawFile      = (TextAsset)EditorGUILayout.ObjectField("RAW 16-bit", rawFile, typeof(TextAsset), false);
        width        = EditorGUILayout.IntField("Width", width);
        height       = EditorGUILayout.IntField("Height", height);
        stride       = Mathf.Max(1, EditorGUILayout.IntField("Stride (>=1)", stride));
        sizeX        = EditorGUILayout.FloatField("Size X (meters)", sizeX);
        sizeZ        = EditorGUILayout.FloatField("Size Z (meters)", sizeZ);
        minHeight    = EditorGUILayout.FloatField("Min Height (m)", minHeight);
        maxHeight    = EditorGUILayout.FloatField("Max Height (m)", maxHeight);
        meshAssetName = EditorGUILayout.TextField("Mesh Asset Name", meshAssetName);

        using (new EditorGUI.DisabledScope(rawFile == null))
        {
            if (GUILayout.Button("Importar como Mesh"))
                ImportMesh();
        }
    }

    void ImportMesh()
    {
        byte[] bytes = rawFile.bytes;
        int expected = width * height * 2;
        if (bytes.Length < expected)
        {
            EditorUtility.DisplayDialog("Error", $"RAW size mismatch. Esperado {expected} bytes, recibido {bytes.Length}.", "OK");
            return;
        }

        // leer 16-bit little-endian a [0..1]
        float[] heights = new float[width * height];
        for (int i = 0, bi = 0; i < heights.Length; i++, bi += 2)
        {
            ushort v = (ushort)(bytes[bi] | (bytes[bi + 1] << 8)); // little endian
            heights[i] = v / 65535f; // [0..1]
        }

        int wS = (width  - 1) / stride + 1;
        int hS = (height - 1) / stride + 1;

        Vector3[] vertices = new Vector3[wS * hS];
        Vector2[] uvs      = new Vector2[wS * hS];
        int quadsX = wS - 1;
        int quadsZ = hS - 1;

        // indices (usar UInt32 si > 65k)
        var mesh = new Mesh();
        if ((long)quadsX * quadsZ * 6L > 65000L)
            mesh.indexFormat = IndexFormat.UInt32;

        int[] triangles = new int[quadsX * quadsZ * 6];

        float sx = sizeX / (wS - 1);
        float sz = sizeZ / (hS - 1);
        float heightScale = (maxHeight - minHeight);

        // construir vertices/uvs
        int vi = 0;
        for (int z = 0; z < hS; z++)
        {
            int srcZ = z * stride;
            for (int x = 0; x < wS; x++)
            {
                int srcX = x * stride;
                float h01 = heights[srcZ * width + srcX];        // [0..1]
                float yWorld = minHeight + h01 * heightScale;    // metros

                vertices[vi] = new Vector3(x * sx, yWorld, z * sz);
                uvs[vi] = new Vector2((float)srcX / (width - 1), (float)srcZ / (height - 1));
                vi++;
            }
        }

        // triángulos
        int ti = 0;
        for (int z = 0; z < quadsZ; z++)
        {
            for (int x = 0; x < quadsX; x++)
            {
                int i0 = z * wS + x;
                int i1 = i0 + 1;
                int i2 = i0 + wS;
                int i3 = i2 + 1;

                triangles[ti++] = i0; triangles[ti++] = i2; triangles[ti++] = i1;
                triangles[ti++] = i1; triangles[ti++] = i2; triangles[ti++] = i3;
            }
        }

        mesh.vertices  = vertices;
        mesh.uv        = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // guardar asset y crear GameObject
        string path = $"Assets/{meshAssetName}.asset";
        AssetDatabase.CreateAsset(mesh, path);
        AssetDatabase.SaveAssets();

        var go = new GameObject(meshAssetName);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = mesh;
        mr.sharedMaterial = new Material(Shader.Find("Standard"));

        Selection.activeObject = go;
        EditorGUIUtility.PingObject(go);
        Debug.Log($"✅ Mesh importado: {wS}x{hS} vertices (stride {stride}). Guardado en {path}");
    }
}
