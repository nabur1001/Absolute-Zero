using System;
using System.Diagnostics;
using AbsoluteZero.Core.Network;
using AbsoluteZero.Core.Session;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace AbsoluteZero.Core.Solo
{
    public sealed class SoloMatchStarter : MonoBehaviour
    {
        public static SoloMatchStarter Instance { get; private set; }

        Process _botProcess;
        string _sessionToken;
        bool _hostStarted;

        const ushort SOLO_PORT = 7777;
        const float BOT_CONNECT_TIMEOUT = 10f;
        float _botWaitTimer;
        bool _waitingForBot;
        bool _sceneLoaded;

        public event Action OnBotConnected;
        public event Action<string> OnSoloFailed;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            CleanupAll();
        }

        public void StartSoloMatch()
        {
            if (_hostStarted) return;
            _sessionToken = Guid.NewGuid().ToString("N");

            var nm = NetworkManager.Singleton;
            if (nm == null) { OnSoloFailed?.Invoke("NetworkManager not found"); return; }

            var transport = nm.GetComponent<UnityTransport>();
            if (transport == null) { OnSoloFailed?.Invoke("UnityTransport not found"); return; }

            transport.SetConnectionData("127.0.0.1", SOLO_PORT);

            var coordinator = NetworkSessionCoordinator.Instance;
            if (coordinator != null) coordinator.SetMatchParameters(GameMode.Solo, 2);

            nm.ConnectionApprovalCallback += ApproveBot;
            nm.NetworkConfig.ConnectionApproval = true;

            if (!nm.StartHost())
            {
                nm.ConnectionApprovalCallback -= ApproveBot;
                OnSoloFailed?.Invoke("StartHost failed");
                return;
            }

            _hostStarted = true;
            Debug.Log($"[Solo] Host started on port {SOLO_PORT}, token={_sessionToken[..8]}...");

            _sceneLoaded = false;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            nm.SceneManager.LoadScene("GameScene", LoadSceneMode.Single);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "GameScene") return;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            _sceneLoaded = true;
            LaunchBotProcess(SOLO_PORT);
        }

        void LaunchBotProcess(ushort port)
        {
            string botPath = GetBotExecutablePath();
            if (string.IsNullOrEmpty(botPath))
            {
                Debug.LogWarning("[Solo] Bot executable not found — waiting for manual bot connection");
                _waitingForBot = true; _botWaitTimer = 0f; return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = botPath,
                Arguments = $"-batchmode -nographics --port {port} --token {_sessionToken}",
                UseShellExecute = false, CreateNoWindow = true
            };

            try
            {
                _botProcess = Process.Start(startInfo);
                Debug.Log($"[Solo] Bot process launched: PID={_botProcess?.Id}");
                _waitingForBot = true; _botWaitTimer = 0f;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Solo] Failed to launch bot: {e.Message}");
                OnSoloFailed?.Invoke($"Failed to launch bot: {e.Message}");
            }
        }

        string GetBotExecutablePath()
        {
#if UNITY_EDITOR
            string basePath = System.IO.Path.Combine(Application.dataPath, "..", "Builds", "Bot");
#else
            string basePath = System.IO.Path.Combine(Application.dataPath, "..", "Bot");
#endif
            string exePath = System.IO.Path.Combine(basePath, "AbsoluteZeroBot.exe");
            return System.IO.File.Exists(exePath) ? exePath : null;
        }

        void Update()
        {
            if (!_waitingForBot) return;
            _botWaitTimer += Time.unscaledDeltaTime;

            if (_botProcess != null && _botProcess.HasExited)
            {
                int exitCode = _botProcess.ExitCode;
                _botProcess.Dispose(); _botProcess = null;
                _waitingForBot = false;
                OnSoloFailed?.Invoke($"Bot process exited prematurely (code {exitCode})");
                return;
            }

            var nm = NetworkManager.Singleton;
            if (nm != null && nm.ConnectedClientsIds.Count >= 2)
            {
                _waitingForBot = false;
                Debug.Log("[Solo] Bot connected");
                OnBotConnected?.Invoke();
                return;
            }

            if (_botWaitTimer >= BOT_CONNECT_TIMEOUT)
            {
                _waitingForBot = false;
                Debug.LogError("[Solo] Bot connection timed out");
                OnSoloFailed?.Invoke("Bot connection timed out");
            }
        }

        void ApproveBot(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            var nm = NetworkManager.Singleton;
            int connected = nm != null ? nm.ConnectedClientsIds.Count : 0;

            if (connected >= 2)
            {
                response.Approved = false; response.Reason = "Solo match full"; return;
            }

            string payload = System.Text.Encoding.UTF8.GetString(request.Payload);
            if (payload != _sessionToken)
            {
                response.Approved = false; response.Reason = "Invalid session token";
                Debug.LogWarning("[Solo] Rejected connection — bad token"); return;
            }

            response.Approved = true; response.CreatePlayerObject = false;
            Debug.Log("[Solo] Bot connection approved");
        }

        public void StopSoloMatch()
        {
            CleanupAll();
        }

        void CleanupAll()
        {
            KillBotProcess();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.ConnectionApprovalCallback -= ApproveBot;
                if (nm.IsListening) nm.Shutdown();
            }

            _hostStarted = false; _waitingForBot = false; _sceneLoaded = false;
        }

        void KillBotProcess()
        {
            if (_botProcess == null) return;
            try
            {
                if (!_botProcess.HasExited)
                {
                    _botProcess.Kill();
                    _botProcess.WaitForExit(3000);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Solo] Error killing bot: {e.Message}"); }
            finally { _botProcess.Dispose(); _botProcess = null; }
        }
    }
}
