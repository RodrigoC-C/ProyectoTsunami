using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;

[ExecuteAlways]
public class WaterMaskController : MonoBehaviour
{
    [Header("Refs")]
    public Material waterMaterial;          // URP_TsunamiWater
    public Transform waterPlane;            // <- tu OceanSurface
    public CesiumGeoreference georeference;

    [Header("Mask")]
    public int maskResolution = 1024;

    [Header("Brush / Frente")]
    public float brushRadiusMeters = 200f;
    public float frontSpeedMS = 100f;
    public float softEdgeMeters = 40f;

    RenderTexture _rt;
    Material _blitMat;
    Mesh _planeMesh;
    Vector3 _planeScale;
    Vector3 _meshCenter;
    Vector3 _meshSize; // en unidades de MESH (default plane ~10x10)

    static readonly int ID_FloodMask = Shader.PropertyToID("_FloodMask");
    static readonly int ID_PlaneCenterWS = Shader.PropertyToID("_PlaneCenterWS");
    static readonly int ID_PlaneRightWS = Shader.PropertyToID("_PlaneRightWS");
    static readonly int ID_PlaneForwardWS = Shader.PropertyToID("_PlaneForwardWS");
    static readonly int ID_PlaneHalfSize = Shader.PropertyToID("_PlaneHalfSize");

    struct TargetStroke { public Vector3 targetWorld; public float startTime; }
    readonly List<TargetStroke> _strokes = new();

    void Awake()
    {
        if (!_blitMat) _blitMat = new Material(Shader.Find("Hidden/BlitDrawCircle"));
        EnsureRt();
        CachePlaneData();
        if (waterMaterial) waterMaterial.SetTexture("_FloodMask", _rt);
    }

    void OnValidate()
    {
        EnsureRt();
        CachePlaneData();
        if (waterMaterial) waterMaterial.SetTexture("_FloodMask", _rt);
    }

    void CachePlaneData()
    {
        if (!waterPlane) return;
        var mf = waterPlane.GetComponent<MeshFilter>();
        _planeMesh = mf ? mf.sharedMesh : null;
        if (_planeMesh != null)
        {
            _meshCenter = _planeMesh.bounds.center; // normalmente (0,0,0)
            _meshSize = _planeMesh.bounds.size;   // normalmente (~10,~,10)
        }
        _planeScale = waterPlane.lossyScale;        // para pasar de metros a UV
    }

    void EnsureRt()
    {
        if (_rt != null && (_rt.width != maskResolution || _rt.height != maskResolution))
        {
            _rt.Release(); DestroyImmediate(_rt); _rt = null;
        }
        if (_rt == null)
        {
            _rt = new RenderTexture(maskResolution, maskResolution, 0, RenderTextureFormat.R8)
            { name = "FloodMaskRT", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            ClearMask();
        }
    }

    public void ClearMask()
    {
        var prev = RenderTexture.active;
        RenderTexture.active = _rt;
        GL.Clear(false, true, Color.black);
        RenderTexture.active = prev;
    }

    // Llamado desde tu UI/manager cuando cambia de frame
    public void PaintTowardFrame(FrameOut frame, Vector3 /*ignored*/ _)
    {
        _strokes.Clear();
        float now = Application.isPlaying ? Time.time : 0f;
        if (frame?.points == null) return;

        foreach (var p in frame.points)
        {
            var ecef = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                new Unity.Mathematics.double3(p.lon, p.lat, p.h));
            var u = georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            _strokes.Add(new TargetStroke { targetWorld = new Vector3((float)u.x, (float)u.y, (float)u.z), startTime = now });
        }
    }

    // --- proyección mundo→UV del plano ---
    bool WorldToUV(Vector3 world, out Vector2 uv)
    {
        uv = Vector2.zero;
        if (!_planeMesh || !waterPlane) return false;

        // punto en espacio local del plano
        Vector3 local = waterPlane.InverseTransformPoint(world);

        // ajustar por centro del mesh (normalmente 0)
        float lx = local.x - _meshCenter.x;
        float lz = local.z - _meshCenter.z;

        // tamaño del plano en WORLD (metros): sizeLocal * lossyScale
        float worldW = _meshSize.x * _planeScale.x;
        float worldH = _meshSize.z * _planeScale.z;

        // mapear -W/2..+W/2  ->  0..1
        uv.x = (lx / (_meshSize.x * 0.5f)) * 0.5f + 0.5f;
        uv.y = (lz / (_meshSize.z * 0.5f)) * 0.5f + 0.5f;

        // descarta si cae fuera del plano
        return (uv.x >= -0.1f && uv.x <= 1.1f && uv.y >= -0.1f && uv.y <= 1.1f);
    }

    // Llama esto en Awake/OnValidate y cuando muevas/escales el plano:
    void PushPlaneToMaterial()
    {
        if (!waterMaterial || !waterPlane) return;

        // Ejes del plano en mundo
        Vector3 right = waterPlane.right;
        Vector3 fwd = waterPlane.forward;

        // Tamaño del plano en MUNDO (a partir del mesh * lossyScale)
        var mf = waterPlane.GetComponent<MeshFilter>();
        var mesh = mf ? mf.sharedMesh : null;
        if (!mesh) return;
        var sizeLocal = mesh.bounds.size;    // p.ej. ~10x10 en el Plane de Unity
        Vector3 halfSizeWorld = new Vector3(
            0.5f * sizeLocal.x * waterPlane.lossyScale.x,
            0f,
            0.5f * sizeLocal.z * waterPlane.lossyScale.z
        );

        waterMaterial.SetTexture(ID_FloodMask, _rt);
        waterMaterial.SetVector(ID_PlaneCenterWS, waterPlane.position);
        waterMaterial.SetVector(ID_PlaneRightWS, right);
        waterMaterial.SetVector(ID_PlaneForwardWS, fwd);
        waterMaterial.SetVector(ID_PlaneHalfSize, halfSizeWorld);
    }

    bool WorldToPlaneUV(Vector3 world, out Vector2 uv)
    {
        uv = Vector2.zero;
        if (!waterPlane) return false;

        // Debe coincidir con el shader:
        Vector3 to = world - waterPlane.position;
        var mf = waterPlane.GetComponent<MeshFilter>();
        var mesh = mf ? mf.sharedMesh : null;
        if (!mesh) return false;

        Vector3 sizeLocal = mesh.bounds.size;
        float halfW = 0.5f * sizeLocal.x * waterPlane.lossyScale.x;
        float halfH = 0.5f * sizeLocal.z * waterPlane.lossyScale.z;

        float u = Vector3.Dot(to, waterPlane.right) / Mathf.Max(1e-4f, halfW) * 0.5f + 0.5f;
        float v = Vector3.Dot(to, waterPlane.forward) / Mathf.Max(1e-4f, halfH) * 0.5f + 0.5f;

        uv = new Vector2(u, v);
        return (u >= -0.1f && u <= 1.1f && v >= -0.1f && v <= 1.1f);
    }

    void Update()
    {
        if (_strokes.Count == 0 || _rt == null || _blitMat == null || !_planeMesh || !waterPlane) return;

        // tamaños en WORLD para convertir metros -> UV
        float worldW = _meshSize.x * _planeScale.x;
        float worldH = _meshSize.z * _planeScale.z;
        float now = Application.isPlaying ? Time.time : 0f;

        // --- ping-pong ---
        var tmp = RenderTexture.GetTemporary(_rt.descriptor);
        Graphics.Blit(_rt, tmp);
        RenderTexture src = tmp, dst = _rt;

        foreach (var s in _strokes)
        {
            // avance radial en metros
            float t = Mathf.Max(0f, now - s.startTime);
            float radius = frontSpeedMS * t;

            // centro = target empujado hacia atrás radius metros (dirección “desde mar”)
            // Como ya no usamos DeepOrigin, tomamos el **centroide** del plano como “mar afuera”.
            Vector3 planeCenterWS = waterPlane.position;
            Vector3 dir = (s.targetWorld - planeCenterWS); dir.y = 0;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;

            Vector3 centerWS = s.targetWorld - dir * radius;

            if (!WorldToUV(centerWS, out Vector2 centerUV)) continue;

            // radios en UV aprox (isotrópico con worldW/H)
            float rU = radius / Mathf.Max(1e-4f, worldW);
            float rV = radius / Mathf.Max(1e-4f, worldH);
            float radiusUV = 0.5f * (rU + rV); // media simple
            float softUV = 0.5f * (softEdgeMeters / worldW + softEdgeMeters / worldH);

            _blitMat.SetVector("_Circle", new Vector4(centerUV.x, centerUV.y, radiusUV, softUV));
            Graphics.Blit(src, dst, _blitMat);
            (src, dst) = (dst, src);
        }

        if (src != _rt) Graphics.Blit(src, _rt);
        RenderTexture.ReleaseTemporary(tmp);
    }
}

