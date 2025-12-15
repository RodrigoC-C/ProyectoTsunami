using UnityEngine;
using System.IO;

public static class TextureArrayBuilder {
    // Crea un Texture2DArray a partir de una lista ordenada de archivos (PNG/EXR)
    public static Texture2DArray BuildFromFiles(string[] paths) {
        if (paths == null || paths.Length == 0) return null;

        var firstBytes = File.ReadAllBytes(paths[0]);
        // Usamos RGBAHalf (16 bits flotante) para mapas de altura si vienen en EXR/PNG HDR
        var first = new Texture2D(2, 2, TextureFormat.RGBAHalf, false, true);
        first.LoadImage(firstBytes); first.Apply();
        int w = first.width, h = first.height;
        var fmt = first.format;

        var array = new Texture2DArray(w, h, paths.Length, fmt, false, true) {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        for (int i = 0; i < paths.Length; i++) {
            var bytes = File.ReadAllBytes(paths[i]);
            var tex = new Texture2D(w, h, fmt, false, true);
            tex.LoadImage(bytes); tex.Apply();
            Graphics.CopyTexture(tex, 0, 0, array, i, 0);
            Object.Destroy(tex);
        }
        Object.Destroy(first);
        array.Apply(false, false);
        return array;
    }
}