using UnityEngine;
using System;
using System.IO;

public static class TerrainRaw16Loader
{
    // RAW 16-bit, little-endian. Convierte a TopographyData con alturas en metros.
    public static TopographyData Load(string path, int width, int height,
                                      int stride, float minHeight, float maxHeight)
    {
        if (!File.Exists(path)) throw new Exception($"No existe RAW: {path}");
        var bytes = File.ReadAllBytes(path);

        long expected = (long)width * height * 2L;
        if (bytes.Length < expected)
            throw new Exception($"RAW size mismatch. Esperado {expected} bytes, recibido {bytes.Length}.");

        int wS = (width  - 1) / Math.Max(1, stride) + 1;
        int hS = (height - 1) / Math.Max(1, stride) + 1;

        var data = new TopographyData {
            rows = hS, cols = wS, h = new float[wS * hS], nodata = -9999f
        };

        float scale = (maxHeight - minHeight) / 65535f;

        int di = 0;
        for (int r = 0; r < hS; r++)
        {
            int srcZ = r * stride;
            for (int c = 0; c < wS; c++, di++)
            {
                int srcX = c * stride;
                int srcIndex = (srcZ * width + srcX) * 2;
                ushort v = (ushort)(bytes[srcIndex] | (bytes[srcIndex + 1] << 8)); // little-endian
                float meters = minHeight + v * scale;
                data.h[di] = meters;
            }
        }
        return data;
    }
}