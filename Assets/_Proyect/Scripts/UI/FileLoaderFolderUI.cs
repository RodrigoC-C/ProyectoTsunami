using System;
using System.IO;
using System.Collections;                // <- NECESARIO para IEnumerator
using UnityEngine;
using UnityEngine.UI;
using SimpleFileBrowser;                 // <- plugin runtime

public class FileLoaderFolderUI : MonoBehaviour
{
    [Header("Refs")]
    public Button loadFolderButton;
    public SceneFolderController controller;

    [Header("Fallback (opcional)")]
    public string defaultRelativeFolder = "Data"; // por si cancelan el picker

    // Eventos opcionales
    public event Action onSimulationReady;
    public event Action OnLoaded;

    void Awake()
    {
        if (!loadFolderButton) loadFolderButton = GetComponent<Button>();
        if (!controller)       controller       = FindObjectOfType<SceneFolderController>();

        if (loadFolderButton)  loadFolderButton.onClick.AddListener(() => StartCoroutine(PickFolderAndLoad()));
        else Debug.LogError("[FileLoaderFolderUI] No hay Button.");

        if (controller != null) controller.onSimulationReady += HandleSimulationReady;
    }

    void OnDestroy()
    {
        if (controller != null) controller.onSimulationReady -= HandleSimulationReady;
    }

    void HandleSimulationReady() => onSimulationReady?.Invoke();

    // Abre el selector de CARPETAS (runtime)
    IEnumerator PickFolderAndLoad()
    {
        // Config opcional
        FileBrowser.SetExcludedExtensions(".lnk", ".tmp", ".ds_store");
        FileBrowser.AddQuickLink("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop), null);

        yield return FileBrowser.WaitForLoadDialog(
            allowMultiSelection: false,
            pickMode: FileBrowser.PickMode.Folders,
            initialPath: null,
            title: "Selecciona carpeta de simulación",
            loadButtonText: "Cargar"
        );

        string folder = null;

        if (FileBrowser.Success && FileBrowser.Result != null && FileBrowser.Result.Length > 0)
        {
            folder = FileBrowser.Result[0];
        }
        else
        {
            // Fallback simple: ./Data junto al .exe (por si cancelan)
            folder = Path.Combine(Directory.GetCurrentDirectory(), defaultRelativeFolder);
        }

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            Debug.LogWarning($"[FileLoaderFolderUI] Carpeta inválida: '{folder}'.");
            yield break;
        }

        controller.LoadFolder(folder);
        OnLoaded?.Invoke();

        // (Opcional) si tienes un MenuController para cambiar de panel:
        var menu = FindObjectOfType<MenuController>();
        if (menu != null) menu.ShowHUD();
    }
}
