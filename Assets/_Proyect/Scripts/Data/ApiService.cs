using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

public class ApiService : MonoBehaviour
{
    [SerializeField] private ApiSettings settings;

    public void Init(ApiSettings cfg) => settings = cfg;

    public IEnumerator GetFramesStructured(string timesCSV, Action<FramesPayload> onSuccess, Action<string> onError)
    {
        if (settings == null) { onError?.Invoke("ApiSettings no asignado"); yield break; }

        string url = $"{settings.baseUrl}{settings.framesEndpoint}";

        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = settings.timeoutSeconds;
            req.downloadHandler = new DownloadHandlerBuffer();

            if (settings.logHttp) Debug.Log($"[ApiService] GET {url}");
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: no hay threads; coroutines ok
#endif
            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                onError?.Invoke($"HTTP error: {req.responseCode} - {req.error}");
                yield break;
            }

            try
            {
                var json = req.downloadHandler.text;
                var payload = JsonConvert.DeserializeObject<FramesPayload>(json);
                onSuccess?.Invoke(payload);
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Parse error: {ex.Message}");
            }
        }
    }
}