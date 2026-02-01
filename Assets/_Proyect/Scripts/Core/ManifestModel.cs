using System;

// ====== básicos ======
[Serializable] public class GridInfo { public int rows; public int cols; }
[Serializable] public class BBox   { public double minLon, minLat, maxLon, maxLat; }

// ====== vertical (compat viejo) ======
[Serializable] public class VerticalInfo {
    public float scale = 1f;     // usado cuando se normaliza
    public float offset = 0f;
    public string unit = "m";
    public string vertical_ref = "MSL";
}

// ====== nuevo: terreno / olas ======
[Serializable] public class TerrainSpec {
    // "json" | "image" | "raw16"
    public string type = "json";

    // json
    public string topography_path;

    // image (PNG/EXR normalizado 0..1)
    public string heightmap_path;
    public float  scale = 1f;    // y = scale*norm + offset
    public float  offset = 0f;

    // raw16
    public string raw_path;
    public int    raw_width;
    public int    raw_height;
    public int    raw_stride = 1;
    public float  minHeight = 0f;
    public float  maxHeight = 1f;

    // nodata (para json)
    public float nodata = -9999f;
}

[Serializable] public class WavesSpec {
    public string frames_dir = "frames";
    public float  disp_scale_m = 1f;
    public float  base_level_norm = 0.333f;
    public bool   flipV = true;
    public bool   rotate90 = false;
    public float  frame_dt_s = 1f;
}

// ====== MANIFEST con compatibilidad ======
[Serializable]
public class Manifest {
    public string version = "1.2";

    // NUEVO
    public TerrainSpec terrain = new TerrainSpec();
    public WavesSpec   waves   = new WavesSpec();

    public GridInfo grid;      // opcional
    public BBox     bbox;      // requerido
    public string   title = "Simulation";

    // ====== CAMPOS LEGACY (para que compile tu código viejo) ======
    // (si tu JSON antiguo los trae, seguirán funcionando)
    public string       topography_path;   // legacy
    public VerticalInfo vertical;          // legacy
    public float        nodata = -9999f;   // legacy

    // ====== helpers ======
    public string TerrainTypeSafe => terrain != null && !string.IsNullOrEmpty(terrain.type) ? terrain.type : "json";

    // Devuelve la ruta del topo (absoluta o relativa) con compatibilidad
    public string ResolveTopographyPath(string baseDir) {
        string p = null;
        if (TerrainTypeSafe == "json") {
            if (terrain != null && !string.IsNullOrEmpty(terrain.topography_path)) p = terrain.topography_path;
            else if (!string.IsNullOrEmpty(topography_path)) p = topography_path; // legacy
        }
        if (string.IsNullOrEmpty(p)) return null;
        return System.IO.Path.IsPathRooted(p) ? p : System.IO.Path.Combine(baseDir, p);
    }
}
