using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using CesiumForUnity;

public class MultiAnchorsFromJson : MonoBehaviour
{
    [Header("Referencias")]
    public CesiumGeoreference georeference;   // Arrástralo. Si lo dejas vacío, se busca.
    public GameObject markerPrefab;           // Opcional. Si es null, crea una esfera pequeña.

    [Header("Datos (TextAsset JSON)")]
    public TextAsset jsonPoints;              // Debe tener { "points": [ {lon,lat,h}, ... ] }

    [Header("Opciones")]
    public bool clearOnPlay = true;           // Limpia marcadores previos al iniciar
    public string markersFolderName = "__Markers";
    public bool centerOriginNearPoint = true;

    [Serializable] public struct GeoPoint { public double lon, lat, h; }
    [Serializable] class GeoPointRoot { public GeoPoint[] points; }

    Transform _markersRoot;

    void Start()
    {
        // 1) Georeference
        if (!georeference)
        {
            Debug.LogError("[MultiAnchorsFromJson] No hay CesiumGeoreference en la escena.");
            return;
        }

        // 2) Asegura que este GO cuelga del georeference (seguro en Start)
        if (!transform.IsChildOf(georeference.transform))
            transform.SetParent(georeference.transform, true);

        // 3) Limpia marcadores previos
        if (clearOnPlay) ClearMarkers();

        // 4) Carga JSON
        var list = LoadPointsFromJson(jsonPoints);
        if (list == null || list.Count == 0)
        {
            Debug.LogWarning("[MultiAnchorsFromJson] JSON vacío o inválido.");
            return;
        }

        // 5) Crea marcadores (uno por punto) con CesiumGlobeAnchor
        var root = GetOrCreateMarkersRoot();
        foreach (var p in list)
        {
            var go = CreateMarker(root);
            go.name = $"Marker_{p.lat:F5}_{p.lon:F5}";

            var anchor = go.GetComponent<CesiumGlobeAnchor>();
            if (!anchor) anchor = go.AddComponent<CesiumGlobeAnchor>();

            // Autoridad: LLA (muy importante)
            anchor.longitudeLatitudeHeight = new double3(p.lon, p.lat, p.h);
        }

        Debug.Log($"[MultiAnchorsFromJson] Colocados {list.Count} puntos.");
    }

    List<GeoPoint> LoadPointsFromJson(TextAsset ta)
    {
        if (!ta || string.IsNullOrWhiteSpace(ta.text)) return null;
        try
        {
            var root = JsonUtility.FromJson<GeoPointRoot>(ta.text);
            if (root?.points == null || root.points.Length == 0) return null;
            return new List<GeoPoint>(root.points);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultiAnchorsFromJson] JSON inválido: {ex.Message}");
            return null;
        }
    }

    Transform GetOrCreateMarkersRoot()
    {
        if (_markersRoot) return _markersRoot;
        var t = transform.Find(markersFolderName);
        if (!t)
        {
            var go = new GameObject(markersFolderName);
            go.transform.SetParent(transform, false);
            _markersRoot = go.transform;
        }
        else _markersRoot = t;
        return _markersRoot;
    }

    GameObject CreateMarker(Transform parent)
    {
        GameObject go;
        if (markerPrefab)
        {
            go = Instantiate(markerPrefab, parent);
        }
        else
        {
            // Esfera mínima por defecto (si no asignaste prefab)
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * 0.6f;
            var col = go.GetComponent<Collider>(); if (col) Destroy(col);
        }
        return go;
    }

    public void ClearMarkers()
    {
        var root = transform.Find(markersFolderName);
        if (!root) return;

        var toDestroy = new List<GameObject>();
        foreach (Transform c in root) toDestroy.Add(c.gameObject);

        foreach (var g in toDestroy) Destroy(g);
    }
}
