using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;
using UnityEngine.UI;
using TMPro;

public class MultiAnchorsFromJsonFrames : MonoBehaviour
{
    [Header("Referencias")]
    public CesiumGeoreference georeference;   // arrástralo
    public GameObject markerPrefab;           // opcional (si es null, crea esferas)

    [Header("Datos (TextAsset JSON con frames)")]
    public TextAsset jsonMulti;               // { "frames":[ { "label":"5 min","t":300,"points":[...]} ] }

    [Header("Opciones")]
    public bool clearOnPlay = true;
    public string markersFolderName = "__Markers";

    [Header("Control de tiempo (opcional)")]
    public Slider slider;               // 0..frames-1 (whole numbers)

    public TMP_Text label;
    
    public KeyCode prevKey = KeyCode.LeftBracket;   // [
    public KeyCode nextKey = KeyCode.RightBracket;  // ]

    // ------------------ MODELOS ------------------
    [Serializable] public struct GeoPoint { public double lon, lat, h; }
    [Serializable] public class Frame { public string label; public int t; public List<GeoPoint> points; }
    [Serializable] public class Root { public string crs; public string height_ref; public List<Frame> frames; }

    // ------------------ ESTADO -------------------
    private Root _root;
    private readonly List<GameObject> _pool = new();
    private Transform _markersRoot;
    private int _index;

    void Start()
    {
        // 1) Georeference y parenting
        if (!georeference)
        {
#if UNITY_2023_1_OR_NEWER
            georeference = FindFirstObjectByType<CesiumGeoreference>();
#else
            georeference = FindObjectOfType<CesiumGeoreference>();
#endif
        }
        if (!georeference) { Debug.LogError("[MultiAnchorsFromJsonFrames] Falta CesiumGeoreference."); return; }
        if (!transform.IsChildOf(georeference.transform))
            transform.SetParent(georeference.transform, true);

        // 2) Limpieza inicial
        if (clearOnPlay) ClearMarkers();

        // 3) Cargar JSON
        if (!LoadJson(jsonMulti)) return;

        // 4) Slider opcional
        if (slider)
        {
            slider.wholeNumbers = true;
            slider.minValue = 0;
            slider.maxValue = Mathf.Max(0, _root.frames.Count - 1);
            slider.onValueChanged.AddListener(v => SetIndex(Mathf.RoundToInt(v)));
        }

        // 5) Mostrar primer frame
        SetIndex(0);
    }

    void Update()
    {
        if (Input.GetKeyDown(prevKey)) SetIndex(_index - 1);
        if (Input.GetKeyDown(nextKey)) SetIndex(_index + 1);
    }

    // ------------------ JSON ------------------
    bool LoadJson(TextAsset ta)
    {
        if (!ta || string.IsNullOrWhiteSpace(ta.text))
        {
            Debug.LogError("[MultiAnchorsFromJsonFrames] JSON vacío/no asignado.");
            return false;
        }
        try
        {
            _root = JsonUtility.FromJson<Root>(ta.text);
            if (_root == null || _root.frames == null || _root.frames.Count == 0)
            {
                Debug.LogError("[MultiAnchorsFromJsonFrames] Estructura inválida o frames vacíos. Revisa nombres de campos.");
                _root = null;
                return false;
            }
            Debug.Log($"[MultiAnchorsFromJsonFrames] Cargado OK. frames={_root.frames.Count}, frame0_points={_root.frames[0].points?.Count ?? 0}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MultiAnchorsFromJsonFrames] Error parseando JSON: {ex.Message}");
            _root = null;
            return false;
        }
    }

    // ------------------ UI/Tiempo ------------------
    public void SetIndex(int i)
    {
        if (_root == null || _root.frames == null || _root.frames.Count == 0) return;
        _index = Mathf.Clamp(i, 0, _root.frames.Count - 1);
        ShowFrame(_root.frames[_index]);

        // actualizar UI opcional
        if (slider && Mathf.RoundToInt(slider.value) != _index)
            slider.SetValueWithoutNotify(_index);

        string txt = !string.IsNullOrEmpty(_root.frames[_index].label)
            ? _root.frames[_index].label
            : $"t={_root.frames[_index].t}s";
        if (label) label.text = "Tiempo: "+txt;

        Debug.Log($"[MultiAnchorsFromJsonFrames] Frame #{_index} → {txt}");
    }

    public void NextFrame() => SetIndex(_index + 1);
    public void PrevFrame() => SetIndex(_index - 1);

    // ------------------ Marcadores ------------------
    void EnsurePoolSize(int count)
    {
        var root = GetOrCreateMarkersRoot();

        while (_pool.Count < count)
            _pool.Add(CreateMarker(root));

        for (int i = 0; i < _pool.Count; i++)
            _pool[i].SetActive(i < count);
    }

    void ShowFrame(Frame f)
    {
        if (f == null || f.points == null || f.points.Count == 0)
        {
            HideAll();
            Debug.LogWarning("[MultiAnchorsFromJsonFrames] Frame vacío.");
            return;
        }

        EnsurePoolSize(f.points.Count);

        for (int i = 0; i < f.points.Count; i++)
        {
            var go = _pool[i];
            if (!go.activeSelf) go.SetActive(true);

            var anchor = go.GetComponent<CesiumGlobeAnchor>() ?? go.AddComponent<CesiumGlobeAnchor>();
            var p = f.points[i];

            // Posición EXACTA por LLA (tu versión no requiere positionAuthority)
            anchor.longitudeLatitudeHeight = new double3(p.lon, p.lat, p.h);

            go.name = $"Marker_{i:D3}_{p.lat:F5}_{p.lon:F5}";
        }

        // Desactiva sobrantes si los hubiera
        for (int i = f.points.Count; i < _pool.Count; i++)
            if (_pool[i].activeSelf) _pool[i].SetActive(false);

        Debug.Log($"[MultiAnchorsFromJsonFrames] Mostrando {f.points.Count} puntos.");
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
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * 0.7f;
            var col = go.GetComponent<Collider>(); if (col) Destroy(col);
        }

        // Asegura el CesiumGlobeAnchor en el root del marker
        if (!go.GetComponent<CesiumGlobeAnchor>())
            go.AddComponent<CesiumGlobeAnchor>();

        return go;
    }

    public void HideAll()
    {
        foreach (var g in _pool) if (g) g.SetActive(false);
    }

    public void ClearMarkers()
    {
        var root = transform.Find(markersFolderName);
        if (!root) return;

        var toDestroy = new List<GameObject>();
        foreach (Transform c in root) toDestroy.Add(c.gameObject);
        foreach (var g in toDestroy) Destroy(g);

        _pool.Clear();
    }
}
