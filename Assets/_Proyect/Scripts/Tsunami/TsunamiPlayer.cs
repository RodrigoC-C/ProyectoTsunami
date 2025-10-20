using UnityEngine;

[ExecuteAlways]
public class TsunamiPlayer : MonoBehaviour
{
    public Material WaterMaterial;
    public int FrameCount = 41;
    public float Fps = 4f;
    public bool Loop = true;

    float startTime;
    int lastFrame;

    void OnEnable() => startTime = Time.time;

    void Update()
    {
        if (!WaterMaterial || FrameCount <= 1) return;

        float t = (Application.isPlaying ? Time.time - startTime : Time.realtimeSinceStartup) * Mathf.Max(0.01f, Fps);
        float f = Loop ? Mathf.Repeat(t, FrameCount) : Mathf.Clamp(t, 0, FrameCount - 1);

        int frame = Mathf.FloorToInt(f);
        int next  = (frame + 1) % FrameCount;
        float blend = f - frame;

        // ints para Sample de array, y blend para mezclar en el shader
        WaterMaterial.SetInt("_Frame", frame);
        WaterMaterial.SetInt("_NextFrame", next);
        WaterMaterial.SetFloat("_Blend", blend);
    }
}
