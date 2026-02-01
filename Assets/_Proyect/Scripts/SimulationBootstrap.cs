using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Tsunami.Services;

public class SimulationBootstrap : MonoBehaviour
{
    [Header("Backend")]
    public string apiBaseUrl = "http://localhost:8000";
    public string city = "Valparaiso";
    public string scenario = "TsunamiTest";

    [Header("Descarga")]
    [Range(1, 16)] public int maxConcurrentDownloads = 4;

    private DataService _dataService;
    private CancellationTokenSource _cts;

    public IReadOnlyList<string> LocalHeightmapPaths { get; private set; }
    public List<DataService.FrameMeta> LocalHeightmapMeta { get; private set; }

    private async void Start()
    {
        _cts = new CancellationTokenSource();
        _dataService = new DataService(apiBaseUrl) { MaxConcurrentDownloads = maxConcurrentDownloads };

        try
        {
            var metas = await _dataService.PreloadHeightmapsWithMetaAsync(city, scenario, _cts.Token);

            LocalHeightmapMeta = metas.ToList();
            LocalHeightmapPaths = LocalHeightmapMeta.Select(m => m.LocalPath).ToList();

            // Ajusta el playback array si lo usas aparte
            var playback = FindObjectOfType<HeightArrayPlayback>();
            if (playback != null && _dataService.LastFrameIntervalSeconds > 0)
                playback.SetSecondsPerFrame(_dataService.LastFrameIntervalSeconds);

            // Timeline unificado (UI + shader + manager) con compresión de tiempo
            var timeline = FindObjectOfType<TsunamiTimelineController>();
            if (timeline != null)
            {
                var tlist = LocalHeightmapMeta.ConvertAll(m => m.TSec);   // ORDENADOS
                float spf = _dataService.LastFrameIntervalSeconds > 0 ? _dataService.LastFrameIntervalSeconds : 0f;

                timeline.compressToTargetDuration = true;          // comprime horas a minutos
                timeline.targetPlaybackTotalSeconds = 180f;        // 3 min (ajústalo)
                timeline.ConfigureFromTimes(tlist, spf);
            }

            // Pasar la info al manager para mostrar el primer frame/logística extra
            var manager = FindObjectOfType<TsunamiManager>();
            if (manager != null)
                manager.LoadLocalFrames(LocalHeightmapMeta);

            Debug.Log($"OK: {LocalHeightmapPaths.Count} frames. Δt(API)={_dataService.LastFrameIntervalSeconds}s");
            Debug.Log($"CACHE BASE: {_dataService.GetCacheBasePath()}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error en preload: {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
