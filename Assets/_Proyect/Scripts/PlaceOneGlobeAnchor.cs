// Assets/Scripts/PlaceOneGlobeAnchor.cs
using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;

[ExecuteAlways]
public class PlaceOneGlobeAnchor : MonoBehaviour
{
    [Header("WGS84 (grados) + altura elipsoide (m)")]
    public double longitudeDeg = -71.628; // Valparaíso
    public double latitudeDeg = -33.045;
    public double heightM = 20.0;

    [Header("Opcional: centrar origen para precisión")]
    public bool centerOriginNearPoint = true;

    void OnEnable() => Apply();
    void OnValidate() => Apply();

    [ContextMenu("Teleport now")]
    public void Apply()
    {
        // 1) Asegura un (1) CesiumGeoreference activo
        var refs = FindObjectsByType<CesiumGeoreference>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (refs.Length == 0) { Debug.LogError("No hay CesiumGeoreference en escena."); return; }
        if (refs.Length > 1) { Debug.LogWarning($"Hay {refs.Length} CesiumGeoreference. Deja solo uno activo."); }
        var geo = refs[0];

        // 2) Asegura el ancla en EL MISMO OBJETO que quieres mover (no en el padre equivocado)
        var anchor = GetComponent<CesiumGlobeAnchor>();
        if (!anchor) anchor = gameObject.AddComponent<CesiumGlobeAnchor>();

        // 4) Teletransporta por LON/LAT/ALT (NO uses transform.position aquí)
        anchor.longitudeLatitudeHeight = new double3(longitudeDeg, latitudeDeg, heightM);

        // 5) Mensaje útil
        Debug.Log($"[TeleportLLA] Movido a lon={longitudeDeg:F6}, lat={latitudeDeg:F6}, h={heightM:F2}");
    }
}
