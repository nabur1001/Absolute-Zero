using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace AbsoluteZero.Core.Solo
{
    public sealed class BotBootstrap : MonoBehaviour
    {
        enum BotState : byte { Idle, Connecting, Connected, WaitingRetry }

        ushort _port;
        string _token;
        BotState _state = BotState.Idle;
        float _retryTimer;
        int _retryCount;

        const float RETRY_INTERVAL = 2f;
        const int MAX_RETRIES = 5;

        void Awake()
        {
            if (!ParseCommandLine()) { enabled = false; return; }
            Debug.Log($"[Bot] Bootstrap — port={_port}, token={_token[..8]}...");
        }

        void Start()
        {
            if (_state == BotState.Idle && _port > 0) TryConnect();
        }

        bool ParseCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            _port = 0; _token = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--port" && i + 1 < args.Length) ushort.TryParse(args[i + 1], out _port);
                else if (args[i] == "--token" && i + 1 < args.Length) _token = args[i + 1];
            }
            return _port > 0 && !string.IsNullOrEmpty(_token);
        }

        void TryConnect()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) { Debug.LogError("[Bot] NetworkManager not found"); Application.Quit(1); return; }

            var transport = nm.GetComponent<UnityTransport>();
            if (transport == null) { Debug.LogError("[Bot] UnityTransport not found"); Application.Quit(1); return; }

            if (nm.IsClient || nm.IsServer) nm.Shutdown();

            transport.SetConnectionData("127.0.0.1", _port);
            nm.NetworkConfig.ConnectionData = System.Text.Encoding.UTF8.GetBytes(_token);

            nm.OnClientConnectedCallback -= OnConnected;
            nm.OnClientDisconnectCallback -= OnDisconnected;
            nm.OnClientConnectedCallback += OnConnected;
            nm.OnClientDisconnectCallback += OnDisconnected;

            if (!nm.StartClient())
            {
                Debug.LogError("[Bot] StartClient failed");
                ScheduleRetry();
            }
            else
            {
                _state = BotState.Connecting;
                Debug.Log($"[Bot] Connecting to 127.0.0.1:{_port}");
            }
        }

        void OnConnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || clientId != nm.LocalClientId) return;
            _state = BotState.Connected;
            Debug.Log("[Bot] Connected to host");
        }

        void OnDisconnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || clientId != nm.LocalClientId) return;

            if (_state == BotState.Connected)
            {
                Debug.Log("[Bot] Disconnected from host — quitting");
                Application.Quit(0);
                return;
            }

            Debug.LogWarning("[Bot] Connection rejected or failed");
            ScheduleRetry();
        }

        void ScheduleRetry()
        {
            _retryCount++;
            if (_retryCount > MAX_RETRIES)
            {
                Debug.LogError($"[Bot] Max retries ({MAX_RETRIES}) exceeded — quitting");
                Application.Quit(1);
                return;
            }

            _state = BotState.WaitingRetry;
            _retryTimer = RETRY_INTERVAL;
            Debug.Log($"[Bot] Retry {_retryCount}/{MAX_RETRIES} in {RETRY_INTERVAL}s");
        }

        void Update()
        {
            if (_state != BotState.WaitingRetry) return;
            _retryTimer -= Time.unscaledDeltaTime;
            if (_retryTimer <= 0f) TryConnect();
        }

        void OnDestroy()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnClientConnectedCallback -= OnConnected;
                nm.OnClientDisconnectCallback -= OnDisconnected;
            }
        }
    }
}
