using UnityEngine;
using System;
using System.IO;
using System.Collections;

public class SceneController : MonoBehaviour {
    public TerrainMeshGenerator terrainGen;
    public OrbitCamera orbitCamera;

    public GeoUtil.BoundsMeters LastBoundsMeters { get; private set; }
    public Manifest LastManifest { get; private set; }

    public void LoadManifest(string manifestPath) {
            try {
            Debug.Log($"[SceneController] Cargando manifest: {manifestPath}");
            var baseDir = Path.GetDirectoryName(manifestPath);
            var json = File.ReadAllText(manifestPath);
            var mf = JsonUtility.FromJson<Manifest>(json);
            if (mf == null) throw new Exception("manifest.json inválido o vacío");
            LastManifest = mf;
            LoadCommon(baseDir, mf);
        } catch (Exception e) {
            Debug.LogError($"[SceneController] {e.Message}");
        }
    }

    public void LoadAdHoc(string topoPath, Manifest template) {
        try {
            LastManifest = template;
            var baseDir = Path.GetDirectoryName(topoPath);
            template.terrain.type = "json";
            template.terrain.topography_path = Path.GetFileName(topoPath);
            LoadCommon(baseDir, template);
        } catch (Exception e) { Debug.LogError($"[SceneController] {e.Message}"); }
    }

    void LoadCommon(string baseDir, Manifest mf) {
        if (mf == null) throw new Exception("Manifest nulo");
        if (mf.bbox == null) throw new Exception("Manifest sin bbox");
        if (terrainGen == null) throw new Exception("SceneController.terrainGen no asignado");
        if (orbitCamera == null) Debug.LogWarning("SceneController.orbitCamera no asignado (no se centrará la cámara)");
        TopographyData topo;

        string ttype = mf.TerrainTypeSafe;
        if (ttype == "image") {
            var hp = mf.terrain.heightmap_path;
            var full = Path.IsPathRooted(hp) ? hp : Path.Combine(baseDir, hp);
            topo = TerrainHeightmapLoader.LoadFromImage(full, mf.terrain.scale, mf.terrain.offset, mf.terrain.nodata);
        }
        else if (ttype == "raw16") {
            var rp = mf.terrain.raw_path;
            var full = Path.IsPathRooted(rp) ? rp : Path.Combine(baseDir, rp);
            topo = TerrainRaw16Loader.Load(full,
                    mf.terrain.raw_width, mf.terrain.raw_height,
                    Mathf.Max(1, mf.terrain.raw_stride),
                    mf.terrain.minHeight, mf.terrain.maxHeight);
        }
        else { // json (y compat legacy)
            var full = mf.ResolveTopographyPath(baseDir);
            if (string.IsNullOrEmpty(full)) throw new Exception("No se indicó topography_path");
            topo = TopographyData.LoadFromJson(full);
            // usa nodata del nuevo o del legacy
            float nodata = (mf.terrain != null) ? mf.terrain.nodata : mf.nodata;
            topo.ApplyScaleOffset(1f, 0f, nodata);
        }

        var bm = GeoUtil.BBoxToLocalMeters(mf.bbox);
        var terrainGO = terrainGen.Build(topo, bm);
        var mesh = terrainGO.GetComponent<MeshFilter>().sharedMesh;
        LastBoundsMeters = bm;

        StartCoroutine(FrameNextFrame(mesh.bounds, terrainGO.transform));
        
        orbitCamera?.Focus(mesh.bounds, terrainGO.transform);

        Debug.Log($"OK: {(mf.title ?? "Topografía")} ({topo.rows}x{topo.cols}) via {ttype}");
    }

    public void FrameCameraTo(Bounds b, Transform t)
    {
        orbitCamera?.Focus(b, t);
    }

    public IEnumerator FrameNextFrame(Bounds b, Transform t)
    {
        yield return null;
        FrameCameraTo(b, t);
    }
}
