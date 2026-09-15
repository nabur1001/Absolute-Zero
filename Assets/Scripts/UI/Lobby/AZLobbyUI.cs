using AbsoluteZero.Core.Network;
using AbsoluteZero.Core.Session;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace AbsoluteZero.UI.LobbyUI
{
    public class AZLobbyUI : MonoBehaviour
    {
        [SerializeField] GameObject _mainPanel;
        [SerializeField] GameObject _modeSelectPanel;
        [SerializeField] GameObject _roomPanel;
        [SerializeField] GameObject _settingsPanel;
        [SerializeField] GameObject _closetPanel;

        LobbyPresenter _presenter;

        LobbyMainView _mainView;
        LobbyModeSelectView _modeSelectView;
        LobbyRoomView _roomView;
        LobbySettingsView _settingsView;
        ClosetView _closetView;

        void Start()
        {
            var canvasRoot = FindCanvasRoot();
            if (canvasRoot == null)
            {
                Debug.LogError("[AZLobbyUI] MainUI Canvas not found");
                return;
            }

            EnsureCanvasSetup(canvasRoot);

            if (_mainPanel == null) _mainPanel = canvasRoot.Find("MainPanel")?.gameObject;
            if (_modeSelectPanel == null) _modeSelectPanel = canvasRoot.Find("ModeSelectPanel")?.gameObject;
            if (_roomPanel == null) _roomPanel = canvasRoot.Find("RoomPanel")?.gameObject;
            if (_settingsPanel == null) _settingsPanel = canvasRoot.Find("SettingsPanel")?.gameObject;
            if (_closetPanel == null) _closetPanel = canvasRoot.Find("ClosetPanel")?.gameObject;

            if (_mainPanel == null)
            {
                Debug.LogError("[AZLobbyUI] MainPanel not found — run menu: AbsoluteZero > Setup Lobby UI");
                return;
            }

            _mainView = new LobbyMainView(_mainPanel);
            _modeSelectView = new LobbyModeSelectView(_modeSelectPanel);
            _roomView = new LobbyRoomView(_roomPanel);
            _settingsView = new LobbySettingsView(_settingsPanel);
            _closetView = new ClosetView(_closetPanel);

            _presenter = new LobbyPresenter(_mainView, _modeSelectView, _roomView, _settingsView, _closetView);

            StartCoroutine(WaitForManagers());
        }

        System.Collections.IEnumerator WaitForManagers()
        {
            _mainView.SetStatus("초기화 중...");

            while (LobbyManager.Instance == null || NetworkSessionCoordinator.Instance == null)
                yield return null;

            var lobbyManager = LobbyManager.Instance;
            var coordinator = NetworkSessionCoordinator.Instance;

            _presenter.Initialize(lobbyManager, coordinator);

            while (coordinator.State != SessionState.Ready && coordinator.State != SessionState.Failed)
                yield return null;

            if (coordinator.State == SessionState.Failed)
            {
                _mainView.SetStatus($"초기화 실패: {coordinator.LastError}");
                yield break;
            }

            _mainView.SetStatus("준비 완료");
        }

        void OnDestroy()
        {
            _presenter?.Dispose();
        }

        Transform FindCanvasRoot()
        {
            var mainUI = GameObject.Find("MainUI");
            return mainUI != null ? mainUI.transform : null;
        }

        void EnsureCanvasSetup(Transform canvasRoot)
        {
            var go = canvasRoot.gameObject;
            var scaler = go.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
            }
            if (go.GetComponent<GraphicRaycaster>() == null)
                go.AddComponent<GraphicRaycaster>();

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var esGO = new GameObject("EventSystem");
                esGO.AddComponent<EventSystem>();
                esGO.AddComponent<InputSystemUIInputModule>();
            }
        }
    }
}
