using System.Collections.Generic;
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

    private async void Start()
    {
        _cts = new CancellationTokenSource();
        _dataService = new DataService(apiBaseUrl) { MaxConcurrentDownloads = maxConcurrentDownloads };

        try
        {
            LocalHeightmapPaths = await _dataService.PreloadHeightmapsAsync(city, scenario, _cts.Token);

            // Ajusta el playback con el intervalo real de tu API:
            var playback = FindObjectOfType<HeightmapPlaybackController>();
            if (playback != null)
            {
                if (_dataService.LastFrameIntervalSeconds > 0)
                    playback.secondsPerFrame = _dataService.LastFrameIntervalSeconds;
                playback.Begin(new List<string>(LocalHeightmapPaths));
            }

            Debug.Log($"OK: {LocalHeightmapPaths.Count} frames. Δt={_dataService.LastFrameIntervalSeconds}s");
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
