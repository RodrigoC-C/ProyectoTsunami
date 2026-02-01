using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    public Transform pivot;       // asigna TerrainRoot
    public float distance = 500f;
    public float minDistance = 5f;
    public float maxDistance = 5000f;
    public float azimuthDeg = -30f;   // yaw
    public float elevationDeg = 30f;  // pitch
    public float zoomSpeed = 50f;

    Camera _cam;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        if (!_cam) _cam = Camera.main;
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
        UpdateCamera();
    }

    void LateUpdate()
    {
        // si tienes input, actualiza azimuth/elevation/distance aquí…
        UpdateCamera();
    }

    void UpdateCamera()
    {
        if (!pivot) return;

        // Spherical -> Cartesian
        float yaw   = azimuthDeg   * Mathf.Deg2Rad;
        float pitch = elevationDeg * Mathf.Deg2Rad;

        // Vector “hacia atrás” de la cámara respecto al pivot
        Vector3 dir =
            new Vector3(
                Mathf.Cos(pitch) * Mathf.Cos(yaw),
                Mathf.Sin(pitch),
                Mathf.Cos(pitch) * Mathf.Sin(yaw)
            );

        // Posicionar
        Vector3 camPos = pivot.position + (-dir * Mathf.Clamp(distance, minDistance, maxDistance));
        transform.position = camPos;

        // *** Forzar look-at al pivot ***
        transform.rotation = Quaternion.LookRotation(pivot.position - camPos, Vector3.up);
    }

    // Llamado por SceneController al cargar/encuadrar
    public void Focus(Bounds b, Transform space)
    {
        if (!pivot) return;

        // Centro del bounds en mundo
        Vector3 centerW = space ? space.TransformPoint(b.center) : b.center;
        pivot.position = centerW;

        // Distancia para encuadrar el bounds
        float radius = b.extents.magnitude;              // radio aprox.
        float fovRad = (_cam ? _cam.fieldOfView : 60f) * Mathf.Deg2Rad;
        float dist   = radius / Mathf.Sin(Mathf.Max(0.1f, fovRad * 0.5f)); // margen
        distance = Mathf.Clamp(dist * 1.2f, minDistance, maxDistance);

        // Ángulos “seguros”
        azimuthDeg = -30f;
        elevationDeg = -30f;

        UpdateCamera();
    }
}
