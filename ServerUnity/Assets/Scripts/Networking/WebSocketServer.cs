// UnityWebSocketServer.cs
// Attach this component to a GameObject
using System;
using System.IO;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using UnityEngine;
using WebSocketSharp.Server;

[DisallowMultipleComponent]
public class UnityWebSocketServer : MonoBehaviour
{
    [SerializeField] private int port = 4001;

    // Absolute path to a PFX/PKCS#12 with private key (CN/SAN must match the DNS you connect with)
    [SerializeField] private string certificatePath = "C:\\Windows\\System32\\holo-unity-wss.pfx";
    [SerializeField] private string certificatePassword = "root";

    private WebSocketServer server;

    // --- Singleton ---
    public static UnityWebSocketServer Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        try
        {
            // secure:true => WSS
            server = new WebSocketServer(port, true);
            //var certPath = Path.Combine(Application.streamingAssetsPath, certificatePath);

            //if (!File.Exists(certPath))
            //    throw new FileNotFoundException($"Certificate not found at path: {certPath}");

            // 1) Load PFX as TextAsset from Resources
            var pfxAsset = Resources.Load<TextAsset>(certificatePath);
            if (pfxAsset == null)
                throw new InvalidOperationException(
                    $"PFX not found in Resources at '{certificatePath}'. " +
                    "Place your PFX as Assets/Resources/<path>.pfx.bytes and import as TextAsset.");

            // 2) Create certificate directly from bytes (no temp file needed)
            //    EphemeralKeySet avoids writing private key to disk; Exportable helps some stacks.
            var cert = new X509Certificate2(
                pfxAsset.bytes,
                certificatePassword,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);

            server.SslConfiguration.ServerCertificate = cert;

            // WebSocketSharp typically supports up to TLS 1.2
            server.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls12;
            server.SslConfiguration.CheckCertificateRevocation = false;

            // Optional: if you’re behind a proxy / TLS terminator
            server.AllowForwardedRequest = true;

            // Register your behavior end-point
            server.AddWebSocketService<RegistrationBehavior>("/register");

            server.Start();
            Debug.Log($"WSS server started on wss://0.0.0.0:{port}/register (connect using a DNS name that matches the cert).");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to start WebSocket server: {ex}");
        }
    }

    private void OnApplicationQuit() => StopServer();
    private void OnDestroy() => StopServer();

    private void StopServer()
    {
        if (server != null)
        {
            try
            {
                if (server.IsListening) server.Stop();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error stopping WebSocket server: {ex}");
            }
            finally
            {
                server = null;
                Debug.Log("WebSocket server stopped");
            }
        }
    }

    // —— Safely get the SessionManager for /register ——
    public WebSocketSessionManager GetRegisterSessions()
    {
        if (server == null) return null;

        Debug.Log($"[WSS] Active services: {string.Join(", ", server.WebSocketServices.Paths)}");
        // Some builds expose TryGetServiceHost; others only the indexer
        if (server.WebSocketServices.TryGetServiceHost("/register", out var host))
            return host.Sessions;
        Debug.LogWarning("[WSS] TryGetServiceHost failed, falling back to indexer");
        var idx = server.WebSocketServices["/register"];
        return idx != null ? idx.Sessions : null;
    }

    // --- Compatibility shim for older call-sites ---
    public WebSocketSessionManager RegisterSessions => GetRegisterSessions();

    // --- Handy wrappers ---
    public bool IsListening => server?.IsListening ?? false;

    public void Broadcast(string message)
        => GetRegisterSessions()?.Broadcast(message);

    public bool TrySendTo(string sessionId, string message)
    {
        var sessions = GetRegisterSessions();
        if (sessions == null) return false;
        sessions.SendTo(message, sessionId);
        return true;
    }
}
