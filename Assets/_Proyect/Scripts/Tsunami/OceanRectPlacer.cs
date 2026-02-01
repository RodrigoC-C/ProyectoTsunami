using UnityEngine;
using Unity.Mathematics;      // <- aquí vive double3
using CesiumForUnity;

/// Coloca y escala un plano 1x1 (UV 0..1) para cubrir el bbox de tu simulación.
/// No usa APIs avanzadas de Cesium; sólo el anchor y una aproximación métrica por grado.
/// Requisitos:
/// - Este script en "OceanRoot"
/// - "OceanRoot" con CesiumGlobeAnchor
/// - targetPlane = hijo con la malla (1x1 en XZ, centrada)
[ExecuteAlways]
public class OceanRectPlacer : MonoBehaviour
{
    [Header("Bounding Box de la simulación (grados)")]
    public double latSouth; // lat mínima
    public double lonWest;  // lon mínima
    public double latNorth; // lat máxima
    public double lonEast;  // lon máxima

    [Header("Altura sobre el elipsoide (m)")]
    public double heightMeters = 0.0;

    [Header("Referencias")]
    public CesiumGlobeAnchor anchor;   // este GameObject (OceanRoot)
    public Transform targetPlane;      // OceanSurface (malla 1x1 en XZ)

    private void Reset()
    {
        if (!anchor) anchor = GetComponent<CesiumGlobeAnchor>();
    }

    [ContextMenu("Apply Placement (Lite)")]
    public void ApplyPlacement()
    {
        if (!anchor || !targetPlane)
        {
            Debug.LogWarning("[OceanRectPlacer] Falta anchor/targetPlane.");
            return;
        }

        // Normaliza por si vienen invertidos
        double south = math.min(latSouth, latNorth);
        double north = math.max(latSouth, latNorth);
        double west  = math.min(lonWest , lonEast );
        double east  = math.max(lonWest , lonEast );

        // Centro del bbox
        double latC = 0.5 * (south + north);
        double lonC = 0.5 * (west  + east );

        // === Escala en METROS usando aproximación por grado ===
        // Longitud de 1° de latitud ~ 110.574 km
        // Longitud de 1° de longitud ~ 111.320 km * cos(lat)
        double dLatDeg = (north - south);
        double dLonDeg = (east  - west );

        double metersPerDegLat = 110_574.0;
        double metersPerDegLon = 111_320.0 * math.cos(math.radians(latC));

        double heightMetersBBox = dLatDeg * metersPerDegLat; // Sur→Norte
        double widthMetersBBox  = dLonDeg * metersPerDegLon; // Oeste→Este

        // === Anclar al centro en coordenadas cartográficas (grados) ===
        // Esta propiedad existe en todas las versiones recientes:
        anchor.longitudeLatitudeHeight = new double3(lonC, latC, heightMeters);

        // === Alinear y escalar el plano 1x1 a metros reales, ENU local (X=Este, Z=Norte) ===
        targetPlane.localPosition = Vector3.zero;
        targetPlane.localRotation = Quaternion.identity;
        targetPlane.localScale    = new Vector3((float)widthMetersBBox, 1f, (float)heightMetersBBox);

#if UNITY_EDITOR
        Debug.Log($"[OceanRectPlacer] {widthMetersBBox:0.0}m x {heightMetersBBox:0.0}m @ lat:{latC:F6}, lon:{lonC:F6}");
#endif
    }

#if UNITY_EDITOR
    // Gizmo para ver el rectángulo en el Editor (aprox) cuando seleccionas el objeto
    private void OnDrawGizmosSelected()
    {
        if (!targetPlane) return;
        Gizmos.color = new Color(0, 0.6f, 1f, 0.35f);
        var s = targetPlane.localScale;
        var p = targetPlane.position;
        var r = targetPlane.rotation;

        Vector3 hx = r * new Vector3( 0.5f * s.x, 0, 0);
        Vector3 hz = r * new Vector3(0, 0,  0.5f * s.z);

        Vector3 c  = p; // centro
        Vector3 sw = c - hx - hz;
        Vector3 se = c + hx - hz;
        Vector3 ne = c + hx + hz;
        Vector3 nw = c - hx + hz;

        Gizmos.DrawLine(sw,se);
        Gizmos.DrawLine(se,ne);
        Gizmos.DrawLine(ne,nw);
        Gizmos.DrawLine(nw,sw);
    }
#endif
}
