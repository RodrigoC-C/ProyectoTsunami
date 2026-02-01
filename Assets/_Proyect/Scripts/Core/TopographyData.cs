using UnityEngine;
using System;
using System.IO;

[Serializable] class TopoJson {
    public int rows;
    public int cols;
    public float[] heights;
}

public class TopographyData {
    public int rows, cols;
    public float[] h;
    public float nodata = -9999f;

    public static TopographyData LoadFromJson(string path) {
        if (!File.Exists(path)) throw new Exception($"No existe topo.json: {path}");
        var txt = File.ReadAllText(path);
        var tj = JsonUtility.FromJson<TopoJson>(txt);
        if (tj == null || tj.heights == null) throw new Exception("topo.json inválido");
        if (tj.rows * tj.cols != tj.heights.Length) throw new Exception("rows*cols != heights.Length");
        return new TopographyData { rows = tj.rows, cols = tj.cols, h = tj.heights };
    }

    public void ApplyScaleOffset(float scale, float offset, float nodataVal) {
        nodata = nodataVal;
        for (int i = 0; i < h.Length; i++) {
            if (!Mathf.Approximately(h[i], nodata)) h[i] = h[i] * scale + offset;
        }
    }
}