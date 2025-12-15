using UnityEngine;

public class TsunamiFramePlayer : MonoBehaviour
{
    public Renderer targetRenderer;                 // ← el PLANE
    public string textureProperty = "_BaseMap";     // o "_MainTex" o tu nombre

    public string resourcesFolder = "TsunamiFrames";
    public string prefix = "heightmap_";
    public int frameCount = 181;
    public int digits = 4;

    public float secondsPerFrame = 0.1f;
    public bool loop = true;

    Texture2D[] frames;
    float timer;
    int current;

    void Start()
    {
        frames = new Texture2D[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            string idx = (i + 1).ToString().PadLeft(digits, '0');
            string fullName = resourcesFolder + "/" + prefix + idx;
            frames[i] = Resources.Load<Texture2D>(fullName);
        }
        ApplyFrame(0);
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= secondsPerFrame)
        {
            timer = 0f;
            current++;
            if (current >= frameCount)
                current = loop ? 0 : frameCount - 1;
            ApplyFrame(current);
        }
    }

    void ApplyFrame(int i)
    {
        if (targetRenderer == null) return;
        var tex = frames[i];
        if (tex == null) return;
        targetRenderer.material.SetTexture(textureProperty, tex);
    }
}
