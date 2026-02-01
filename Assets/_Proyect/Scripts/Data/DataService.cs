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
    /// Servicio de datos: consulta timeline y descarga heightmaps (URL firmada) según tu API.
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

        /// <summary>Intervalo entre frames reportado por la API (segundos). Úsalo como fallback.</summary>
        public int LastFrameIntervalSeconds { get; private set; } = 0;

        public event Action<float> OnOverallProgress;
        public event Action<int, string> OnFileStarted;
        public event Action<int, string> OnFileCompleted;
        public event Action<string> OnLog;

        public DataService(string apiBaseUrl, string subFolder = "heightmaps_cache")
        {
            ApiBaseUrl = apiBaseUrl.TrimEnd('/');
            CacheBasePath = Path.Combine(Application.temporaryCachePath, subFolder);
            Directory.CreateDirectory(CacheBasePath);
        }

        // ====== DTOs (según backend) ======
        [Serializable] private class TimelineItem
        {
            public int t_index;
            public int t_sec;
            public int width;
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
            public string url;
            public int width;
            public int height;
            public string format;
        }

        private class DownloadItem
        {
            public int Index;
            public int TSec;
            public string Url;
            public string LocalPath;
            public long ExpectedBytes;
            public string ETag;
        }

        public class FrameMeta
        {
            public int Index { get; set; }       // 1..N (como en tu API)
            public int TSec { get; set; }
            public string LocalPath { get; set; }
        }

        private class TimelineInfo
        {
            public List<int> indices;
            public Dictionary<int, int> indexToSec;
            public int frameIntervalSec;
        }

        // ====== API pública ======

        /// <summary>
        /// Descarga heightmaps para city+scenario. Devuelve rutas locales ordenadas por t_index.
        /// </summary>
        public async Task<IReadOnlyList<string>> PreloadHeightmapsAsync(
            string city,
            string scenario,
            CancellationToken ct = default)
        {
            var metas = await PreloadHeightmapsWithMetaAsync(city, scenario, ct);
            return metas.Select(m => m.LocalPath).ToList();
        }

        /// <summary>
        /// Igual que el anterior pero devuelve también t_sec por frame.
        /// </summary>
        public async Task<IReadOnlyList<FrameMeta>> PreloadHeightmapsWithMetaAsync(
            string city,
            string scenario,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(city)) throw new ArgumentException("city vacío.");
            if (string.IsNullOrWhiteSpace(scenario)) throw new ArgumentException("scenario vacío.");

            var tinfo = await FetchTimelineAsync(city, scenario, ct);
            LastFrameIntervalSeconds = tinfo.frameIntervalSec;

            if (tinfo.indices == null || tinfo.indices.Count == 0)
                throw new Exception("El timeline está vacío.");

            var runKey = $"{city}_{scenario}";
            var runFolder = Path.Combine(CacheBasePath, Sanitize($"{city}/{scenario}/{runKey}"));
            Directory.CreateDirectory(runFolder);

            var items = await ResolveFrameMetasAsync(city, scenario, tinfo.indices, runFolder, tinfo.indexToSec, ct);
            await DownloadAllAsync(items, ct);

            var outList = items.OrderBy(m => m.Index).Select(m => new FrameMeta
            {
                Index = m.Index,
                TSec = m.TSec,
                LocalPath = m.LocalPath
            }).ToList();

            return outList;
        }

        public string GetCacheBasePath() => CacheBasePath;

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

        private async Task<TimelineInfo> FetchTimelineAsync(string city, string scenario, CancellationToken ct)
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

                var indices = obj.items.Select(it => it.t_index).OrderBy(x => x).ToList();
                var map = new Dictionary<int, int>(obj.items.Length);
                foreach (var it in obj.items) map[it.t_index] = it.t_sec;

                return new TimelineInfo
                {
                    indices = indices,
                    indexToSec = map,
                    frameIntervalSec = Mathf.Max(0, obj.frame_interval_sec)
                };
            }
        }

        private async Task<List<DownloadItem>> ResolveFrameMetasAsync(
            string city,
            string scenario,
            List<int> indices,
            string runFolder,
            Dictionary<int, int> indexToSec,
            CancellationToken ct)
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
                        var localName = $"frame_{idx:0000}.png";
                        var di = new DownloadItem
                        {
                            Index = idx,
                            TSec = (indexToSec != null && indexToSec.TryGetValue(idx, out var tsecs)) ? tsecs : meta.t_sec,
                            Url = meta.url,
                            LocalPath = Path.Combine(runFolder, localName),
                            ExpectedBytes = 0,
                            ETag = null
                        };
                        lock (results) results.Add(di);
                    }
                    finally
                    {
                        sem.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks);
            }

            results.Sort((a, b) => a.Index.CompareTo(b.Index));
            return results;
        }

        private async Task<FrameUrlObj> FetchFrameUrlAsync(string city, string scenario, int tIndex, CancellationToken ct)
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
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(item.LocalPath));

            int attempt = 0;
            Exception last = null;

            while (attempt < MaxRetriesPerFile)
            {
                ct.ThrowIfCancellationRequested();
                attempt++;

                try
                {
                    await DownloadToFileAsync(item.Url, item.LocalPath, ct);
                    return;
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
                var dh = new DownloadHandlerFile(localPath, true);
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
            return s.Replace('\\', '/');
        }
    }
}
