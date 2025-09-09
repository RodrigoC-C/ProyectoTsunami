using UnityEngine;

[CreateAssetMenu(fileName = "ApiSettings", menuName = "Config/Api Settings")]
public class ApiSettings : ScriptableObject
{
    [Header("API")]
    public string baseUrl = "http://localhost:8000";
    public string framesEndpoint = "/api/frames-structured"; // GET

    [Header("Networking")]
    public int timeoutSeconds = 15;
    public bool logHttp = true;
}