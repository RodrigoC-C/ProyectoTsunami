using UnityEngine;
using System;
using System.IO;

public static class TerrainHeightmapLoader {
    // Carga una imagen (PNG/EXR). Si está normalizada 0..1, aplica y=scale*norm+offset.
    public static TopographyData LoadFromImage(string path, float scale, float offset, float nodata) {
        if (!File.Exists(path)) throw new Exception($"No existe heightmap: {path}");
        var bytes = File.ReadAllBytes(path);
        var tex = new Texture2D(2, 2, TextureFormat.RGBAHalf, false, true);
        if (!tex.LoadImage(bytes, false)) throw new Exception("No se pudo cargar la imagen de terreno");
        tex.Apply();

        int w = tex.width, h = tex.height;
        var cols = w; var rows = h;
        var colors = tex.GetPixels();
        var data = new TopographyData { rows = rows, cols = cols, h = new float[rows * cols], nodata = nodata };

        int i = 0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++, i++) {
                float norm = colors[i].r;        // 0..1
                data.h[i] = norm * scale + offset;
            }

        UnityEngine.Object.Destroy(tex);
        return data;
    }
}
