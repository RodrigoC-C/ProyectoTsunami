using UnityEngine;

[ExecuteAlways]
public class HeightArrayPlayback : MonoBehaviour
{
    [Header("Material / Malla")]
    public MeshRenderer targetRenderer;
    public string heightArrayName = "_HeightArray";
    public string slicePropName   = "_Slice";
    public Texture2DArray heightArray;

    [Header("Timeline")]
    public float secondsPerFrame = 0.25f;
    public bool loop = true;
    [Range(0, 255)] public int debugSlice = 0; // scrubber en modo Editor

    [Header("Displacement (Deformación)")]
    public float dispScaleMeters = 10f; // La altura de la OLA (ej. 10m)
    public bool flipV = true;
    public bool rotate90 = false;
    
    // =================================================================
    // ¡LA MAGIA ESTÁ AQUÍ!
    [Header("Inundation 'Patch' (Inundación)")]
    [Tooltip("Cuántos metros sube el NIVEL DEL MAR por cada frame de la animación.")]
    public float riseAmountPerFrame = 10f; // Sube 10cm por frame
    
    [Tooltip("El frame en el que la subida del mar debe empezar.")]
    public int riseStartFrame = 5; // No subas el mar hasta que la ola se acerque
    // =================================================================

    float t; 
    int cursor; 
    Material _mat;
    private float initialYPosition; // La posición Y original del plano

    void Start()
    {
        // Guardamos la altura inicial del plano (la del nivel del mar)
        initialYPosition = transform.localPosition.y;
    }

    void OnEnable()
    {
        if (!targetRenderer) targetRenderer = GetComponentInChildren<MeshRenderer>();
        _mat = targetRenderer ? targetRenderer.sharedMaterial : null;
        
        // Guardar la altura inicial si OnEnable se llama primero
        if (initialYPosition == 0.0f) 
        {
            initialYPosition = transform.localPosition.y;
        }

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
            t += Time.deltaTime;
            if (t >= secondsPerFrame)
            {
                t = 0f;
                cursor++;
                if (cursor >= heightArray.depth) cursor = loop ? 0 : heightArray.depth - 1;
                ApplySlice(cursor);
            }
        }
        else
        {
            debugSlice = Mathf.Clamp(debugSlice, 0, heightArray.depth - 1);
            ApplySlice(debugSlice);
            ApplyStaticParams();
        }
    }

    void ApplySlice(int s)
    {
        // 1. Actualiza el FRAME (la forma de la ola) en el shader
        _mat.SetFloat(slicePropName, (float)s);

        // 2. ¡EL PARCHE! Calcula la nueva altura del NIVEL DEL MAR
        float riseOffset = 0.0f;
        if (s >= riseStartFrame)
        {
            riseOffset = (s - riseStartFrame) * riseAmountPerFrame;
        }
        float newY = initialYPosition + riseOffset;

        // 3. Aplica la nueva altura vertical a TODO el plano
        transform.localPosition = new Vector3(transform.localPosition.x, newY, transform.localPosition.z);

        #if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
        #endif
    }
}