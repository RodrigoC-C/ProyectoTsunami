using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Tsunami.Core; // Usa SimTime.FormatAuto(...) para formatear segundos

/// <summary>
/// Timeline maestro: controla play/pause/seek del HeightArrayPlayback,
/// sincroniza Slider/Label y (opcional) notifica a un TsunamiManager.
/// - Acepta tiempos reales por frame (t_sec).
/// - Puede normalizar el primer frame a 00:00.
/// - Puede comprimir toda la duración real a X segundos de reproducción.
/// - Soporta botón/slider UI sin configuración extra del usuario.
/// </summary>
[DisallowMultipleComponent]
public class TsunamiTimelineController : MonoBehaviour
{
    // ---------------- Refs ----------------
    [Header("Referencias")]
    [Tooltip("Componente que mueve el _Slice del shader. Debe tener externalDrive = true.")]
    public HeightArrayPlayback heightArrayPlayback;

    [Tooltip("(Opcional) Si lo asignas, se llamará Manager.GoToIndex(i) cuando cambie el frame.")]
    public TsunamiManager manager; // puedes dejarlo None

    [Tooltip("Slider de la UI para scrubbing de frames.")]
    public Slider slider;

    [Tooltip("Texto TMP para mostrar el tiempo formateado y el índice (i/N).")]
    public TMP_Text timeLabel;

    // -------------- Reproducción --------------
    [Header("Reproducción")]
    public bool loop = true;
    public bool autoplay = false;

    // -------------- Formato de tiempo --------------
    [Header("Tiempo / Etiqueta")]
    [Tooltip("Si es true, el primer frame se muestra como 00:00 (resta el t_sec inicial).")]
    public bool normalizeStartAtZero = true;

    // -------------- Compresión global --------------
    [Header("Compresión de tiempo")]
    [Tooltip("Comprime el tiempo real total (t_sec) a esta duración de reproducción.")]
    public bool compressToTargetDuration = true;

    [Tooltip("Segundos de reproducción deseados para toda la animación (p. ej., 180 = 3 minutos).")]
    [Min(0.01f)] public float targetPlaybackTotalSeconds = 180f;

    // -------------- Estado interno --------------
    // Lista de segundos reales por frame, ORDENADA (uno por cada frame)
    private List<int> _tSecs = new();

    // Duración de reproducción por frame (después de aplicar compresión)
    private List<float> _durations = new();

    // Normalización a 00:00 (si corresponde)
    private int _baseStartSec = 0;

    // Cursor de reproducción
    private int _index = 0;
    private float _accum = 0f;
    private bool _playing = false;

    public int FrameCount => _tSecs?.Count ?? 0;
    public int CurrentIndex => _index;

    // -------------- Lifecycle --------------
    private void Awake()
    {
        if (!heightArrayPlayback) heightArrayPlayback = FindAnyObjectByType<HeightArrayPlayback>();
        if (!manager) manager = FindAnyObjectByType<TsunamiManager>();

        if (slider)
        {
            slider.wholeNumbers = true;
            slider.interactable = false;
            slider.onValueChanged.AddListener(OnSliderChanged);
        }
    }

    private void Start()
    {
        if (autoplay) Play();
    }

    private void Update()
    {
        if (!_playing || FrameCount <= 0) return;

        float frameDur = (_durations.Count == FrameCount && _index < _durations.Count)
            ? _durations[_index]
            : (heightArrayPlayback ? heightArrayPlayback.secondsPerFrame : 0.25f);

        _accum += Time.deltaTime;
        if (_accum >= frameDur)
        {
            _accum = 0f;
            int next = _index + 1;
            if (next >= FrameCount)
            {
                if (loop) next = 0;
                else { Pause(); return; }
            }
            Seek(next);
        }
    }

    // -------------- API pública --------------
    /// <summary>
    /// Configura la línea de tiempo con los segundos reales por frame (t_sec).
    /// Debe venir ORDENADA (posición 0 ↔ frame 1 del array, etc.).
    /// </summary>
    public void ConfigureFromTimes(List<int> tSeconds, float fallbackSecondsPerFrameFromAPI = 0f)
    {
        _tSecs = tSeconds != null ? new List<int>(tSeconds) : new List<int>();

        // Normalizar inicio a 00:00 si corresponde
        _baseStartSec = (normalizeStartAtZero && _tSecs.Count > 0) ? _tSecs[0] : 0;

        // Construir duraciones por frame (con o sin compresión global)
        BuildDurations(fallbackSecondsPerFrameFromAPI);

        // Configurar UI
        if (slider)
        {
            slider.minValue = 0;
            slider.maxValue = Mathf.Max(0, FrameCount - 1);
            slider.SetValueWithoutNotify(0);
            slider.interactable = FrameCount > 1;
        }

        // Llevar todo al primer frame
        Seek(0, notifySlider: false);
    }

    public void Play()  { _playing = true;  heightArrayPlayback?.Play();  }
    public void Pause() { _playing = false; heightArrayPlayback?.Pause(); }
    public void Restart() { Seek(0); Play(); }

    /// <summary>Salta a un frame específico.</summary>
    public void Seek(int index, bool notifySlider = true)
    {
        if (FrameCount == 0) return;

        _index = Mathf.Clamp(index, 0, FrameCount - 1);
        _accum = 0f;

        // Mover shader/malla
        heightArrayPlayback?.Seek(_index);

        // UI
        UpdateLabel();
        if (slider && notifySlider) slider.SetValueWithoutNotify(_index);

        // Notificar al manager (opcional)
        manager?.GoToIndex(_index);
    }

    // -------------- UI callbacks --------------
    private void OnSliderChanged(float value)
    {
        Seek(Mathf.RoundToInt(value), notifySlider: false);
    }

    // -------------- Helpers --------------
    private void BuildDurations(float fallbackSpf)
    {
        _durations.Clear();

        if (_tSecs == null || _tSecs.Count == 0)
        {
            if (fallbackSpf > 0f) _durations.Add(fallbackSpf);
            return;
        }

        if (_tSecs.Count == 1)
        {
            _durations.Add(fallbackSpf > 0f
                ? fallbackSpf
                : (heightArrayPlayback ? heightArrayPlayback.secondsPerFrame : 0.25f));
            return;
        }

        // Diferencias reales entre frames (en segundos reales)
        var diffs = new List<float>(_tSecs.Count - 1);
        for (int i = 0; i < _tSecs.Count - 1; i++)
            diffs.Add(Mathf.Max(0.01f, _tSecs[i + 1] - _tSecs[i]));

        // Duración real total (segundos) del rango representado
        float realTotal = Mathf.Max(0.01f, _tSecs[_tSecs.Count - 1] - _tSecs[0]);

        // Factor de compresión global (si corresponde)
        float scale = 1f;
        if (compressToTargetDuration && targetPlaybackTotalSeconds > 0f && realTotal > 0.01f)
            scale = targetPlaybackTotalSeconds / realTotal;

        // Opcional: pequeña protección contra saltos gigantes en reproducción
        // (no afecta al tiempo que se muestra en pantalla)
        var sorted = new List<float>(diffs);
        sorted.Sort();
        float median = sorted[sorted.Count / 2];
        float maxCap = Mathf.Max(median * 2f * scale, 0.02f);

        for (int i = 0; i < _tSecs.Count; i++)
        {
            if (i == _tSecs.Count - 1)
            {
                float last = diffs.Count > 0 ? Mathf.Min(diffs[diffs.Count - 1] * scale, maxCap)
                                             : (fallbackSpf > 0 ? fallbackSpf : 0.25f);
                _durations.Add(Mathf.Max(0.01f, last));
            }
            else
            {
                float v = Mathf.Min(diffs[i] * scale, maxCap);
                _durations.Add(Mathf.Max(0.01f, v));
            }
        }
    }

    private void UpdateLabel()
    {
        if (!timeLabel) return;

        // Tiempo absoluto del frame actual (tal cual lo da la API)
        int tsecsAbs = (_tSecs != null && _index >= 0 && _index < _tSecs.Count) ? _tSecs[_index] : 0;

        // Tiempo relativo (normalizado) si normalizeStartAtZero = true
        int tsecsRel = Mathf.Max(0, tsecsAbs - _baseStartSec);

        // Formato automático (mm:ss, hh:mm:ss o d:hh:mm:ss)
        string pretty = SimTime.FormatAuto(tsecsRel);

        timeLabel.text = $"{pretty}  ({_index + 1}/{FrameCount})";
    }
}
