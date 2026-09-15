using System.Collections;
using AbsoluteZero.Core.Audio;
using AbsoluteZero.Core.Match;
using AbsoluteZero.UI.Game.Bridge;
using AbsoluteZero.UI.Game.Build;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace AbsoluteZero.UI.Game
{
    [DefaultExecutionOrder(-100)]
    public class GameUIRoot : MonoBehaviour
    {
        public static GameUIRoot Instance { get; private set; }

        GameDataBridge _bridge;
        LocalPlayerCommandAdapter _commands;
        GameUIManager _uiManager;

        public IGameDataBridge Bridge => _bridge;
        public ILocalPlayerCommands Commands => _commands;
        public GameUIManager UIManager => _uiManager;

        void Awake()
        {
            if (Instance != null)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            EnsureEventSystem();
            EnsureAudioManager();
        }

        IEnumerator Start()
        {
            float deadline = Time.realtimeSinceStartup + MatchCompositionRoot.InitializationTimeout;
            while (MatchCompositionRoot.Instance == null)
            {
                if (Time.realtimeSinceStartup >= deadline) { ShowInitializationFailure(); yield break; }
                yield return null;
            }

            var mcr = MatchCompositionRoot.Instance;
            while (mcr != null && mcr.ActiveConfig == null)
            {
                if (mcr.InitializationFailure != null || Time.realtimeSinceStartup >= deadline)
                { ShowInitializationFailure(); yield break; }
                yield return null;
            }
            if (mcr == null) yield break;
            var registry = mcr.Registry;

            _bridge = gameObject.AddComponent<GameDataBridge>();
            _bridge.Initialize(registry);

            _commands = new LocalPlayerCommandAdapter(registry);

            int seatCount = mcr.ActiveConfig.RequiredPlayerCount;
            var refs = GameHudBuilder.Build(seatCount);

            _uiManager = gameObject.AddComponent<GameUIManager>();
            _uiManager.Initialize(_bridge, _commands, refs);

            Debug.Log("[GameUIRoot] Bridge + Commands + UIManager initialized");
        }

        GameObject _failureScreen;
        void Update()
        {
            if (MatchCompositionRoot.Instance?.InitializationFailure != null)
                ShowInitializationFailure();
        }

        void ShowInitializationFailure()
        {
            if (_failureScreen != null) return;
            _failureScreen = new GameObject("MatchInitializationFailure", typeof(RectTransform),
                typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
            _failureScreen.transform.SetParent(transform, false);
            var canvas = _failureScreen.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            panel.transform.SetParent(_failureScreen.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            panel.GetComponent<UnityEngine.UI.Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.96f);
            var label = new GameObject("Message", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            label.transform.SetParent(panel.transform, false);
            label.GetComponent<RectTransform>().sizeDelta = new Vector2(600, 140);
            var text = label.GetComponent<TMPro.TextMeshProUGUI>();
            text.text = "Unable to start the match.\nPlease return to the lobby and try again.";
            text.fontSize = 24; text.alignment = TMPro.TextAlignmentOptions.Center;
            var button = new GameObject("ReturnToLobby", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            button.transform.SetParent(panel.transform, false);
            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.sizeDelta = new Vector2(240, 55); buttonRect.anchoredPosition = new Vector2(0, -110);
            button.GetComponent<UnityEngine.UI.Image>().color = new Color(0.15f, 0.3f, 0.5f);
            var title = new GameObject("Label", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            title.transform.SetParent(button.transform, false);
            title.GetComponent<RectTransform>().sizeDelta = new Vector2(240, 55);
            var titleText = title.GetComponent<TMPro.TextMeshProUGUI>();
            titleText.text = "Return to Lobby"; titleText.fontSize = 22; titleText.alignment = TMPro.TextAlignmentOptions.Center;
            button.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(ReturnAfterInitializationFailure);
        }

        async void ReturnAfterInitializationFailure()
        {
            try
            {
                if (Core.Session.NetworkSessionCoordinator.Instance != null)
                    await Core.Session.NetworkSessionCoordinator.Instance.LeaveAsync();
                else Unity.Netcode.NetworkManager.Singleton?.Shutdown();
                if (this != null && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "LobbyScene")
                    UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
            }
            catch (System.Exception error) { Debug.LogException(error); }
        }

        void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
        }

        void EnsureAudioManager()
        {
            if (GameAudioManager.Instance == null)
            {
                var go = new GameObject("GameAudioManager");
                go.AddComponent<GameAudioManager>();
                DontDestroyOnLoad(go);
            }
            GameAudioManager.Instance?.PlayBGM();
        }

        void OnDestroy()
        {
            _commands?.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}
