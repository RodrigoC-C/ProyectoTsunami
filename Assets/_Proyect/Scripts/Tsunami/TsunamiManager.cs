using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TsunamiManager : MonoBehaviour
{
    [Header("Config")]
    public ApiSettings apiSettings;
    public string timesCSV = "300,600,1800"; // puedes dejar vacío y traer últimos N desde la API cambiando el endpoint

    [Header("Refs")]
    public ApiService apiService;          // coloca este componente en un GO “DataSystems”
    public TsunamiVisualizer visualizer;   // coloca este en un GO “TsunamiSystem”

    [Header("State")]
    public bool autoPlay = true;
    public float secondsPerFrame = 2f;

    private FramesPayload _payload;
    private int _frameIndex = 0;
    private bool _isPlaying = false;
    private Coroutine _playCo;

    public event Action<FramesPayload> OnFramesLoaded;
    public event Action<FrameOut, int, int> OnFrameChanged;

    private void Awake()
    {
        if (!apiService) apiService = FindAnyObjectByType<ApiService>();
        if (apiService) apiService.Init(apiSettings);
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
                    // if (autoPlay) Play();  // <- deja esto comentado o asegúrate de tener autoPlay=false
                }
                else
                {
                    Debug.LogWarning("[Tsunami] API ok pero sin frames");
                }
            },
            onError: (err) => Debug.LogError("[Tsunami] " + err)
        );
    }

    private void OnFrameLoadedOrChanged()
    {
        var total = _payload?.frames?.Count ?? 0;
        if (total > 0)
            OnFrameChanged?.Invoke(_payload.frames[_frameIndex], _frameIndex, total);
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
        if (_payload?.frames == null || _payload.frames.Count == 0) return;
        _frameIndex = (_frameIndex + 1) % _payload.frames.Count;
        visualizer.ShowFrame(_payload.frames[_frameIndex]);
        OnFrameLoadedOrChanged();
    }

    public void PrevFrame()
    {
        if (_payload?.frames == null || _payload.frames.Count == 0) return;
        _frameIndex = (_frameIndex - 1 + _payload.frames.Count) % _payload.frames.Count;
        visualizer.ShowFrame(_payload.frames[_frameIndex]);
        OnFrameLoadedOrChanged();
    }

    public void GoToTimeSeconds(int t)
    {
        if (_payload?.frames == null) return;
        int idx = _payload.frames.FindIndex(f => f.t == t);
        if (idx >= 0)
        {
            _frameIndex = idx;
            visualizer.ShowFrame(_payload.frames[_frameIndex]);
        }
    }
}
