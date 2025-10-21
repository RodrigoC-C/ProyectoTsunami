using UnityEngine;
using UnityEngine.Rendering;

public class TsunamiDisplacementController : MonoBehaviour
{
    [Header("Compute Shader")]
    public ComputeShader displacementComputeShader;
    
    [Header("Shader Parameters")]
    public float etaMin = -2f;
    public float etaMax = 2f;
    public float verticalScale = 4f;
    public float baseY = 0f;
    public float frameLerp = 0f;
    public float smoothStrength = 1f;
    
    [Header("Textures")]
    public Texture2DArray etaArray;
    public Vector2 domainSize = new Vector2(256, 256);
    public Vector2 etaTexelSize = new Vector2(1f/256f, 1f/256f);
    
    [Header("Animation")]
    public int frameIndex = 0;
    public int nextFrameIndex = 1;
    public float animationSpeed = 1f;
    
    // Textura de salida
    private RenderTexture _displacementTexture;
    private int _kernelHandle;
    
    // Referencia al material del agua
    public Material waterMaterial;
    
    void Start()
    {
        Initialize();
    }
    
    void Initialize()
    {
        if (displacementComputeShader == null)
        {
            Debug.LogError("Compute Shader no asignado!");
            return;
        }
        
        _kernelHandle = displacementComputeShader.FindKernel("CSMain");
        
        // Crear textura de desplazamiento
        _displacementTexture = new RenderTexture(
            (int)domainSize.x, (int)domainSize.y, 0, 
            RenderTextureFormat.RFloat
        );
        _displacementTexture.enableRandomWrite = true;
        _displacementTexture.Create();
        
        // Asignar al material
        if (waterMaterial != null)
        {
            waterMaterial.SetTexture("_DisplacementTex", _displacementTexture);
        }
    }
    
    void Update()
    {
        if (displacementComputeShader == null || etaArray == null)
            return;
            
        // Actualizar animación
        UpdateAnimation();
        
        // Ejecutar compute shader
        DispatchComputeShader();
    }
    
    void UpdateAnimation()
    {
        // Aquí puedes controlar la animación de frames
        // Por ejemplo, basado en tiempo
        frameLerp += Time.deltaTime * animationSpeed;
        if (frameLerp >= 1f)
        {
            frameLerp = 0f;
            frameIndex = nextFrameIndex;
            nextFrameIndex = (nextFrameIndex + 1) % etaArray.depth;
        }
    }
    
    void DispatchComputeShader()
    {
        // Setear parámetros
        displacementComputeShader.SetFloat("_EtaMin", etaMin);
        displacementComputeShader.SetFloat("_EtaMax", etaMax);
        displacementComputeShader.SetFloat("_VerticalScale", verticalScale);
        displacementComputeShader.SetFloat("_BaseY", baseY);
        displacementComputeShader.SetFloat("_FrameLerp", frameLerp);
        displacementComputeShader.SetFloat("_SmoothStrength", smoothStrength);
        displacementComputeShader.SetVector("_EtaTexelSize", etaTexelSize);
        displacementComputeShader.SetVector("_DomainSize", domainSize);
        displacementComputeShader.SetInt("_FrameIndex", frameIndex);
        displacementComputeShader.SetInt("_NextFrameIndex", nextFrameIndex);
        
        // Setear texturas
        displacementComputeShader.SetTexture(_kernelHandle, "_EtaArray", etaArray);
        displacementComputeShader.SetTexture(_kernelHandle, "_DisplacementOutput", _displacementTexture);
        
        // Calcular grupos de threads
        uint threadGroupSizeX, threadGroupSizeY, threadGroupSizeZ;
        displacementComputeShader.GetKernelThreadGroupSizes(_kernelHandle, 
            out threadGroupSizeX, out threadGroupSizeY, out threadGroupSizeZ);
            
        int groupCountX = Mathf.CeilToInt(domainSize.x / threadGroupSizeX);
        int groupCountY = Mathf.CeilToInt(domainSize.y / threadGroupSizeY);
        
        // Ejecutar
        displacementComputeShader.Dispatch(_kernelHandle, groupCountX, groupCountY, 1);
    }
    
    void OnDestroy()
    {
        if (_displacementTexture != null)
        {
            _displacementTexture.Release();
        }
    }
    
    
}