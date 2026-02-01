using UnityEngine;

public class MenuController : MonoBehaviour
{
    [Header("UI")]
    public GameObject panelInicio;
    public GameObject panelHUD;

    [Header("Pipelines")]
    public SceneFolderController folderLoader;

    void OnEnable()
    {
        if (!folderLoader) folderLoader = FindObjectOfType<SceneFolderController>();
        if (folderLoader != null) folderLoader.OnSimulationReady += HandleReady;
    }

    void OnDisable()
    {
        if (folderLoader != null) folderLoader.OnSimulationReady -= HandleReady;
    }

    void HandleReady() => ShowHUD();

    // === NUEVO ===
    public void ShowHUD()
    {
        if (panelInicio) panelInicio.SetActive(false);
        if (panelHUD)    panelHUD.SetActive(true);
    }

    // (opcional, por si vuelves al menú)
    public void ShowStart()
    {
        if (panelHUD)    panelHUD.SetActive(false);
        if (panelInicio) panelInicio.SetActive(true);
    }
}
