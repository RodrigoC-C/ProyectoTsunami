using UnityEngine;
using UnityEngine.Experimental.Rendering;
using System;
using System.IO;
using System.Linq;

public static class TextureArrayBuilderRuntime
{
    // Construye un Texture2DArray a partir de una carpeta con PNG/EXR.
    // Fuerza lectura lineal (no sRGB) y usa un GraphicsFormat soportado.
    public static Texture2DArray BuildFromFolder(string framesDir)
    {
        if (!Directory.Exists(framesDir))
            throw new Exception($"No existe carpeta de frames: {framesDir}");

        var files = Directory.EnumerateFiles(framesDir)
                             .Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                         p.EndsWith(".exr", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                             .ToArray();
        if (files.Length == 0)
            throw new Exception("No se encontraron PNG/EXR en la carpeta de frames.");

        Texture2D LoadLinear(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, /*linear*/ true);
            if (!t.LoadImage(bytes, false)) throw new Exception("No se pudo decodificar " + Path.GetFileName(path));
            t.Apply(false, false);
            return t;
        }

        var t0 = LoadLinear(files[0]);
        int w = t0.width, h = t0.height;

        // usa el mismo formato del primer PNG; si no es sampleable, cae a RGBA32 lineal (universal)
        GraphicsFormat fmt = t0.graphicsFormat;
        if (!SystemInfo.IsFormatSupported(fmt, FormatUsage.Sample))
            fmt = GraphicsFormat.R8G8B8A8_UNorm;

        var array = new Texture2DArray(w, h, files.Length, fmt, TextureCreationFlags.None)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        for (int i = 0; i < files.Length; i++)
        {
            var ti = LoadLinear(files[i]);
            if (ti.width != w || ti.height != h)
                throw new Exception($"Tamaño distinto en '{Path.GetFileName(files[i])}' ({ti.width}x{ti.height} vs {w}x{h}).");

            Graphics.CopyTexture(ti, 0, 0, array, i, 0);
            UnityEngine.Object.Destroy(ti);
        }

        UnityEngine.Object.Destroy(t0);
        
        
        return array;
    }
}