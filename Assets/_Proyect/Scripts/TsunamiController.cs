using UnityEngine;

public class TsunamiController : MonoBehaviour
{
    [Header("References")]
    public Transform waterPlane;   // Quad horizontal (Rotation.x = -90)
    public Transform coastTarget;  // Punto hacia donde avanza el frente (opcional)

    [Header("Physics-ish (simple)")]
    [Tooltip("Profundidad mar profundo (m). Ej: 4000")]
    public float oceanDepth = 4000f;
    [Tooltip("Profundidad cerca de costa (m). Ej: 50")]
    public float coastalDepth = 50f;
    [Tooltip("Multiplicador para calibrar la velocidad de avance del frente")]
    public float waveFrontSpeedMultiplier = 20f; // 0 para probar nivel sin movimiento

    [Header("Flood / Sea Level")]
    [Tooltip("Offset del nivel del mar respecto a su altura base (m)")]
    public float seaLevelOffsetMeters = 0f;
    [Tooltip("Cuántos metros reales equivalen a 1 unidad Y del plano")]
    public float verticalScaleFactor = 1f;

    // --- Internos ---
    const float g = 9.81f;
    float vDeep;      // velocidad teórica en mar profundo (m/s)
    float targetH;    // altura visual objetivo al acercarse a costa (escala Y)
    float baseSeaLevelWorldY; // <-- altura base del agua en coordenadas de mundo (para offset relativo)
    float initialWaveHeight;

    [Header("Run Control")]
    public bool stopAtTarget = true;
    public float stopDistance = 2f;

    private bool _running;


    void Start()
    {
        if (!waterPlane)
        {
            Debug.LogWarning("[TsunamiController] Falta asignar 'waterPlane'.");
            enabled = false;
            return;
        }

        // Guarda la altura inicial del agua en MUNDO (offset relativo a partir de aquí)
        baseSeaLevelWorldY = waterPlane.position.y;
        initialWaveHeight = waterPlane.localScale.y;

        // Parámetros “físicos” simples
        vDeep = Mathf.Sqrt(g * Mathf.Max(1f, oceanDepth)); // m/s
        targetH = initialWaveHeight *
                  Mathf.Sqrt(Mathf.Max(1f, oceanDepth) / Mathf.Max(1f, coastalDepth));
    }

    public void BeginTowards(Transform target, Vector3 startWorldPos)
    {
        coastTarget = target;
        transform.position = startWorldPos;
        _running = true;
    }

    public void StopRun()
    {
        _running = false;
    }

    void Update()
    {
        if (!waterPlane) return;

        if (_running && coastTarget && waveFrontSpeedMultiplier > 0f)
        {
            Vector3 targetPos = new Vector3(
                coastTarget.position.x,
                transform.position.y, // mantenemos nivel del agua
                coastTarget.position.z
            );

            // Si ya estoy dentro del radio, me pego EXACTO y paro
            if ((transform.position - targetPos).sqrMagnitude <= stopDistance * stopDistance)
            {
                transform.position = targetPos;  // <--- SNAP EXACTO
                _running = false;
            }
            else
            {
                float speed = vDeep * waveFrontSpeedMultiplier;
                transform.position = Vector3.MoveTowards(transform.position, targetPos, speed * Time.deltaTime);
            }
        }

        // Altura visual (como ya tenías)
        if (coastTarget)
        {
            float d = Vector3.Distance(transform.position, coastTarget.position);
            float lerp = Mathf.InverseLerp(5000f, 50f, d);
            float H = Mathf.Lerp(initialWaveHeight, targetH, 1f - lerp);

            var s = waterPlane.localScale;
            s.y = Mathf.Max(0.1f, H);
            waterPlane.localScale = s;
        }
    }

        

}
