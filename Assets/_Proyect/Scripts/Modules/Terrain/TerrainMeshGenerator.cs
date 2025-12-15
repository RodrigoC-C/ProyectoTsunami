using UnityEngine;

public class TerrainMeshGenerator : MonoBehaviour {
    [Range(0f, 10f)] public float verticalExaggeration = 1f;
    public Material terrainMaterial; // URP/Lit por defecto si es null
    

    public GameObject Build(TopographyData topo, GeoUtil.BoundsMeters bm) {
        int rows = topo.rows, cols = topo.cols;

        var mesh = new Mesh {
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };

        var verts = new Vector3[rows * cols];
        var uvs   = new Vector2[verts.Length];

        float dx = bm.size.x / (cols - 1);
        float dz = bm.size.y / (rows - 1);

        for (int r = 0; r < rows; r++) {
            for (int c = 0; c < cols; c++) {
                int i = r * cols + c;
                float y = Mathf.Approximately(topo.h[i], topo.nodata) ? 0f : topo.h[i] * verticalExaggeration;
                verts[i] = new Vector3(c * dx, y, r * dz);
                uvs[i]   = new Vector2(c / (float)(cols - 1), r / (float)(rows - 1));
            }
        }

        var tris = new int[(rows - 1) * (cols - 1) * 6];
        int t = 0;
        for (int r = 0; r < rows - 1; r++) {
            for (int c = 0; c < cols - 1; c++) {
                int i = r * cols + c;
                tris[t++] = i; tris[t++] = i + cols + 1; tris[t++] = i + 1;
                tris[t++] = i; tris[t++] = i + cols;     tris[t++] = i + cols + 1;
            }
        }

        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("TopoMesh", typeof(MeshFilter), typeof(MeshRenderer));
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = terrainMaterial; 
        go.transform.position = bm.origin;
        return go;
    }
}
