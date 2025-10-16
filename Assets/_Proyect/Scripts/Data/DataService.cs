using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Tsunami.Services
{
    /// <summary>
    /// Servicio de datos: consulta timeline y descarga heightmaps (MinIO presignado) según tu API.
    /// </summary>
    public sealed class DataService
    {
        // ========= CONFIG =========
        public string ApiBaseUrl { get; }
        public string TimelineEndpoint { get; } = "/timeline";
        public string FrameUrlEndpoint { get; } = "/frame/url";

        public int MaxConcurrentDownloads { get; set; } = 4;
        public int MaxRetriesPerFile { get; set; } = 3;
        public float RetryBackoffSeconds { get; set; } = 1.25f;

        /// <summary>Carpeta base de caché temporal.</summary>
        public string CacheBasePath { get; }

        /// <summary>Intervalo entre frames reportado por la API (segundos). Úsalo para el playback.</summary>
        public int LastFrameIntervalSeconds { get; private set; } = 0;

        public event Action<float> OnOverallProgress;           // 0..1 total
        public event Action<int, string> OnFileStarted;         // idx, path
        public event Action<int, string> OnFileCompleted;       // idx, path
        public event Action<string> OnLog;

        public DataService(string apiBaseUrl, string subFolder = "heightmaps_cache")
        {
            ApiBaseUrl = apiBaseUrl.TrimEnd('/');
            CacheBasePath = Path.Combine(Application.temporaryCachePath, subFolder);
            Directory.CreateDirectory(CacheBasePath);
        }

        // ====== DTOs (exactos a tu backend) ======
        [Serializable] private class TimelineItem
        {
            public int t_index;
            public int t_sec;
            public int width;   // en tu JSON vienen null -> JsonUtility los deja en 0
            public int height;
            public string format;
        }

        [Serializable] private class TimelineResponse
        {
            public string city;
            public string scenario;
            public int frame_interval_sec;
            public TimelineItem[] items;
        }

        [Serializable] private class FrameUrlObj
        {
            public int t_index;
            public int t_sec;
            public string url;        // presigned URL
            public int width;
            public int height;
            public string format;     // "png"
        }

        private class DownloadItem
        {
            public int Index;
            public string Url;
            public string LocalPath;
            public long ExpectedBytes;   // no viene en tu API (queda 0, se ignora)
            public string ETag;          // no viene en tu API
        }

        // ====== API pública ======

        /// <summary>
        /// Descarga todos los heightmaps para city+scenario.
        /// Devuelve rutas locales ordenadas por t_index.
        /// Además actualiza LastFrameIntervalSeconds con lo reportado por la API.
        /// </summary>
        public async Task<IReadOnlyList<string>> PreloadHeightmapsAsync(
            string city,
            string scenario,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(city)) throw new ArgumentException("city vacío.");
            if (string.IsNullOrWhiteSpace(scenario)) throw new ArgumentException("scenario vacío.");

            // 1) Timeline → lista de índices + frame_interval_sec
            var (indices, frameIntervalSec) = await FetchTimelineAsync(city, scenario, ct);
            LastFrameIntervalSeconds = frameIntervalSec;

            if (indices == null || indices.Count == 0)
                throw new Exception("El timeline está vacío.");

            // 2) Carpeta por city/scenario (sin runId en tu API -> usamos clave estática)
            var runKey = $"{city}_{scenario}";
            var runFolder = Path.Combine(CacheBasePath, Sanitize($"{city}/{scenario}/{runKey}"));
            Directory.CreateDirectory(runFolder);

            // 3) Resolver URLs por frame
            var metas = await ResolveFrameMetasAsync(city, scenario, indices, runFolder, ct);

            // 4) Descargar
            await DownloadAllAsync(metas, ct);

            // 5) Rutas locales ordenadas
            return metas.OrderBy(i => i.Index).Select(i => i.LocalPath).ToList();
        }

        /// <summary>Devuelve la carpeta de caché base (temporal).</summary>
        public string GetCacheBasePath() => CacheBasePath;

        /// <summary>Elimina toda la caché temporal generada por este servicio.</summary>
        public void ClearCache()
        {
            try
            {
                if (Directory.Exists(CacheBasePath))
                    Directory.Delete(CacheBasePath, true);
            }
            catch (Exception e)
            {
                Log($"No se pudo limpiar caché: {e.Message}");
            }
            Directory.CreateDirectory(CacheBasePath);
        }

        // ====== Internals ======

        private async Task<(List<int> indices, int frameIntervalSec)> FetchTimelineAsync(
            string city, string scenario, CancellationToken ct)
        {
            var url = $"{ApiBaseUrl}{TimelineEndpoint}?city={UnityWebRequest.EscapeURL(city)}&scenario={UnityWebRequest.EscapeURL(scenario)}";
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 30;
                var op = req.SendWebRequest();
                await op;

#if UNITY_2020_2_OR_NEWER
                if (req.result != UnityWebRequest.Result.Success)
#else
                if (req.isNetworkError || req.isHttpError)
#endif
                    throw new Exception($"Error consultando timeline: {req.error} ({req.responseCode})");

                var json = req.downloadHandler.text?.Trim();
                if (string.IsNullOrEmpty(json))
                    throw new Exception("Timeline vacío.");

                var obj = JsonUtility.FromJson<TimelineResponse>(json);
                if (obj == null || obj.items == null || obj.items.Length == 0)
                    throw new Exception("Respuesta de timeline inválida o sin items.");

                var list = obj.items.Select(it => it.t_index).OrderBy(x => x).ToList();
                var interval = Mathf.Max(0, obj.frame_interval_sec);
                return (list, interval);
            }
        }

        private async Task<List<DownloadItem>> ResolveFrameMetasAsync(
            string city, string scenario, List<int> indices, string runFolder, CancellationToken ct)
        {
            var results = new List<DownloadItem>(indices.Count);

            using (var sem = new System.Threading.SemaphoreSlim(Mathf.Min(MaxConcurrentDownloads, 8)))
            {
                var tasks = indices.Select(async idx =>
                {
                    await sem.WaitAsync(ct);
                    try
                    {
                        var meta = await FetchFrameUrlAsync(city, scenario, idx, ct);
                        var localName = $"frame_{idx:0000}.png"; // tu API ya usa este patrón
                        var di = new DownloadItem
                        {
                            Index = idx,
                            Url = meta.url,
                            LocalPath = Path.Combine(runFolder, localName),
                            ExpectedBytes = 0,
                            ETag = null
                        };
                        lock (results) results.Add(di);
                    }
                    finally { sem.Release(); }
                }).ToList();

                await Task.WhenAll(tasks);
            }

            results.Sort((a, b) => a.Index.CompareTo(b.Index));
            return results;
        }

        private async Task<FrameUrlObj> FetchFrameUrlAsync(
            string city, string scenario, int tIndex, CancellationToken ct)
        {
            var url = $"{ApiBaseUrl}{FrameUrlEndpoint}?city={UnityWebRequest.EscapeURL(city)}&scenario={UnityWebRequest.EscapeURL(scenario)}&t_index={tIndex}";
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 30;
                var op = req.SendWebRequest();
                await op;

#if UNITY_2020_2_OR_NEWER
                if (req.result != UnityWebRequest.Result.Success)
#else
                if (req.isNetworkError || req.isHttpError)
#endif
                    throw new Exception($"Error consultando frame url (t_index={tIndex}): {req.error} ({req.responseCode})");

                var json = req.downloadHandler.text;
                var obj = JsonUtility.FromJson<FrameUrlObj>(json);
                if (obj == null || string.IsNullOrEmpty(obj.url))
                    throw new Exception($"Respuesta inválida de /frame/url para t_index={tIndex}.");
                return obj;
            }
        }

        private async Task DownloadAllAsync(List<DownloadItem> items, CancellationToken ct)
        {
            var total = items.Count;
            var completed = 0;

            using (var sem = new System.Threading.SemaphoreSlim(MaxConcurrentDownloads))
            {
                var tasks = items.Select(async item =>
                {
                    await sem.WaitAsync(ct);
                    try
                    {
                        OnFileStarted?.Invoke(item.Index, item.LocalPath);
                        await EnsureDownloadedAsync(item, ct);
                        OnFileCompleted?.Invoke(item.Index, item.LocalPath);
                    }
                    finally
                    {
                        var done = Interlocked.Increment(ref completed);
                        OnOverallProgress?.Invoke(done / (float)total);
                        sem.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks);
            }
        }

        private async Task EnsureDownloadedAsync(DownloadItem item, CancellationToken ct)
        {
            if (File.Exists(item.LocalPath))
            {
                // No tenemos ExpectedBytes/ETag -> si existe, lo aceptamos
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(item.LocalPath));

            var attempt = 0;
            Exception last = null;

            while (attempt < MaxRetriesPerFile)
            {
                ct.ThrowIfCancellationRequested();
                attempt++;

                try
                {
                    await DownloadToFileAsync(item.Url, item.LocalPath, ct);
                    return; // OK
                }
                catch (Exception ex)
                {
                    last = ex;
                    SafeDelete(item.LocalPath);

                    var wait = Mathf.Pow(RetryBackoffSeconds, attempt);
                    Log($"Fallo descargando idx {item.Index} (intento {attempt}/{MaxRetriesPerFile}): {ex.Message}. Reintentando en {wait:0.0}s");
                    await Task.Delay(TimeSpan.FromSeconds(wait), ct);
                }
            }

            throw new Exception($"No se pudo descargar {item.Url} -> {item.LocalPath}", last);
        }

        private static async Task DownloadToFileAsync(string url, string localPath, CancellationToken ct)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 60;
                var dh = new DownloadHandlerFile(localPath, true); // resume si existe parcial
                req.downloadHandler = dh;

                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

#if UNITY_2020_2_OR_NEWER
                if (req.result != UnityWebRequest.Result.Success)
#else
                if (req.isNetworkError || req.isHttpError)
#endif
                    throw new IOException($"Descarga fallida: {req.error} ({req.responseCode})");
            }
        }

        private void Log(string msg)
        {
            OnLog?.Invoke(msg);
#if UNITY_EDITOR
            Debug.Log($"[DataService] {msg}");
#endif
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* ignore */ }
        }

        private static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Replace('\\','/'); // permite subcarpetas
        }
    }
}
