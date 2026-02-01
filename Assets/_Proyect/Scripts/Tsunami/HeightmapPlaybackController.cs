using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Reproduce una secuencia de heightmaps (PNG) aplicándolos como textura de desplazamiento
/// en el material del agua. Compatible con el shader "Tsunami/WaterDisplaceURP".
/// </summary>
public class HeightmapPlaybackController : MonoBehaviour
{
    [Header("Material / Malla de agua")]
    [Tooltip("Renderer del mesh de agua (ej. el MeshRenderer de tu WaterMesh).")]
    public Renderer waterRenderer;

    [Tooltip("Nombre del sampler de displacement en el shader.")]
    public string displacementTexName = "_DispTex";

    [Header("Timeline")]
    [Tooltip("Segundos que dura cada frame (puedes sobreescribirlo con el valor de la API).")]
    public float secondsPerFrame = 0.5f;

    [Tooltip("Repetir al terminar.")]
    public bool loop = true;

    [Header("UV / Escala de desplazamiento")]
    public Vector2 uvScale = Vector2.one;
    public Vector2 uvOffset = Vector2.zero;

    [Tooltip("Escala de desplazamiento en metros (propiedad _DispScale del shader).")]
    public float dispScaleMeters = 2f;

    // Rutas locales de los PNG, ordenadas por índice.
    private List<string> _paths = new List<string>();

    private Material _mat;
    private Texture2D _tex;               // reciclada para no alocar en cada frame
    private int _dispId;
    private int _uvScaleId;
    private int _uvOffsetId;
    private int _dispScaleId;

    private Coroutine _playCo;
    private int _cursor;                  // frame actual
    private List<int> _tSecs = new List<int>();

    // Eventos para suscriptores: index actual y tiempo en segundos (t_sec)
    public event System.Action<int, int> OnFrameChanged; // (index, t_sec)

    private void Awake()
    {
        if (waterRenderer == null)
        {
            Debug.LogWarning("[HeightmapPlaybackController] Asigna el Renderer del agua.");
        }

        _mat = waterRenderer != null ? waterRenderer.material : null;
        _dispId = Shader.PropertyToID(displacementTexName);
        _uvScaleId = Shader.PropertyToID("_UVScale");
        _uvOffsetId = Shader.PropertyToID("_UVOffset");
        _dispScaleId = Shader.PropertyToID("_DispScale");
    }

    private void Start()
    {
        if (_mat != null)
        {
            _mat.SetVector(_uvScaleId, new Vector4(uvScale.x, uvScale.y, 0, 0));
            _mat.SetVector(_uvOffsetId, new Vector4(uvOffset.x, uvOffset.y, 0, 0));
            _mat.SetFloat(_dispScaleId, dispScaleMeters);
        }
    }

    /// <summary>
    /// Inicia la reproducción con esta lista de archivos locales.
    /// </summary>
    public void Begin(List<string> localHeightmapPaths)
    {
        if (localHeightmapPaths == null || localHeightmapPaths.Count == 0)
        {
            Debug.LogWarning("[HeightmapPlaybackController] Lista vacía.");
            return;
        }

        _paths = new List<string>(localHeightmapPaths);
        _cursor = 0;
    _tSecs = new List<int>(_paths.Count); // se podrá rellenar por TsunamiManager cuando sea necesario

        if (_playCo != null) StopCoroutine(_playCo);
        _playCo = StartCoroutine(PlayRoutine());
    }

    public void SetFrameTimes(List<int> tsecs)
    {
        _tSecs = tsecs ?? new List<int>(_paths.Count);
    }

    public void Play()
    {
        if (_playCo == null) _playCo = StartCoroutine(PlayRoutine());
    }

    public void Pause()
    {
        if (_playCo != null)
        {
            StopCoroutine(_playCo);
            _playCo = null;
        }
    }

    public void ResetToStart()
    {
        _cursor = 0;
        ApplyCurrent();
    }

    public void Next()
    {
        if (_paths == null || _paths.Count == 0) return;
        _cursor = Mathf.Min(_cursor + 1, _paths.Count - 1);
        ApplyCurrent();
    }

    public void Prev()
    {
        if (_paths == null || _paths.Count == 0) return;
        _cursor = Mathf.Max(_cursor - 1, 0);
        ApplyCurrent();
    }

    /// <summary>
    /// Salta al índice exacto (si está en rango) y actualiza la textura.
    /// </summary>
    public void Seek(int frameIndex)
    {
        if (_paths == null || _paths.Count == 0) return;
        _cursor = Mathf.Clamp(frameIndex, 0, _paths.Count - 1);
        ApplyCurrent();
    }

    /// <summary>
    /// Cambia la velocidad: segundos reales por frame.
    /// </summary>
    public void SetSecondsPerFrame(float seconds)
    {
        secondsPerFrame = Mathf.Max(0.001f, seconds);
    }

    private IEnumerator PlayRoutine()
    {
        var wait = new WaitForSeconds(Mathf.Max(0.001f, secondsPerFrame));

        while (true)
        {
            ApplyCurrent();

            _cursor++;
            if (_cursor >= _paths.Count)
            {
                if (!loop) yield break;
                _cursor = 0;
            }

            // Si cambias secondsPerFrame en runtime (p. ej. con el valor de la API), respétalo:
            wait = new WaitForSeconds(Mathf.Max(0.001f, secondsPerFrame));
            yield return wait;
        }
    }

    private void ApplyCurrent()
    {
        if (_mat == null || _paths == null || _paths.Count == 0) return;

        var path = _paths[_cursor];
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[HeightmapPlaybackController] Frame no encontrado: {path}");
            return;
        }

        var bytes = File.ReadAllBytes(path);
        if (_tex == null) _tex = new Texture2D(2, 2, TextureFormat.R8, false, true); // R8 suficiente para displacements normalizados
        _tex.LoadImage(bytes, false);
        _mat.SetTexture(_dispId, _tex);

        var tse = (_tSecs != null && _cursor >= 0 && _cursor < _tSecs.Count) ? _tSecs[_cursor] : 0;
        OnFrameChanged?.Invoke(_cursor, tse);
    }
}
