using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TsunamiManager : MonoBehaviour
{
    [Header("Config")]
    public ApiSettings apiSettings;
    public string timesCSV = "300,600,1800"; // puedes dejar vac�o y traer �ltimos N desde la API cambiando el endpoint

    [Header("Refs")]
    public ApiService apiService;          // coloca este componente en un GO �DataSystems�
    public TsunamiVisualizer visualizer;   // coloca este en un GO �TsunamiSystem�

    [Header("State")]
    public bool autoPlay = false;
    public float secondsPerFrame = 2f;

    private FramesPayload _payload;
    private List<Tsunami.Services.DataService.FrameMeta> _localFrames;
    private int _frameIndex = 0;
    private bool _isPlaying = false;
    private Coroutine _playCo;

    public event Action<FramesPayload> OnFramesLoaded;
    public event Action<FrameOut, int, int> OnFrameChanged;
    public WaterMeshClipper waterClipper;
    public HeightmapPlaybackController playbackController;

    private void Awake()
    {
        if (!apiService) apiService = FindAnyObjectByType<ApiService>();
        if (apiService) apiService.Init(apiSettings);
        if (!playbackController) playbackController = FindAnyObjectByType<HeightmapPlaybackController>();
        if (playbackController != null)
        {
            playbackController.OnFrameChanged += HandlePlaybackFrameChanged;
        }
    }

    private void Start()
    {
        StartCoroutine(LoadFrames());
    }

    private IEnumerator LoadFrames()
    {
        yield return apiService.GetFramesStructured(
            string.IsNullOrWhiteSpace(timesCSV) ? "300,600,1800" : timesCSV,
            onSuccess: (payload) =>
            {
                _payload = payload;
                _frameIndex = 0;

                if (_payload.frames != null && _payload.frames.Count > 0)
                {
                    visualizer.ShowFrame(_payload.frames[_frameIndex]);
                    OnFrameLoadedOrChanged();
                    OnFramesLoaded?.Invoke(_payload);

                    // Importante: SIN autoplay por tu requerimiento
                    // if (autoPlay) Play();  // <- deja esto comentado o aseg�rate de tener autoPlay=false
                }
                else
                {
                    Debug.LogWarning("[Tsunami] API ok pero sin frames");
                }
            },
            onError: (err) => Debug.LogError("[Tsunami] " + err)
        );
    }

    // ------------------ Nuevo: cargar frames locales ya descargados (rutas + t_sec)
    public void LoadLocalFrames(List<Tsunami.Services.DataService.FrameMeta> localFrames)
    {
        if (localFrames == null || localFrames.Count == 0) return;
        _localFrames = new List<Tsunami.Services.DataService.FrameMeta>(localFrames);

        // Informar al playback controller
        var paths = _localFrames.Select(f => f.LocalPath).ToList();
        var tsecs = _localFrames.Select(f => f.TSec).ToList();
        playbackController?.SetSecondsPerFrame(secondsPerFrame);
        playbackController?.Begin(paths);
        playbackController?.SetFrameTimes(tsecs);

        // Mostrar primer frame en el visualizer
        // intentar mapear el FrameOut equivalente si tenemos payload
        if (_payload != null && _payload.frames != null && _payload.frames.Count > 0)
        {
            var maybe = _payload.frames[0];
            visualizer.ShowFrame(maybe);
        }
    }

    private void OnFrameLoadedOrChanged()
    {
        var total = _payload?.frames?.Count ?? 0;
        if (total > 0)
        {
            var f = _payload.frames[_frameIndex];
            OnFrameChanged?.Invoke(f, _frameIndex, total);

            // --- NUEVO: reconstruir malla de agua recortada para este frame ---
            if (waterClipper != null)
            {
                waterClipper.RebuildForFrame(f);
                Debug.Log($"[Tsunami] Clip frame #{_frameIndex} '{f.label}'");
            }
        }
    }

    public void GoToIndex(int index)
    {
        if (_payload?.frames == null || _payload.frames.Count == 0) return;
        _frameIndex = Mathf.Clamp(index, 0, _payload.frames.Count - 1);
        visualizer.ShowFrame(_payload.frames[_frameIndex]);
        OnFrameLoadedOrChanged();
    }

    public void NextFrame()
    {
        if (_localFrames != null && _localFrames.Count > 0)
        {
            _frameIndex = (_frameIndex + 1) % _localFrames.Count;
            // trigger playback
            playbackController?.Next();
            OnFrameLoadedOrChanged();
            return;
        }
        if (_payload?.frames == null || _payload.frames.Count == 0) return;
        _frameIndex = (_frameIndex + 1) % _payload.frames.Count;
        visualizer.ShowFrame(_payload.frames[_frameIndex]);
        OnFrameLoadedOrChanged();
    }

    public void PrevFrame()
    {
        if (_localFrames != null && _localFrames.Count > 0)
        {
            _frameIndex = (_frameIndex - 1 + _localFrames.Count) % _localFrames.Count;
            playbackController?.Prev();
            OnFrameLoadedOrChanged();
            return;
        }
        if (_payload?.frames == null || _payload.frames.Count == 0) return;
        _frameIndex = (_frameIndex - 1 + _payload.frames.Count) % _payload.frames.Count;
        visualizer.ShowFrame(_payload.frames[_frameIndex]);
        OnFrameLoadedOrChanged();
    }

    public void GoToTimeSeconds(int t)
    {
        if (_localFrames != null && _localFrames.Count > 0)
        {
            int idx = _localFrames.FindIndex(f => f.TSec == t);
            if (idx >= 0)
            {
                _frameIndex = idx;
                playbackController?.Seek(idx);
            }
            return;
        }
        if (_payload?.frames == null) return;
        int idx2 = _payload.frames.FindIndex(f => f.t == t);
        if (idx2 >= 0)
        {
            _frameIndex = idx2;
            visualizer.ShowFrame(_payload.frames[_frameIndex]);
        }
    }

    private void HandlePlaybackFrameChanged(int index, int tsec)
    {
        // Mapear el index a FrameOut si hay payload
        if (_payload?.frames != null && index >= 0 && index < _payload.frames.Count)
        {
            var f = _payload.frames[index];
            OnFrameChanged?.Invoke(f, index, _payload.frames.Count);
            visualizer.ShowFrame(f);
        }
        else if (_localFrames != null && index >= 0 && index < _localFrames.Count)
        {
            // Crear un FrameOut temporal con label y t
            var meta = _localFrames[index];
            var fo = new FrameOut { label = $"frame_{meta.Index}", t = meta.TSec, points = new System.Collections.Generic.List<PointOut>() };
            OnFrameChanged?.Invoke(fo, index, _localFrames.Count);
            visualizer.ShowFrame(fo);
        }
    }
}
