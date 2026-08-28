using AbsoluteZero.Core.Audio;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Inventory;
using AbsoluteZero.UI.Game.Bridge;
using AbsoluteZero.UI.Game.Build;
using AbsoluteZero.UI.Game.Presenters;
using AbsoluteZero.UI.MiniGame;
using UnityEngine;

namespace AbsoluteZero.UI.Game
{
    public class GameUIManager : MonoBehaviour
    {
        IGameDataBridge _bridge;
        ILocalPlayerCommands _commands;
        GameHudRefs _refs;

        TemperaturePresenter _tempPresenter;
        MatchHudPresenter _matchHudPresenter;
        RoundResultPresenter _roundResultPresenter;
        OpponentBarPresenter _oppBarPresenter;
        InventoryPresenter _inventoryPresenter;

        bool _initialized;

        public MatchHudPresenter MatchHud => _matchHudPresenter;

        public void Initialize(IGameDataBridge bridge, ILocalPlayerCommands commands, GameHudRefs refs)
        {
            _bridge = bridge;
            _commands = commands;
            _refs = refs;

            _tempPresenter = new TemperaturePresenter(bridge, refs);

            _matchHudPresenter = gameObject.AddComponent<MatchHudPresenter>();
            _matchHudPresenter.Initialize(bridge, commands, refs);

            _roundResultPresenter = new RoundResultPresenter(bridge, commands, refs, this);

            _oppBarPresenter = gameObject.AddComponent<OpponentBarPresenter>();
            _oppBarPresenter.Initialize(bridge, refs);

            var fanSpawner = gameObject.GetComponent<FanSpawner>();
            if (fanSpawner == null)
                fanSpawner = gameObject.AddComponent<FanSpawner>();
            fanSpawner.SpawnStayItemFans();

            SpawnInventoryPresenter();
            SpawnMiniGameHub();

            _bridge.OnPhaseChanged += HandlePhaseChanged;

            _initialized = true;
            Debug.Log("[GameUIManager] Initialized — all presenters created");
        }

        void SpawnInventoryPresenter()
        {
            var presenterGO = new GameObject("InventoryPresenter");
            var spawnRoot = GameObject.Find("MyItemSpawnRoot");
            if (spawnRoot != null)
                presenterGO.transform.position = spawnRoot.transform.position;
            _inventoryPresenter = presenterGO.AddComponent<InventoryPresenter>();
            _inventoryPresenter.OnWorldItemClicked += OnItemClicked;
        }

        void SpawnMiniGameHub()
        {
            var hubGO = new GameObject("MiniGameHub");
            hubGO.AddComponent<MiniGameHub>();
            MiniGameHub.OnFinishedLocal += OnMiniGameFinished;
        }

        void Update()
        {
            if (!_initialized) return;
            _tempPresenter.Tick(Time.deltaTime);
        }

        void HandlePhaseChanged(TurnPhase oldPhase, TurnPhase newPhase)
        {
            if (newPhase == TurnPhase.PrepPhase)
                _tempPresenter.SnapTempDisplay();
        }

        void OnItemClicked(int slotIndex)
        {
            if (MiniGameHub.IsRunning) return;
            if (_inventoryPresenter == null) return;

            var itemData = _inventoryPresenter.GetLocalItemData(slotIndex);
            if (itemData == null) return;

            if (_commands.TrySelectItem((byte)slotIndex))
            {
                GameAudioManager.Instance?.PlayButtonClick();
                _inventoryPresenter.NotifyItemConfirmed(slotIndex);
                _matchHudPresenter.SetStatusText($"Selected: {itemData.ItemName}");
            }
        }

        void OnMiniGameFinished(byte slotIndex, bool success)
        {
            _matchHudPresenter?.SetStatusText(success ? "Mini-game clear!" : "Mini-game failed...");
        }

        void OnDestroy()
        {
            if (_bridge != null)
                _bridge.OnPhaseChanged -= HandlePhaseChanged;
            _tempPresenter?.Dispose();
            _roundResultPresenter?.Dispose();
            MiniGameHub.OnFinishedLocal -= OnMiniGameFinished;
            if (_inventoryPresenter != null)
                _inventoryPresenter.OnWorldItemClicked -= OnItemClicked;

            if (GameAudioManager.Instance != null)
                GameAudioManager.Instance.StopBGM();
        }
    }
}
