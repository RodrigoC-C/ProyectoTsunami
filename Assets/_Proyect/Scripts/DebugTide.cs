using UnityEngine;

public class DebugTide : MonoBehaviour
{
    public TsunamiController tsunami;
    public float amplitudeMeters = 5f;
    public float periodSeconds = 4f;

    void Update()
    {
        if (tsunami == null) return;
        float w = (Mathf.PI * 2f) / Mathf.Max(0.1f, periodSeconds);
        tsunami.seaLevelOffsetMeters = amplitudeMeters * Mathf.Sin(Time.time * w);
    }
}

