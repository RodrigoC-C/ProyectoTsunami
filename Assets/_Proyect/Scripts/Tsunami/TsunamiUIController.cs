using UnityEngine;
using UnityEngine.UI;
// Si usas TextMeshPro, descomenta la siguiente línea y usa TMP_Text en vez de Text:
using TMPro;

public class TsunamiUIController : MonoBehaviour
{
    [Header("Refs")]
    public TsunamiManager manager;
    public Slider frameSlider;
    public TMP_Text labelText;      // o TMP_Text labelText;

    private bool _ready;
    public WaterMaskController maskController;

    void OnEnable()
    {
        if (manager != null)
        {
            manager.OnFramesLoaded += HandleFramesLoaded;
            manager.OnFrameChanged += HandleFrameChanged;
        }
    }
    void OnDisable()
    {
        if (manager != null)
        {
            manager.OnFramesLoaded -= HandleFramesLoaded;
            manager.OnFrameChanged -= HandleFrameChanged;
        }
    }

    void Start()
    {
        // Slider básico
        if (frameSlider != null)
        {
            frameSlider.interactable = false;
            frameSlider.wholeNumbers = true;
            frameSlider.onValueChanged.AddListener(OnSliderChanged);
        }
        if (labelText != null) labelText.text = "...";
    }

    private void HandleFramesLoaded(FramesPayload payload)
    {
        int total = payload.frames?.Count ?? 0;
        if (total <= 0) return;

        _ready = true;
        frameSlider.interactable = true;
        frameSlider.minValue = 0;
        frameSlider.maxValue = total - 1;
        frameSlider.SetValueWithoutNotify(0);

        // Inicializa texto del primer frame
        var f0 = payload.frames[0];
        if (labelText) labelText.text = $"{f0.label}";
    }

    private void HandleFrameChanged(FrameOut frame, int index, int total)
    {
        frameSlider.SetValueWithoutNotify(index);
        if (labelText) labelText.text = $"{frame.label} ";

        if (manager.visualizer != null)
            manager.visualizer.LaunchWavePatchesTowardCurrentMarkers(frame);

        if (maskController != null)
            maskController.PaintTowardFrame(frame, manager.visualizer.deepSeaOrigin.position);
    }

    private void OnSliderChanged(float value)
    {
        if (!_ready) return;
        manager.GoToIndex(Mathf.RoundToInt(value));
    }



    // Botones opcionales
    public void BtnPrev() { manager.PrevFrame(); }
    public void BtnNext() { manager.NextFrame(); }
    
}
