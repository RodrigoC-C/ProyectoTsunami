using UnityEngine;

[ExecuteAlways]
public class HighResPlaneGenerator : MonoBehaviour
{
    [Range(2,2048)] public int segmentsX = 250;
    [Range(2,2048)] public int segmentsZ = 250;

    void OnEnable() { Build(); }
    void OnValidate() { Build(); }

    void Build()
    {
        var mf = GetComponent<MeshFilter>() ?? gameObject.AddComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>() ?? gameObject.AddComponent<MeshRenderer>();

        int vx = segmentsX + 1, vz = segmentsZ + 1;
        var verts = new Vector3[vx*vz];
        var uvs   = new Vector2[verts.Length];
        var tris  = new int[segmentsX*segmentsZ*6];

        for (int z=0; z<vz; z++)
        for (int x=0; x<vx; x++) {
            int i = z*vx + x;
            float fx = (x/(float)segmentsX) - 0.5f;
            float fz = (z/(float)segmentsZ) - 0.5f;
            verts[i] = new Vector3(fx,0,fz);
            uvs[i]   = new Vector2(x/(float)segmentsX, z/(float)segmentsZ);
        }

        int t=0;
        for (int z=0; z<segmentsZ; z++)
        for (int x=0; x<segmentsX; x++) {
            int i=z*vx+x;
            tris[t++]=i; tris[t++]=i+vx; tris[t++]=i+1;
            tris[t++]=i+1; tris[t++]=i+vx; tris[t++]=i+vx+1;
        }

        var m = new Mesh();
        m.indexFormat = (verts.Length>65535) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        m.vertices=verts; m.uv=uvs; m.triangles=tris;
        m.RecalculateNormals(); m.RecalculateBounds();
        mf.sharedMesh = m;
    }
}
