using UnityEngine;

public static class GeoUtil {
    const double R = 6378137.0;

    public struct BoundsMeters {
        public Vector2 size;    // (widthX, heightZ) en metros
        public Vector3 origin;  // origen local (0,0,0 para simplificar)
    }

    public static BoundsMeters BBoxToLocalMeters(BBox b) {
        double lat0 = (b.minLat + b.maxLat) * 0.5 * Mathf.Deg2Rad;
        double dLon = (b.maxLon - b.minLon) * Mathf.Deg2Rad;
        double dLat = (b.maxLat - b.minLat) * Mathf.Deg2Rad;
        float width  = (float)(R * System.Math.Cos(lat0) * dLon);
        float height = (float)(R * dLat);
        return new BoundsMeters { size = new Vector2(width, height), origin = Vector3.zero };
    }
}