using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WaterMeshClipper : MonoBehaviour
{
    [Header("Referencias")]
    public CesiumGeoreference georeference;
    public Transform deepOrigin;          // Punto mar adentro (referencia para decidir lado)
    public Transform waterPlane;          // Centro/ejes del área (puede ser el mismo GO)

    [Header("Área base")]
    [Tooltip("Half-size en metros (X = ancho/2, Y = alto/2). Si es (0,0) usa el Mesh del waterPlane.")]
    public Vector2 overrideHalfSizeMeters = Vector2.zero;

    [Header("Lado a conservar")]
    [Tooltip("true = lado TIERRA (inundación). false = lado MAR.")]
    public bool keepLandSide = true;

    [Header("Alturas")]
    [Tooltip("Usar la altura WS real de cada punto para el borde del agua.")]
    public bool usePointHeights = true;

    public enum ArcHeightMode { CopyNearestFrontPoint, AverageFront, FixedY, PlaneY }
    [Tooltip("Cómo asignar altura a los vértices del arco (los que cierran contra el rectángulo).")]
    public ArcHeightMode arcHeightMode = ArcHeightMode.CopyNearestFrontPoint;
    public float fixedArcY = 0f;                  // usado si ArcHeightMode.FixedY
    public float planeYOffset = 0f;               // usado si ArcHeightMode.PlaneY (waterPlane.position.y + offset)

    [Header("Debug/Visual")]
    public bool elevateResult = true;
    public float elevateByMeters = 1.0f;
    public bool debugLog = true;
    public bool drawGizmos = true;

    // --- internos ---
    Mesh _mesh;
    Vector3 _planeCenterWS;
    Vector3 _planeRightWS;
    Vector3 _planeForwardWS;
    Vector2 _halfSize; // (x=halfWidth, y=halfHeight) en METROS (plano)

    List<Vector3> _lastFrontWS;
    List<Vector3> _lastPolyWS;

    void Awake()
    {
        if (!georeference) georeference = FindAnyObjectByType<CesiumGeoreference>();
        _mesh = new Mesh { name = "WaterClipMesh" };
        GetComponent<MeshFilter>().sharedMesh = _mesh;
        CachePlaneBasis();
    }

    void OnValidate()
    {
        if (!Application.isPlaying) CachePlaneBasis();
    }

    void CachePlaneBasis()
    {
        if (!waterPlane) waterPlane = transform;

        _planeCenterWS = waterPlane.position;
        _planeRightWS = waterPlane.right.normalized;
        _planeForwardWS = waterPlane.forward.normalized;

        if (overrideHalfSizeMeters != Vector2.zero)
        {
            _halfSize = overrideHalfSizeMeters;
            return;
        }

        var mf = waterPlane.GetComponent<MeshFilter>();
        var m = mf ? mf.sharedMesh : null;

        if (m != null)
        {
            var sz = m.bounds.size; // p.ej. Plane ~ 10x10 local
            _halfSize = new Vector2(
                0.5f * sz.x * waterPlane.lossyScale.x,
                0.5f * sz.z * waterPlane.lossyScale.z
            );
        }
        else
        {
            _halfSize = new Vector2(5000, 5000); // fallback 10 km x 10 km
        }
    }

    // ---------- API principal ----------
    public void RebuildForFrame(FrameOut frame)
    {
        _mesh.Clear();
        _lastFrontWS = null;
        _lastPolyWS = null;

        if (frame == null || frame.points == null || frame.points.Count == 0)
            return;

        CachePlaneBasis();

        // 1) Polilínea exacta: L/L/H -> WS -> coords plano (2D) + alturas WS
        var front2D = new List<Vector2>(frame.points.Count);
        var frontY = new List<float>(frame.points.Count);
        _lastFrontWS = new List<Vector3>(frame.points.Count);

        foreach (var p in frame.points)
        {
            double3 ecef = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                new double3(p.lon, p.lat, p.h));
            double3 u = georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            Vector3 ws = new Vector3((float)u.x, (float)u.y, (float)u.z);

            front2D.Add(WorldToPlane2D(ws));
            frontY.Add(ws.y);
            _lastFrontWS.Add(ws);
        }

        if (front2D.Count == 1) { BuildFullRect(frontY[0]); return; }

        // 2) Ordenar a lo largo de la costa (eje dominante)
        SortAlongCoast(front2D, frontY);

        // 3) Cosido “exacto por puntos” y selección de lado
        Vector2 deep2D = WorldToPlane2D(deepOrigin ? deepOrigin.position
                                                   : (_planeCenterWS - _planeForwardWS * 5000f));

        // Extensiones de extremos: para LADO TIERRA queremos ir en dirección contraria a "hacia el mar".
        // Dirección base "mar afuera" = (P - deep2D); para tierra usamos (deep2D - P).
        Vector2 P0 = front2D[0];
        Vector2 PN = front2D[front2D.Count - 1];

        Vector2 dir0 = keepLandSide ? (deep2D - P0).normalized : (P0 - deep2D).normalized;
        Vector2 dirN = keepLandSide ? (deep2D - PN).normalized : (PN - deep2D).normalized;

        Vector2 I0; int side0; I0 = RayRectIntersection(P0, dir0, _halfSize, out side0);
        Vector2 IN; int sideN; IN = RayRectIntersection(PN, dirN, _halfSize, out sideN);

        // Arcos del rectángulo CW/CCW
        var arcCW = BuildRectArc(I0, side0, IN, sideN, cw: true);
        var arcCCW = BuildRectArc(I0, side0, IN, sideN, cw: false);

        // Alturas para el arco según modo
        float yFirst = frontY[0];
        float yLast = frontY[frontY.Count - 1];
        float yAvg = 0.5f * (yFirst + yLast);
        float yPlane = waterPlane.position.y + planeYOffset;

        List<float> arcCW_Y = BuildArcHeights(arcCW, front2D, frontY, yFirst, yLast, yAvg, yPlane);
        List<float> arcCCW_Y = BuildArcHeights(arcCCW, front2D, frontY, yFirst, yLast, yAvg, yPlane);

        // Polígonos candidatos = arco + polilínea (reversa)
        var polyCW = new List<Vector2>(arcCW.Count + front2D.Count);
        var polyCCW = new List<Vector2>(arcCCW.Count + front2D.Count);
        var polyCW_Y = new List<float>(arcCW.Count + front2D.Count);
        var polyCCW_Y = new List<float>(arcCCW.Count + front2D.Count);

        polyCW.AddRange(arcCW); polyCW_Y.AddRange(arcCW_Y);
        polyCCW.AddRange(arcCCW); polyCCW_Y.AddRange(arcCCW_Y);

        for (int i = front2D.Count - 1; i >= 0; i--)
        {
            polyCW.Add(front2D[i]);
            polyCW_Y.Add(usePointHeights ? frontY[i] : yPlane);

            polyCCW.Add(front2D[i]);
            polyCCW_Y.Add(usePointHeights ? frontY[i] : yPlane);
        }

        // Elegimos el polígono por la relación con deepOrigin:
        // keepLandSide=true -> elegimos el que NO contiene deepOrigin (lado tierra).
        bool cwContainsDeep = PointInPolygon(deep2D, polyCW);
        var chosen2D = (keepLandSide ? (!cwContainsDeep ? polyCW : polyCCW)
                                     : (cwContainsDeep ? polyCW : polyCCW));
        var chosenY = (keepLandSide ? (!cwContainsDeep ? polyCW_Y : polyCCW_Y)
                                     : (cwContainsDeep ? polyCW_Y : polyCCW_Y));

        // 4) Triangulación y aplicar (usando alturas por vértice)
        TriangulateAndApply(chosen2D, chosenY);
    }

    // ---------- proyección helpers ----------
    Vector2 WorldToPlane2D(Vector3 ws)
    {
        Vector3 to = ws - _planeCenterWS;
        float u = Vector3.Dot(to, _planeRightWS);
        float v = Vector3.Dot(to, _planeForwardWS);
        return new Vector2(u, v);
    }

    Vector3 Plane2DToWorld(Vector2 uv, float y)
    {
        var w = _planeCenterWS + _planeRightWS * uv.x + _planeForwardWS * uv.y;
        w.y = y;
        if (elevateResult) w.y += elevateByMeters;
        return w;
    }

    // ---------- ordenar puntos sobre el eje dominante (PCA mínima) ----------
    void SortAlongCoast(List<Vector2> pts, List<float> ys)
    {
        Vector2 mean = Vector2.zero;
        foreach (var p in pts) mean += p;
        mean /= pts.Count;

        float sxx = 0, syy = 0, sxy = 0;
        foreach (var p in pts)
        {
            Vector2 d = p - mean;
            sxx += d.x * d.x; syy += d.y * d.y; sxy += d.x * d.y;
        }

        float T = sxx + syy;
        float D = sxx * syy - sxy * sxy;
        float lambda = 0.5f * (T + Mathf.Sqrt(Mathf.Max(0f, T * T - 4f * D)));

        Vector2 axis = (Mathf.Abs(sxy) > 1e-5f)
            ? new Vector2(lambda - syy, sxy).normalized
            : (sxx >= syy ? Vector2.right : Vector2.up);

        // ordenar pts y sus alturas de forma acoplada
        var idx = new List<int>(pts.Count);
        for (int i = 0; i < pts.Count; i++) idx.Add(i);
        idx.Sort((a, b) => Vector2.Dot(pts[a] - mean, axis).CompareTo(Vector2.Dot(pts[b] - mean, axis)));

        var pts2 = new List<Vector2>(pts.Count);
        var y2 = new List<float>(pts.Count);
        foreach (var i in idx) { pts2.Add(pts[i]); y2.Add(ys[i]); }
        pts.Clear(); pts.AddRange(pts2);
        ys.Clear(); ys.AddRange(y2);
    }

    // ---------- intersección rayo-rectángulo ----------
    // sides: 0=left(-x), 1=bottom(-y), 2=right(+x), 3=top(+y)
    Vector2 RayRectIntersection(Vector2 p, Vector2 dir, Vector2 half, out int side)
    {
        side = -1;
        float tMin = float.PositiveInfinity;
        Vector2 hit = p;

        if (Mathf.Abs(dir.x) > 1e-6f)
        {
            float t = (-half.x - p.x) / dir.x; // left
            float y = p.y + t * dir.y;
            if (t > 0 && y >= -half.y - 1e-6f && y <= half.y + 1e-6f && t < tMin) { tMin = t; hit = new Vector2(-half.x, y); side = 0; }

            t = (half.x - p.x) / dir.x; // right
            y = p.y + t * dir.y;
            if (t > 0 && y >= -half.y - 1e-6f && y <= half.y + 1e-6f && t < tMin) { tMin = t; hit = new Vector2(half.x, y); side = 2; }
        }
        if (Mathf.Abs(dir.y) > 1e-6f)
        {
            float t = (-half.y - p.y) / dir.y; // bottom
            float x = p.x + t * dir.x;
            if (t > 0 && x >= -half.x - 1e-6f && x <= half.x + 1e-6f && t < tMin) { tMin = t; hit = new Vector2(x, -half.y); side = 1; }

            t = (half.y - p.y) / dir.y; // top
            x = p.x + t * dir.x;
            if (t > 0 && x >= -half.x - 1e-6f && x <= half.x + 1e-6f && t < tMin) { tMin = t; hit = new Vector2(x, half.y); side = 3; }
        }
        return hit;
    }

    // ---------- construir arco del rectángulo entre dos choques ----------
    // cw=true recorre lados 0->1->2->3->0 en sentido horario empezando en sideStart hasta sideEnd.
    List<Vector2> BuildRectArc(Vector2 Istart, int sideStart, Vector2 Iend, int sideEnd, bool cw)
    {
        var arc = new List<Vector2>();
        arc.Add(Istart);

        Vector2[] corners = new Vector2[4] {
            new Vector2(-_halfSize.x, -_halfSize.y), // 0 (left-bottom)
            new Vector2( _halfSize.x, -_halfSize.y), // 1 (right-bottom)
            new Vector2( _halfSize.x,  _halfSize.y), // 2 (right-top)
            new Vector2(-_halfSize.x,  _halfSize.y)  // 3 (left-top)
        };

        int Next(int s, bool clockwise) => clockwise ? (s + 3) & 3 : (s + 1) & 3;

        int s = sideStart;
        while (s != sideEnd)
        {
            int cornerIdx = cw ? (s + 3) & 3 : (s + 1) & 3;
            arc.Add(corners[cornerIdx]);
            s = Next(s, cw);
        }

        arc.Add(Iend);
        return arc;
    }

    // ---------- alturas para el arco ----------
    List<float> BuildArcHeights(List<Vector2> arc, List<Vector2> front2D, List<float> frontY, float yFirst, float yLast, float yAvg, float yPlane)
    {
        var ys = new List<float>(arc.Count);
        for (int i = 0; i < arc.Count; i++)
        {
            float y;
            switch (arcHeightMode)
            {
                case ArcHeightMode.CopyNearestFrontPoint:
                    int k = NearestIndex2D(arc[i], front2D);
                    y = usePointHeights ? frontY[k] : yPlane;
                    break;
                case ArcHeightMode.AverageFront:
                    y = usePointHeights ? yAvg : yPlane;
                    break;
                case ArcHeightMode.FixedY:
                    y = fixedArcY;
                    break;
                default: // PlaneY
                    y = yPlane;
                    break;
            }
            ys.Add(y);
        }
        return ys;
    }

    int NearestIndex2D(Vector2 p, List<Vector2> pts)
    {
        int best = 0;
        float bestD2 = float.MaxValue;
        for (int i = 0; i < pts.Count; i++)
        {
            float d2 = (pts[i] - p).sqrMagnitude;
            if (d2 < bestD2) { bestD2 = d2; best = i; }
        }
        return best;
    }

    // ---------- punto en polígono (ray casting) ----------
    bool PointInPolygon(Vector2 p, List<Vector2> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var pi = poly[i]; var pj = poly[j];
            bool intersect = ((pi.y > p.y) != (pj.y > p.y)) &&
                             (p.x < (pj.x - pi.x) * (p.y - pi.y) / Mathf.Max(1e-6f, (pj.y - pi.y)) + pi.x);
            if (intersect) inside = !inside;
        }
        return inside;
    }

    // ---------- rect completo (fallback) ----------
    void BuildFullRect(float y)
    {
        var rect = new List<Vector2>(4)
        {
            new Vector2(-_halfSize.x, -_halfSize.y),
            new Vector2( _halfSize.x, -_halfSize.y),
            new Vector2( _halfSize.x,  _halfSize.y),
            new Vector2(-_halfSize.x,  _halfSize.y),
        };
        var ys = new List<float> { y, y, y, y };
        TriangulateAndApply(rect, ys);
    }

    // ---------- triangulación y aplicación ----------
    void TriangulateAndApply(List<Vector2> poly, List<float> polyY)
    {
        if (poly == null || poly.Count < 3) { _mesh.Clear(); return; }

        var vertsWS = new List<Vector3>(poly.Count);
        for (int i = 0; i < poly.Count; i++)
            vertsWS.Add(Plane2DToWorld(poly[i], polyY[i]));

        _lastPolyWS = new List<Vector3>(vertsWS);

        int n = poly.Count;
        var tris = new List<int>((n - 2) * 3);
        for (int i = 1; i < n - 1; i++) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }

        var t = transform;
        for (int i = 0; i < vertsWS.Count; i++)
            vertsWS[i] = t.InverseTransformPoint(vertsWS[i]);

        _mesh.Clear();
        _mesh.SetVertices(vertsWS);
        _mesh.SetTriangles(tris, 0, true);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        if (debugLog) Debug.Log($"[WaterMeshClipper] poly={n} verts={_mesh.vertexCount} tris={tris.Count / 3}");
    }

    // ---------- gizmos ----------
    void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        CachePlaneBasis();

        // rect base
        Gizmos.color = Color.cyan;
        Vector3 c = _planeCenterWS;
        Vector3 rx = _planeRightWS * _halfSize.x;
        Vector3 fz = _planeForwardWS * _halfSize.y;
        Gizmos.DrawLine(c - rx - fz, c + rx - fz);
        Gizmos.DrawLine(c + rx - fz, c + rx + fz);
        Gizmos.DrawLine(c + rx + fz, c - rx + fz);
        Gizmos.DrawLine(c - rx + fz, c - rx - fz);

        // frente
        if (_lastFrontWS != null && _lastFrontWS.Count > 1)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < _lastFrontWS.Count - 1; i++)
                Gizmos.DrawLine(_lastFrontWS[i], _lastFrontWS[i + 1]);
        }

        // polígono resultante
        if (_lastPolyWS != null && _lastPolyWS.Count > 1)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < _lastPolyWS.Count; i++)
                Gizmos.DrawLine(_lastPolyWS[i], _lastPolyWS[(i + 1) % _lastPolyWS.Count]);
        }
    }
}
