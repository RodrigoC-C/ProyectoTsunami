// ShaderSanity.cs  (ponlo en cualquier GameObject de la escena)
using UnityEngine;

public class ShaderSanity : MonoBehaviour {
  public Renderer oceanRenderer;
  public Material terrainMat;

  void Start() {
    if (oceanRenderer != null) {
      var m = oceanRenderer.sharedMaterial;
      Debug.Log($"[Sanity] Ocean mat={m?.name} shader={m?.shader?.name}");
      if (m != null) Debug.Log($"[Sanity] Has _HeightArray? {m.HasProperty("_HeightArray")}");
    }

    if (terrainMat != null) {
      Debug.Log($"[Sanity] Terrain mat={terrainMat.name} shader={terrainMat.shader?.name}");
    }
  }
}
