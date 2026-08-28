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
            while (MatchCompositionRoot.Instance == null)
                yield return null;

            var registry = MatchCompositionRoot.Instance.Registry;

            _bridge = gameObject.AddComponent<GameDataBridge>();
            _bridge.Initialize(registry);

            _commands = new LocalPlayerCommandAdapter(registry);

            var refs = GameHudBuilder.Build();

            _uiManager = gameObject.AddComponent<GameUIManager>();
            _uiManager.Initialize(_bridge, _commands, refs);

            Debug.Log("[GameUIRoot] Bridge + Commands + UIManager initialized");
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
