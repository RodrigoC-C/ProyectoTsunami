using UnityEngine;

[ExecuteAlways]
public class HeightArrayPlayback1 : MonoBehaviour
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

    [Header("Displacement")]
    public float dispScaleMeters = 10f;
    public bool flipV = true;
    public bool rotate90 = false;

    float t; int cursor; Material _mat;

    void OnEnable()
    {
        if (!targetRenderer) targetRenderer = GetComponentInChildren<MeshRenderer>();
        _mat = targetRenderer ? targetRenderer.sharedMaterial : null;
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
        _mat.SetFloat(slicePropName, s);
#if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
#endif
    }
}
