using UnityEngine;
using System;
using System.IO;
using System.Collections; 
#if UNITY_EDITOR
using UnityEditor;
#endif


public class SceneFolderController : MonoBehaviour
{
    public SceneController sceneController;
    public Action OnSimulationReady;

    [Header("Agua")]
    public MeshRenderer oceanRenderer;          // Renderer del OceanSurface
    public HeightArrayPlayback waterPlayback;   // Tu animador (puede estar en OceanRoot)
    public Shader tsunamiShader;

    // Alias para compatibilidad con el nombre que estás usando en otros scripts
    public Action onSimulationReady
    {
        get => OnSimulationReady;
        set => OnSimulationReady = value;
    }

    void Awake()
    {
        if (!sceneController) sceneController = FindObjectOfType<SceneController>();
        if (!oceanRenderer)   oceanRenderer   = GetComponentInChildren<MeshRenderer>();
        if (!waterPlayback)   waterPlayback   = GetComponentInChildren<HeightArrayPlayback>();
    }

    public void LoadFolder(string folderPath)
    {
        // 1) manifest
        string manifestPath = Path.Combine(folderPath, "manifest.json");
        if (File.Exists(manifestPath))
        {
            sceneController.LoadManifest(manifestPath);
            TryLoadWater(folderPath);
        }
        else
        {
            // Compat: topo.json suelto
            string topoJson = Path.Combine(folderPath, "topo.json");
            if (File.Exists(topoJson))
            {
                var adHoc = new Manifest {
                    bbox  = new BBox { minLon = 0, minLat = 0, maxLon = 0.01, maxLat = 0.01 },
                    title = "AdHoc (topo.json)"
                };
                sceneController.LoadAdHoc(topoJson, adHoc);
                TryLoadWater(folderPath);
            }
            else
            {
                Debug.LogError("[FolderLoader] No encontré manifest.json ni topo.json.");
            }
        }
    }

    void TryLoadWater(string folderPath)
    {
        try
        {
            var mf = sceneController.LastManifest;
            if (mf == null || mf.waves == null) { Debug.Log("[Water] Manifest sin sección 'waves'."); return; }

            string framesDir = Path.Combine(folderPath, mf.waves.frames_dir ?? "frames");
            if (!Directory.Exists(framesDir)) { Debug.Log("[Water] No hay carpeta de frames."); return; }

            // 1) Construir array en runtime (sin pasos manuales)
            var array = TextureArrayBuilderRuntime.BuildFromFolder(framesDir);

            // 2) Material del océano
            if (!oceanRenderer)
            {
                Debug.LogError("[Water] Falta 'oceanRenderer' en el SceneFolderController.");
                return;
            }
            var mat = oceanRenderer.sharedMaterial;
            if (tsunamiShader != null) mat.shader = tsunamiShader;
            if (mat == null)
            {
                Debug.LogError("[Water] OceanSurface no tiene material asignado. Asigna WaterMat en el Inspector.");
                return;
            }

            // 3) Enviar parámetros al material
            mat.SetTexture("_HeightArray", array);
            mat.SetFloat("_BaseLevel", mf.waves.base_level_norm);
            mat.SetFloat("_DispScale", mf.waves.disp_scale_m);
            mat.SetFloat("_FlipV", mf.waves.flipV ? 1f : 0f);
            mat.SetFloat("_Rotate90", mf.waves.rotate90 ? 1f : 0f);

            // 4) Sincronizar el playback y animar
            if (!waterPlayback) waterPlayback = oceanRenderer.GetComponentInParent<HeightArrayPlayback>();
            if (waterPlayback)
            {
                waterPlayback.targetRenderer  = oceanRenderer;
                waterPlayback.heightArray     = array;
                waterPlayback.dispScaleMeters = Mathf.Max(0.0001f, mf.waves.disp_scale_m);
                waterPlayback.flipV           = mf.waves.flipV;
                waterPlayback.rotate90        = mf.waves.rotate90;
                waterPlayback.externalDrive   = false;

                float dt = (mf.waves.frame_dt_s > 0f) ? mf.waves.frame_dt_s : 1f; // opcional en manifest
                waterPlayback.SetSecondsPerFrame(dt);
                waterPlayback.Play();
            }

            // 5) Escalar/posicionar el plano de agua para calzar con el terreno
            var bm = sceneController.LastBoundsMeters;
            var oceanT = oceanRenderer.transform;
            oceanT.localScale    = new Vector3(bm.size.x, 1f, bm.size.y);
            oceanT.localPosition = Vector3.zero;

            // Reencuadre tras escalar el agua:
            var terrainMF = sceneController.GetComponentInChildren<MeshFilter>();
            if (terrainMF != null)
            {
                var mesh = terrainMF.sharedMesh;
                StartCoroutine(sceneController.FrameNextFrame(mesh.bounds, terrainMF.transform));
            }

            Debug.Log($"[Water] OK. Frames: {array.depth}  Size: {array.width}x{array.height}  Format: {array.format}");
        }
        catch (Exception e)
        {
            Debug.LogError("[Water] " + e.Message);
        }
    }

    public void OpenPickerOrFallback() => LoadFolderFromPicker();
    // Llamado por el botón del menú
    public void LoadFolderFromPicker()
    {
        string folder = null;
        #if UNITY_EDITOR
        folder = EditorUtility.OpenFolderPanel("Selecciona carpeta de simulación", "", "");
        #else
        // Fallback simple en build
        folder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        #endif

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            Debug.LogWarning("[FolderLoader] Carpeta inválida.");
            return;
        }

        LoadFolder(folder);
    }
}
