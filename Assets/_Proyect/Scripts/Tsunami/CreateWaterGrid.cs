using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class CreateWaterGrid : MonoBehaviour
{
    [Header("Tamaño en metros (dominio fgout)")]
    public float widthMeters  = 28000f; // eje X
    public float heightMeters = 27800f; // eje Z

    [Header("Resolución de la malla (subdivisiones)")]
    public int nx = 512; // celdas en X
    public int ny = 512; // celdas en Z

    [Header("Material de agua (shader que desplaza con eta)")]
    public Material waterMaterial;

    Mesh _mesh;

    void OnEnable()  => Build();
    void OnValidate()=> Build();

    void Build()
    {
        nx = Mathf.Max(1, nx);
        ny = Mathf.Max(1, ny);
        widthMeters  = Mathf.Max(1f, widthMeters);
        heightMeters = Mathf.Max(1f, heightMeters);

        var mf = GetComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();
        if (waterMaterial) mr.sharedMaterial = waterMaterial;

        if (_mesh == null)
        {
            _mesh = new Mesh();
            _mesh.name = "WaterGrid";
            _mesh.indexFormat = (nx * ny > 65000) ? UnityEngine.Rendering.IndexFormat.UInt32
                                                  : UnityEngine.Rendering.IndexFormat.UInt16;
            mf.sharedMesh = _mesh;
        }
        _mesh.Clear();

        int vxCountX = nx + 1;
        int vxCountZ = ny + 1;
        int vCount   = vxCountX * vxCountZ;

        var verts = new Vector3[vCount];
        var uvs   = new Vector2[vCount];
        var tris  = new int[nx * ny * 6];

        // Centro en (0,0,0) (pivot centrado)
        float halfW = widthMeters * 0.5f;
        float halfH = heightMeters * 0.5f;

        for (int j = 0; j < vxCountZ; j++)
        {
            float v = j / (float)ny;
            float z = Mathf.Lerp(-halfH, halfH, v);
            for (int i = 0; i < vxCountX; i++)
            {
                float u = i / (float)nx;
                float x = Mathf.Lerp(-halfW, halfW, u);

                int idx = j * vxCountX + i;
                verts[idx] = new Vector3(x, 0f, z); // y=0, luego el shader la sube/baja
                uvs[idx]   = new Vector2(u, 1f - v); // V invertida para “norte arriba”
            }
        }

        int t = 0;
        for (int j = 0; j < ny; j++)
        {
            for (int i = 0; i < nx; i++)
            {
                int i0 =  j      * vxCountX + i;
                int i1 =  j      * vxCountX + (i + 1);
                int i2 = (j + 1) * vxCountX + i;
                int i3 = (j + 1) * vxCountX + (i + 1);

                // triángulo 1
                tris[t++] = i0; tris[t++] = i2; tris[t++] = i1;
                // triángulo 2
                tris[t++] = i1; tris[t++] = i2; tris[t++] = i3;
            }
        }

        _mesh.vertices  = verts;
        _mesh.uv        = uvs;
        _mesh.triangles = tris;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        // Importante: deja transform.scale = (1,1,1) para que 1 unidad = 1 m
        transform.localScale = Vector3.one;
    }

    // Dibuja un recuadro en la escena
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        var c = transform.position;
        Vector3 a = c + new Vector3(-widthMeters/2, 0, -heightMeters/2);
        Vector3 b = c + new Vector3( widthMeters/2, 0, -heightMeters/2);
        Vector3 d = c + new Vector3(-widthMeters/2, 0,  heightMeters/2);
        Vector3 e = c + new Vector3( widthMeters/2, 0,  heightMeters/2);
        Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, e); Gizmos.DrawLine(e, d); Gizmos.DrawLine(d, a);
    }
}
