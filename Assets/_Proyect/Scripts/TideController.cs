using UnityEngine;

public class TitleControl : MonoBehaviour
{
    [SerializeField] Transform waterPlane;   // arrastra aquí tu Plane en el inspector
    [SerializeField] float amplitude = 2f;   // altura máxima de la marea (+/- en metros)
    [SerializeField] float period = 10f;     // segundos que tarda en subir y bajar

    Vector3 basePosition;

    void Start()
    {
        if (waterPlane == null) waterPlane = transform;
        basePosition = waterPlane.position;
    }

    void Update()   
    {
        // sube y baja con un seno
        float y = Mathf.Sin(Time.time * (2 * Mathf.PI / period)) * amplitude;
        waterPlane.position = basePosition + new Vector3(0, y, 0);
    }
}

