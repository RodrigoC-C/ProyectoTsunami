using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class TideApiDebug : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] Transform waterPlane;

    [Header("API")]
    [SerializeField] string baseUrl = "http://localhost:8000";
    [SerializeField] double lat = -23.1;
    [SerializeField] double lon = -70.45;

    [Header("Timing")]
    [SerializeField] float pollSeconds = 2f;
    [SerializeField] float smoothSeconds = 1.5f;

    [Header("Fallback visible si no hay datos")]
    [SerializeField] bool useFallbackWhenFail = true;
    [SerializeField] float fallbackAmplitude = 1.0f;
    [SerializeField] float fallbackPeriod = 6f;

    Vector3 basePos;
    float current = 0f, target = 0f, lerpT = 0f;
    string lastStatus = "init";

    void Start()
    {
        if (!waterPlane) waterPlane = transform;
        basePos = waterPlane.position;
        StartCoroutine(PollLoop());
    }

    IEnumerator PollLoop()
    {
        while (true)
        {
            string tIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            string url = $"{baseUrl}/tide/value?lat={lat}&lon={lon}&t={UnityWebRequest.EscapeURL(tIso)}";
            Debug.Log($"[Tide] GET {url}");

            using (var req = UnityWebRequest.Get(url))
            {
                var t0 = Time.realtimeSinceStartup;
                yield return req.SendWebRequest();
                var ms = (Time.realtimeSinceStartup - t0) * 1000f;

                long code = 0;
                string raw = "";
#if UNITY_2020_2_OR_NEWER
                code = req.responseCode;
#endif
                if (req.downloadHandler != null && req.downloadHandler.data != null)
                    raw = System.Text.Encoding.UTF8.GetString(req.downloadHandler.data).Trim();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[Tide] HTTP {code} in {ms:F0} ms. RAW='{raw}'");
                    // 1) Intento parsear "texto plano" 0.52
                    if (float.TryParse(raw, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var val))
                    {
                        target = val; lerpT = 0f;
                        lastStatus = $"OK num {val:F3} m";
                    }
                    else
                    {
                        // 2) Intento parsear JSON: {"altura":0.52}
                        try
                        {
                            var json = JsonUtility.FromJson<AlturaWrap>(raw);
                            if (json != null)
                            {
                                target = json.altura;
                                lerpT = 0f;
                                lastStatus = $"OK json {target:F3} m";
                            }
                            else
                            {
                                lastStatus = $"Parse FAIL (ni número ni JSON). RAW='{raw}'";
                                Debug.LogWarning("[Tide] " + lastStatus);
                            }
                        }
                        catch (Exception e)
                        {
                            lastStatus = $"JSON parse EX: {e.Message}. RAW='{raw}'";
                            Debug.LogWarning("[Tide] " + lastStatus);
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"[Tide] HTTP {code} ERROR: {req.error}. RAW='{raw}'");
                    lastStatus = $"HTTP {code} {req.error}";
                }
            }

            yield return new WaitForSeconds(pollSeconds);
        }
    }

    [Serializable] class AlturaWrap { public float altura; }

    void Update()
    {
        // Si no se pudo leer, aplico fallback (para ver que el script sí mueve la malla)
        if (useFallbackWhenFail && (lastStatus.StartsWith("HTTP") || lastStatus.StartsWith("Parse")))
        {
            float w = 2f * Mathf.PI / Mathf.Max(0.001f, fallbackPeriod);
            target = Mathf.Sin(Time.time * w) * fallbackAmplitude;
        }

        lerpT += Time.deltaTime / Mathf.Max(0.001f, smoothSeconds);
        float val = Mathf.Lerp(current, target, Mathf.SmoothStep(0, 1, lerpT));
        current = val;

        var pos = basePos;
        pos.y = basePos.y + current;
        waterPlane.position = pos;
    }

    void OnGUI()
    {
        GUI.Label(new Rect(110, 10, 900, 22), $"Water Y={waterPlane.position.y:F3} (base {basePos.y:F3})");
        GUI.Label(new Rect(110, 30, 1200, 22), $"Current={current:F3}  Target={target:F3}  Status={lastStatus}");
        GUI.Label(new Rect(110, 50, 1200, 22), $"API={baseUrl}  lat={lat} lon={lon}");
    }
}
