using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class APIClient : MonoBehaviour
{
    void Start()
    {
        StartCoroutine(GetDataFromAPI());
    }

    IEnumerator GetDataFromAPI()
    {
        string url = "http://127.0.0.1:8000/tsunami"; // Aquí va tu endpoint
        UnityWebRequest request = UnityWebRequest.Get(url);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Respuesta: " + request.downloadHandler.text);
            // Aquí podrías deserializar JSON y convertirlo en objetos de C#
        }
        else
        {
            Debug.LogError("Error en API: " + request.error);
        }
    }

    
}

