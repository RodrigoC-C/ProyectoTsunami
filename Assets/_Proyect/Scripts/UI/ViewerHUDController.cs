using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ViewerHUDController : MonoBehaviour
{
    public HeightArrayPlayback playback;
    public Button btnPlay, btnPause, btnRestart;
    public Slider sliderTiempo;
    public TextMeshProUGUI txtTiempo;

    void OnEnable()
    {
        if (playback != null)
            playback.OnSliceChanged += HandleSliceChanged;
    }

    void OnDisable()
    {
        if (playback != null)
            playback.OnSliceChanged -= HandleSliceChanged;
    }

    void Start()
    {
        // Botones
        if (btnPlay) btnPlay.onClick.AddListener(() => {
            if (playback == null) return;
            playback.ExternalDrive = false;   // <- clave: usa timeline interno
            playback.Play();
        });

        if (btnPause) btnPause.onClick.AddListener(() => {
            if (playback == null) return;
            playback.Pause();
        });

        if (btnRestart) btnRestart.onClick.AddListener(() => {
            if (playback == null) return;
            playback.ExternalDrive = false;   // reinicia y usa timeline interno
            playback.Stop();
            playback.Play();
        });

        // Slider
        if (sliderTiempo)
        {
            sliderTiempo.wholeNumbers = true;
            sliderTiempo.onValueChanged.AddListener(OnSliderChanged);
            RefreshSliderBounds(); // por si ya hay array cargado
        }
    }

    void Update()
    {
        if (!playback) return;

        // Si estamos en timeline interno, refleja el frame actual en el slider
        if (sliderTiempo && !playback.ExternalDrive)
            sliderTiempo.SetValueWithoutNotify(playback.CurrentFrame);

        if (txtTiempo)
            txtTiempo.text = $"t = {playback.CurrentTimeSeconds:0.0}s  (frame {playback.CurrentFrame}/{Mathf.Max(0, playback.FrameCount-1)})";
    }

    void OnSliderChanged(float v)
    {
        if (!playback) return;
        // Cuando mueves el slider, tú controlas el frame (timeline externo)
        playback.ExternalDrive = true;
        playback.SetFrame((int)v);
    }

    void HandleSliceChanged(int _)
    {
        // Este callback ocurre cuando se aplica un slice; aquí podemos
        // reconfigurar el slider si el FrameCount cambió (post-carga).
        RefreshSliderBounds();
    }

    void RefreshSliderBounds()
    {
        if (!sliderTiempo || playback == null) return;
        int max = Mathf.Max(0, playback.FrameCount - 1);
        sliderTiempo.minValue = 0;
        sliderTiempo.maxValue = max;
        sliderTiempo.SetValueWithoutNotify(Mathf.Clamp(sliderTiempo.value, 0, max));
    }
}
