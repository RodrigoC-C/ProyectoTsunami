using System.Collections.Generic;
using UnityEngine;

/// Genera una malla tipo “ribbon” tomando como borde EXACTO los marcadores ya colocados
/// (hijos de markersRoot). Se proyecta hacia el mar (o hacia tierra) una distancia dada,
/// con número de columnas/filas configurables. No re-georreferencia: usa posiciones WS actuales.
///
/// Cómo usar:
///  - Asigna markersRoot (el mismo que usa TsunamiVisualizer).
///  - Arrastra DeepOrigin (un Empty mar adentro para saber el lado).
///  - Ajusta columnsMode / extraSubdivs / rows / extentMeters / height options.
///  - Llama RebuildFromMarkers() cada vez que cambies de frame (lo conecto abajo).
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WaterRibbonFromMarkers : MonoBehaviour
{
    public enum ColumnsMode { UseMarkersExactly, SubdivideBetweenMarkers }
    public enum HeightMode { FlatAtSeaLevel, LerpToSeaLevelFromPoints, CopyPointsOnlyOnCoast }

    [Header("Entradas")]
    public Transform markersRoot;            // padre de las esferas/markers actuales
    public Transform deepOrigin;             // punto mar adentro para definir “hacia dónde”
    public Transform seaLevelRef;            // opcional: si lo asignas, usa su Y como nivel del mar
    public float seaLevelY = 0f;             // si no hay seaLevelRef, usa este Y

    [Header("Malla")]
    public ColumnsMode columnsMode = ColumnsMode.SubdivideBetweenMarkers;
    [Tooltip("Solo si ColumnsMode=SubdivideBetweenMarkers: cuántas subdivisiones internas por segmento")]
    [Min(0)] public int extraSubdivsPerSegment = 3;
    [Tooltip("Filas (profundidad). 0 = solo la orilla; 1 = orilla + una fila; etc.")]
    [Min(1)] public int rows = 8;
    [Tooltip("Cuánto se proyecta hacia el lado elegido (m)")]
    public float extentMeters = 4000f;
    [Tooltip("Proyectar hacia DeepOrigin (true) o alejándose (false)")]
    public bool projectTowardDeepOrigin = true;

    [Header("Alturas")]
    public HeightMode heightMode = HeightMode.LerpToSeaLevelFromPoints;
    [Tooltip("Offset visual para evitar z-fighting")]
    public float elevateMeters = 0.2f;

    [Header("Orden/Debug")]
    public bool drawGizmos = true;
    [Range(0, 1)] public float gizmoNormalScale = 0.1f;

    Mesh _mesh;
    readonly List<Vector3> _coastWS = new();    // columnas (borde exacto)
    readonly List<Vector3> _dirsWS = new();    // normales por columna (hacia mar/tierra)
    float SeaY => seaLevelRef ? seaLevelRef.position.y : seaLevelY;

    void Awake()
    {
        _mesh = new Mesh { name = "WaterRibbonMesh" };
        GetComponent<MeshFilter>().sharedMesh = _mesh;
    }

    // Llama esto cuando cambies de frame.
    public void RebuildFromMarkers()
    {
        _mesh.Clear();
        _coastWS.Clear();
        _dirsWS.Clear();

        if (!markersRoot || markersRoot.childCount == 0) return;

        // 1) Recolectar marcadores activos
        var points = new List<Transform>();
        for (int i = 0; i < markersRoot.childCount; i++)
        {
            var c = markersRoot.GetChild(i);
            if (c.gameObject.activeInHierarchy) points.Add(c);
        }
        if (points.Count < 2) return;

        // 2) Ordenarlos “a lo largo de la costa” (PCA mínima sobre XZ)
        points.Sort((a, b) => (a.position.x).CompareTo(b.position.x)); // pre-sort para estabilidad
        SortAlongCoast(points);

        // 3) Generar columnas del borde (exacto por marcadores o con subdivs)
        if (columnsMode == ColumnsMode.UseMarkersExactly)
        {
            foreach (var t in points) _coastWS.Add(t.position);
        }
        else
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 A = points[i].position;
                Vector3 B = points[i + 1].position;
                int steps = Mathf.Max(1, extraSubdivsPerSegment + 1); // cuantos tramos entre A-B
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps;
                    _coastWS.Add(Vector3.Lerp(A, B, t));
                }
            }
            _coastWS.Add(points[^1].position); // último
        }

        // 4) Normal “hacia el mar/tierra” por columna (según DeepOrigin)
        Vector3 deep = deepOrigin ? deepOrigin.position : (_coastWS[0] - Vector3.forward * 1000f);
        for (int i = 0; i < _coastWS.Count; i++)
        {
            Vector3 toDeep = (deep - _coastWS[i]);
            toDeep.y = 0; // horizontalizar
            if (toDeep.sqrMagnitude < 1e-6f) toDeep = Vector3.forward;
            toDeep.Normalize();

            Vector3 dir = projectTowardDeepOrigin ? toDeep : -toDeep;
            _dirsWS.Add(dir);
        }

        // 5) Construir grid (cols x rows)
        int cols = _coastWS.Count;
        int rws = Mathf.Max(1, rows);
        int vertCount = cols * (rws + 1);
        var verts = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];

        float seaY = SeaY;

        for (int i = 0; i < cols; i++)
        {
            Vector3 coast = _coastWS[i];
            Vector3 dir = _dirsWS[i];

            for (int j = 0; j <= rws; j++)
            {
                float t = j / (float)rws;         // 0..1
                Vector3 pos = coast + dir * (t * extentMeters);

                float y;
                switch (heightMode)
                {
                    case HeightMode.FlatAtSeaLevel:
                        y = seaY;
                        break;
                    case HeightMode.CopyPointsOnlyOnCoast:
                        y = (j == 0) ? coast.y : seaY;
                        break;
                    default: // LerpToSeaLevelFromPoints
                        y = Mathf.Lerp(coast.y, seaY, t);
                        break;
                }
                pos.y = y + elevateMeters;

                int idx = i * (rws + 1) + j;
                verts[idx] = transform.InverseTransformPoint(pos);
                uvs[idx] = new Vector2(i / (float)Mathf.Max(1, cols - 1), t);
            }
        }

        // 6) Triángulos
        var tris = new int[(cols - 1) * rws * 6];
        int k = 0;
        for (int i = 0; i < cols - 1; i++)
        {
            for (int j = 0; j < rws; j++)
            {
                int a = i * (rws + 1) + j;
                int b = (i + 1) * (rws + 1) + j;
                int c = i * (rws + 1) + (j + 1);
                int d = (i + 1) * (rws + 1) + (j + 1);

                tris[k++] = a; tris[k++] = b; tris[k++] = c;
                tris[k++] = c; tris[k++] = b; tris[k++] = d;
            }
        }

        _mesh.Clear();
        _mesh.vertices = verts;
        _mesh.uv = uvs;
        _mesh.triangles = tris;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }

    // Ordena transforms a lo largo del eje de mayor varianza en XZ (tipo PCA simple)
    void SortAlongCoast(List<Transform> pts)
    {
        Vector3 mean = Vector3.zero;
        foreach (var t in pts) mean += t.position;
        mean /= pts.Count;

        // cov en XZ
        float sxx = 0, szz = 0, sxz = 0;
        foreach (var t in pts)
        {
            Vector3 d = t.position - mean;
            sxx += d.x * d.x; szz += d.z * d.z; sxz += d.x * d.z;
        }

        float T = sxx + szz;
        float D = sxx * szz - sxz * sxz;
        float lambda = 0.5f * (T + Mathf.Sqrt(Mathf.Max(0f, T * T - 4f * D)));

        Vector2 axis = (Mathf.Abs(sxz) > 1e-5f)
            ? new Vector2(lambda - szz, sxz).normalized
            : (sxx >= szz ? Vector2.right : Vector2.up);

        pts.Sort((a, b) =>
        {
            Vector2 pa = new Vector2(a.position.x - mean.x, a.position.z - mean.z);
            Vector2 pb = new Vector2(b.position.x - mean.x, b.position.z - mean.z);
            float da = Vector2.Dot(pa, axis);
            float db = Vector2.Dot(pb, axis);
            return da.CompareTo(db);
        });
    }

    // Gizmos para entender columnas y normales
    void OnDrawGizmos()
    {
        if (!drawGizmos || _coastWS.Count == 0) return;
        Gizmos.color = Color.yellow;
        for (int i = 0; i < _coastWS.Count - 1; i++)
            Gizmos.DrawLine(_coastWS[i], _coastWS[i + 1]);

        Gizmos.color = Color.cyan;
        foreach (var p in _coastWS) Gizmos.DrawSphere(p + Vector3.up * 0.2f, 10f);

        Gizmos.color = Color.magenta;
        for (int i = 0; i < _coastWS.Count; i++)
            Gizmos.DrawLine(_coastWS[i], _coastWS[i] + _dirsWS[i] * (extentMeters * gizmoNormalScale));
    }
}
