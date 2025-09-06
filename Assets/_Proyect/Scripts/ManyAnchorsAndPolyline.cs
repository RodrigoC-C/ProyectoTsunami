// Assets/Scripts/ManyAnchorsAndPolyline.cs
// Unity 6000.0.43f1 – URP/HDRP – Cesium for Unity
// - Múltiples puntos (lon/lat/alt) -> marcadores con CesiumGlobeAnchor
// - Polilínea con LineRenderer para “barrera/contorno”
// - Carga opcional desde JSON

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using CesiumForUnity;

[ExecuteAlways]
public class ManyAnchorsAndPolyline : MonoBehaviour
{
    [Header("REFERENCIAS")]
    public CesiumGeoreference georeference;  // si lo dejas vacío, se busca en escena
    [Tooltip("Prefab para cada punto. Debe estar vacío o contener tu visual. Se le añadirá CesiumGlobeAnchor automáticamente.")]
    public GameObject markerPrefab;
    [Tooltip("LineRenderer para la polilínea. Si está vacío, se creará uno automáticamente como hijo.")]
    public LineRenderer lineRenderer;

    [Header("DATOS – PUNTOS (WGS84 grados, altura m elipsoide)")]
    public List<GeoPoint> points = new List<GeoPoint>();

    [Header("OPCIONES DE LÍNEA")]
    public bool drawLine = true;
    public float lineWidth = 2f;
    public bool closeLoop = false;

    [Header("OPCIONES DE MARCADORES")]
    public bool spawnMarkers = true;
    [Tooltip("Se limpian y recrean marcadores en cada Apply()")]
    public bool rebuildMarkersEachApply = true;

    [Header("CARGA OPCIONAL DESDE JSON (TextAsset)")]
    [Tooltip("Formato: { \"points\": [ {\"lon\": -71.628, \"lat\": -33.045, \"h\": 20.0}, ... ] }")]
    public TextAsset jsonPoints;

    [Serializable]
    public struct GeoPoint
    {
        public double lon;
        public double lat;
        public double h;
    }

    [Serializable]
    private class GeoPointRoot
    {
        public GeoPoint[] points;
    }

    // Marcadores instanciados
    private readonly List<GameObject> _spawnedMarkers = new();

    void OnEnable() => Apply();
    void OnValidate() => Apply();
    void Update()
    {
        // Mantén la línea coherente si el origen de Cesium cambiara o si editas puntos en tiempo real
        if (drawLine && lineRenderer != null && georeference != null)
            UpdateLinePositions();
    }

    [ContextMenu("Apply now")]
    public void Apply()
    {
        // 0) Georeference
        if (georeference == null)
        {
            var refs = FindObjectsByType<CesiumGeoreference>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (refs.Length == 0)
            {
                Debug.LogError("[ManyAnchorsAndPolyline] No hay CesiumGeoreference en la escena.");
                return;
            }
            georeference = refs[0];
        }

        // 1) Cargar desde JSON si existe y es válido
        if (jsonPoints != null && !string.IsNullOrWhiteSpace(jsonPoints.text))
        {
            try
            {
                var root = JsonUtility.FromJson<GeoPointRoot>(jsonPoints.text);
                if (root != null && root.points != null && root.points.Length > 0)
                {
                    points = new List<GeoPoint>(root.points);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ManyAnchorsAndPolyline] JSON inválido: {ex.Message}");
            }
        }

        // 2) Marcadores
        if (spawnMarkers)
            BuildOrRebuildMarkers();
        else
            ClearMarkers();

        // 3) Línea
        EnsureLineRenderer();
        if (drawLine) UpdateLinePositions();
        if (lineRenderer != null) lineRenderer.enabled = drawLine;
    }

    private void EnsureLineRenderer()
    {
        if (!drawLine) return;

        if (lineRenderer == null)
        {
            // Crear hijo con LineRenderer si no existe
            var go = new GameObject("TsunamiLimit_LineRenderer");
            go.transform.SetParent(transform, false);
            lineRenderer = go.AddComponent<LineRenderer>();
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            lineRenderer.useWorldSpace = true;
            lineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        }

        lineRenderer.widthMultiplier = Mathf.Max(0.0001f, lineWidth);
        lineRenderer.alignment = LineAlignment.View; // se ve bien en top-down; puedes cambiar a TransformZ si prefieres
    }

    private void UpdateLinePositions()
    {
        if (points == null || points.Count == 0 || georeference == null || lineRenderer == null)
        {
            if (lineRenderer != null) lineRenderer.positionCount = 0;
            return;
        }

        int count = points.Count + (closeLoop ? 1 : 0);
        lineRenderer.positionCount = count;

        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            double3 ecef = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
            new double3(p.lon, p.lat, p.h));

            double3 xyz = georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            lineRenderer.SetPosition(i, new Vector3((float)xyz.x, (float)xyz.y, (float)xyz.z));
        }

        if (closeLoop)
        {
            // Repite el primer punto al final
            var p0 = points[0];
            double3 ecef0 = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
            new double3(p0.lon, p0.lat, p0.h));
            double3 xyz0 = georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef0);
            lineRenderer.SetPosition(points.Count, new Vector3((float)xyz0.x, (float)xyz0.y, (float)xyz0.z));
        }
    }

    private void BuildOrRebuildMarkers()
    {
        if (markerPrefab == null)
        {
            Debug.LogWarning("[ManyAnchorsAndPolyline] spawnMarkers=true, pero markerPrefab es null. No se crearán marcadores.");
            return;
        }

        if (rebuildMarkersEachApply)
            ClearMarkers();

        // Si ya existen y no queremos reconstruir, nada que hacer
        if (!rebuildMarkersEachApply && _spawnedMarkers.Count > 0)
            return;

        if (points == null || points.Count == 0) return;

        foreach (var p in points)
        {
            var go = Instantiate(markerPrefab, transform);
            go.name = $"Marker_{p.lat:F5}_{p.lon:F5}";

            // Asegura CesiumGlobeAnchor en el marcador para precisión geográfica
            var anchor = go.GetComponent<CesiumGlobeAnchor>();
            if (!anchor) anchor = go.AddComponent<CesiumGlobeAnchor>();
            anchor.longitudeLatitudeHeight = new double3(p.lon, p.lat, p.h);

            _spawnedMarkers.Add(go);
        }
    }

    private void ClearMarkers()
    {
        foreach (var m in _spawnedMarkers)
        {
            if (m != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(m);
                else Destroy(m);
#else
                Destroy(m);
#endif
            }
        }
        _spawnedMarkers.Clear();
    }
}

