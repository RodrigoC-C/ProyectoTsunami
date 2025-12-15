using UnityEngine;

[ExecuteAlways]
public class HeightArrayPlayback : MonoBehaviour
{
    [Header("Material / Malla")]
    public MeshRenderer targetRenderer;
    public string heightArrayName = "_HeightArray";
    public string slicePropName   = "_Slice";
    public Texture2DArray heightArray;

    [Header("Timeline (auto)")]
    public float secondsPerFrame = 0.25f;
    public bool loop = true;
    [Range(0, 255)] public int debugSlice = 0;

    [Header("Control")]
    [Tooltip("Si es true, NO avanza en Update: el timeline externo manda.")]
    public bool externalDrive = true;

    [Header("Displacement (Deformación)")]
    public float dispScaleMeters = 10f;
    public bool flipV = true;
    public bool rotate90 = false;

    [Header("Inundation 'Patch'")]
    [Tooltip("Cuántos metros sube el NIVEL DEL MAR por cada frame de la animación.")]
    public float riseAmountPerFrame = 10f;
    [Tooltip("El frame en el que la subida del mar debe empezar.")]
    public int riseStartFrame = 5;

    float t;
    int cursor;
    Material _mat;
    float initialYPosition;
    bool _isPlaying;

    public int FrameCount => heightArray ? heightArray.depth : 0;
    public int CurrentIndex => Mathf.Clamp(cursor, 0, Mathf.Max(0, FrameCount - 1));
    public System.Action<int> OnSliceChanged;

    void Start() => initialYPosition = transform.localPosition.y;

    void OnEnable()
    {
        if (!targetRenderer) targetRenderer = GetComponentInChildren<MeshRenderer>();
        _mat = targetRenderer ? targetRenderer.sharedMaterial : null;
        if (Mathf.Approximately(initialYPosition, 0f))
            initialYPosition = transform.localPosition.y;

        ApplyStaticParams();
        ApplySlice(0);
    }

    void ApplyStaticParams()
    {
        if (_mat == null) return;
        if (heightArray) _mat.SetTexture(heightArrayName, heightArray);
        _mat.SetFloat("_DispScale", dispScaleMeters);
        _mat.SetFloat("_FlipV", flipV ? 1f : 0f);
        _mat.SetFloat("_Rotate90", rotate90 ? 1f : 0f);
    }

    void Update()
    {
        if (_mat == null || heightArray == null) return;

        if (Application.isPlaying)
        {
            if (!externalDrive && _isPlaying && FrameCount > 0)
            {
                t += Time.deltaTime;
                if (t >= secondsPerFrame)
                {
                    t = 0f;
                    cursor++;
                    if (cursor >= FrameCount) cursor = loop ? 0 : FrameCount - 1;
                    ApplySlice(cursor);
                }
            }
        }
        else
        {
            debugSlice = Mathf.Clamp(debugSlice, 0, Mathf.Max(0, FrameCount - 1));
            ApplySlice(debugSlice);
            ApplyStaticParams();
        }
    }

    void ApplySlice(int s)
    {
        if (_mat == null) return;

        _mat.SetFloat(slicePropName, (float)s);

        float riseOffset = 0.0f;
        if (s >= riseStartFrame)
            riseOffset = (s - riseStartFrame) * riseAmountPerFrame;

        var p = transform.localPosition;
        //transform.localPosition = new Vector3(p.x, initialYPosition + riseOffset, p.z);

        #if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
        #endif

        OnSliceChanged?.Invoke(CurrentIndex);
    }

    // ==== API para control externo ====
    public void Play()  { _isPlaying = true; }
    public void Pause() { _isPlaying = false; }
    public void Stop()  { _isPlaying = false; Seek(0); }

    public void Seek(int index)
    {
        if (FrameCount == 0) return;
        cursor = Mathf.Clamp(index, 0, FrameCount - 1);
        t = 0f;
        ApplySlice(cursor);
    }

    public void SetSecondsPerFrame(float spf) => secondsPerFrame = Mathf.Max(0.001f, spf);

    // ==== Aliases/compatibilidad para HUD ====
    public bool ExternalDrive
    {
        get => externalDrive;
        set => externalDrive = value;
    }

    public bool IsPlaying => _isPlaying;

    // El HUD espera este nombre
    public int CurrentFrame => CurrentIndex;

    // Tiempo “corriente” según frame + fracción acumulada
    public float CurrentTimeSeconds => (cursor * secondsPerFrame) + t;

    // Alias de Seek
    public void SetFrame(int frame) => Seek(frame);

    // Opcional: control tipo “toggle”
    public void SetPlaying(bool play)
    {
        if (play) Play();
        else      Pause();
    }

    // (opcional) Si algún loader quiere inyectar el array después de crearlo
    public void SetHeightArray(Texture2DArray arr)
    {
        heightArray = arr;
        if (_mat != null && heightArray != null)
            _mat.SetTexture(heightArrayName, heightArray);
        cursor = 0;
        ApplySlice(0);
    }
    public void Rebind(Texture2DArray arr)
    {
        heightArray = arr;
        if (!targetRenderer) targetRenderer = GetComponentInChildren<MeshRenderer>();
        _mat = targetRenderer ? targetRenderer.sharedMaterial : null;
        ApplyStaticParams();
        ApplySlice(0);
    }
}
