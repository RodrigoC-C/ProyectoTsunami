using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics; // para double3

public class TsunamiVisualizer : MonoBehaviour
{
    [Header("Referencias")]
    public CesiumGeoreference georeference;  // arrástralo; si no, se busca en Awake
    public Transform markersRoot;            // Empty para organizar
    public GameObject markerPrefab;          // esfera o tu prefab

    [Header("Opciones")]
    public bool useGlobeAnchor = true;       // RECOMENDADO: coloca puntos con CesiumGlobeAnchor
    public float markerScaleMeters = 3f;

    private readonly List<GameObject> _pool = new List<GameObject>();

    [Header("Testing (solo para debug)")]
    public bool testBigMarkers = false;
    [Min(0.1f)] public float testScaleMeters = 30f;


    [Header("Wave Patches")]
    public GameObject wavePatchPrefab;   // el prefab con TsunamiController
    public Transform deepSeaOrigin;      // un Empty mar adentro
    public float startFromTargetMeters = 3000f; // qué tan mar adentro parte cada parche

    private readonly List<GameObject> _wavePool = new List<GameObject>();

    void Awake()
    {
        if (!georeference) georeference = Object.FindAnyObjectByType<CesiumGeoreference>();
        if (!georeference)
            Debug.LogError("[TsunamiVisualizer] CesiumGeoreference no encontrado en la escena.");
    }

    public void ShowFrame(FrameOut frame)
    {
        if (frame == null || frame.points == null) return;

        EnsurePool(frame.points.Count);

        for (int i = 0; i < _pool.Count; i++)
        {
            var go = _pool[i];
            if (i < frame.points.Count)
            {
                var p = frame.points[i];
                go.SetActive(true);

                if (useGlobeAnchor)
                {
                    // Opción 1 (recomendada): usar CesiumGlobeAnchor y poner L/L/H directamente (en grados y metros)
                    var anchor = go.GetComponent<CesiumGlobeAnchor>();
                    if (!anchor) anchor = go.AddComponent<CesiumGlobeAnchor>();
                    anchor.longitudeLatitudeHeight = new double3(p.lon, p.lat, p.h);
                }
                else
                {
                    // Opción 2: conversión manual L/L/H -> ECEF -> Unity
                    double3 ecef = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                        new double3(p.lon, p.lat, p.h) // grados + metros (elipsoidal)
                    );
                    double3 u = georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
                    go.transform.SetPositionAndRotation(
                        new Vector3((float)u.x, (float)u.y, (float)u.z), Quaternion.identity
                    );
                }
            }
            else
            {
                go.SetActive(false);
            }
        }
    }

    public void LaunchWavePatchesTowardCurrentMarkers(FrameOut frame)
    {
        if (frame == null || frame.points == null) return;
        EnsureWavePool(frame.points.Count);

        for (int i = 0; i < frame.points.Count; i++)
        {
            var marker = _pool[i];
            var waveGo = _wavePool[i];
            waveGo.SetActive(true);

            var ctrl = waveGo.GetComponent<TsunamiController>();

            // Dirección: DE DeepSeaOrigin HACIA el marcador
            var dir = (marker.transform.position - deepSeaOrigin.position).normalized;

            // Punto de partida: "startFromTargetMeters" por detrás del marcador (hacia el mar)
            var startPos = marker.transform.position - dir * startFromTargetMeters;

            ctrl.BeginTowards(marker.transform, startPos);
        }

        for (int i = frame.points.Count; i < _wavePool.Count; i++)
            _wavePool[i].SetActive(false);
    }

    private void EnsureWavePool(int needed)
    {
        while (_wavePool.Count < needed)
        {
            var parent = markersRoot ? markersRoot : transform;   // <--- MISMO PARENT QUE MARCADORES
            var go = Instantiate(wavePatchPrefab, parent);
            _wavePool.Add(go);
        }
    }

    private void EnsurePool(int needed)
    {
        while (_pool.Count < needed)
        {
            var prefab = markerPrefab ?? GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var go = Instantiate(prefab, markersRoot ? markersRoot : transform);
            go.transform.localScale = Vector3.one * (testBigMarkers ? testScaleMeters : markerScaleMeters);
            _pool.Add(go);
        }
    }

    public void ClearAll()
    {
        foreach (var go in _pool) if (go) go.SetActive(false);
    }
    public void ApplyMarkerScale()
    {
        float s = (testBigMarkers ? testScaleMeters : markerScaleMeters);
        foreach (var go in _pool)
            if (go) go.transform.localScale = Vector3.one * s;
    }
    // (opcional) para que cambie en tiempo real cuando muevas el slider en el Inspector
#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_pool != null && _pool.Count > 0)
            ApplyMarkerScale();
    }
#endif
}
