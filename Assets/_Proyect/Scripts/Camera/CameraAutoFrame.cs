using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class CameraAutoFrame : MonoBehaviour
{
    [Header("Qué encuadrar (si lo dejas vacío, busca por nombre)")]
    public List<Transform> targets = new List<Transform>();

    [Header("Cómo encuadrar")]
    [Range(5,85)] public float elevationDeg = 45f;
    [Range(-179,180)] public float azimuthDeg = -35f;
    [Range(0f, 2f)] public float margin = 0.15f;   // % de margen alrededor
    public float minDistance = 5f;
    public float focusYOffset = 0f;

    [Header("Clipping")]
    public float nearClip = 0.1f;
    public float farClip  = 5000f;

    [Header("Hotkey")]
    public KeyCode reframeKey = KeyCode.F;

    Camera cam;

    void OnEnable()
    {
        cam = GetComponent<Camera>();
        if (cam)
        {
            cam.nearClipPlane = nearClip;
            cam.farClipPlane  = farClip;
        }
        // pequeño delay para dar tiempo a que se generen meshes en runtime
        if (Application.isPlaying)
            Invoke(nameof(ReframeNow), 0.1f);
        else
            ReframeNow();
    }

    void OnValidate()
    {
        if (!Application.isPlaying) ReframeNow();
    }

    void Update()
    {
        if (Application.isPlaying && Input.GetKeyDown(reframeKey))
            ReframeNow();
    }

    [ContextMenu("Reframe Now")]
    public void ReframeNow()
    {
        if (!cam) cam = GetComponent<Camera>();
        if (!cam) return;

        // Si no hay targets asignados intenta encontrarlos por nombre
        if (targets == null || targets.Count == 0)
        {
            TryAutoFindTargets();
        }

        if (targets == null || targets.Count == 0) return;

        // Unir bounds de todos los Renderers válidos
        bool any = false;
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);

        foreach (var t in targets)
        {
            if (!t) continue;

            Renderer r = t.GetComponent<Renderer>();
            if (!r) r = t.GetComponentInChildren<Renderer>();

            if (!r) continue;

            if (!any) { b = r.bounds; any = true; }
            else      { b.Encapsulate(r.bounds); }
        }
        if (!any) return;

        // Centro y tamaño
        var center = b.center + Vector3.up * focusYOffset;
        float radius = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
        if (radius < 0.01f) radius = 0.01f;

        // Distancia según FOV vertical y margen
        float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
        float distByFov = (radius * (1f + margin)) / Mathf.Tan(0.5f * fovRad);

        // Orientación por elevación/azimut
        Quaternion rot = Quaternion.Euler(
            Mathf.Clamp(elevationDeg, 5f, 85f),
            azimuthDeg,
            0f
        );

        Vector3 back = rot * Vector3.back; // dirección desde el centro hacia la cámara
        float distance = Mathf.Max(minDistance, distByFov);

        cam.transform.position = center - back * distance;
        cam.transform.rotation = Quaternion.LookRotation(center - cam.transform.position, Vector3.up);

        // Actualiza clips por si el volumen es grande
        cam.nearClipPlane = Mathf.Min(nearClip, Mathf.Max(0.01f, distance * 0.01f));
        cam.farClipPlane  = Mathf.Max(farClip, distance * 4f);
    }

    void TryAutoFindTargets()
    {
        // Intenta encontrar objetos típicos por nombre
        var maybe = new[] { "TopoMesh", "OceanSurface", "GeoClawPatch_Mesh1", "OceanRoot" };
        foreach (var name in maybe)
        {
            var go = GameObject.Find(name);
            if (go) {
                if (targets == null) targets = new List<Transform>();
                if (!targets.Contains(go.transform))
                    targets.Add(go.transform);
            }
        }
    }
}
