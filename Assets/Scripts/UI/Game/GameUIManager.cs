using AbsoluteZero.Core.Audio;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Inventory;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Item.Data;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Network;
using AbsoluteZero.Core.Player;
using AbsoluteZero.UI.Game.Bridge;
using AbsoluteZero.UI.Game.Build;
using AbsoluteZero.UI.Game.Presenters;
using AbsoluteZero.UI.MiniGame;
using UnityEngine;
using UnityEngine.InputSystem;

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
        GhostSkillPresenter _ghostSkillPresenter;

        bool _initialized;

        // Multi target selection state
        bool _waitingForTarget;
        byte _pendingTargetSlot;

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

            _ghostSkillPresenter = gameObject.AddComponent<GhostSkillPresenter>();
            _ghostSkillPresenter.Initialize(bridge, commands, refs);

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

            if (_waitingForTarget)
            {
                HandleTargetSelection();
                return;
            }

            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                _matchHudPresenter?.TryReadyByKey();
        }

        void HandlePhaseChanged(TurnPhase oldPhase, TurnPhase newPhase)
        {
            if (newPhase == TurnPhase.PrepPhase)
                _tempPresenter.SnapTempDisplay();

            if (_waitingForTarget)
                CancelTargetSelection();
        }

        void OnItemClicked(int slotIndex)
        {
            if (MiniGameHub.IsRunning) return;
            if (_inventoryPresenter == null) return;
            if (_waitingForTarget) { CancelTargetSelection(); return; }

            var itemData = _inventoryPresenter.GetLocalItemData(slotIndex);
            if (itemData == null) return;

            bool isMulti = _bridge.CurrentMatch.Mode == GameMode.Multi;
            bool needsTarget = itemData.GetTargetMode() == TargetMode.SingleTarget;

            if (isMulti && needsTarget)
            {
                _pendingTargetSlot = (byte)slotIndex;
                _waitingForTarget = true;
                _matchHudPresenter.SetStatusText($"{itemData.ItemName} — 대상을 클릭하세요");
                return;
            }

            if (_commands.TrySelectItem((byte)slotIndex))
            {
                GameAudioManager.Instance?.PlayButtonClick();
                _inventoryPresenter.NotifyItemConfirmed(slotIndex);
                _matchHudPresenter.SetStatusText($"Selected: {itemData.ItemName}");
            }
        }

        void HandleTargetSelection()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelTargetSelection();
                return;
            }

            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
                return;

            if (UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                return;

            byte target = FindClickedAliveSeat();
            if (target == byte.MaxValue) return;

            byte slot = _pendingTargetSlot;
            _waitingForTarget = false;

            if (_commands.TrySelectItemWithTarget(slot, target))
            {
                GameAudioManager.Instance?.PlayButtonClick();
                _inventoryPresenter?.NotifyItemConfirmed(slot);
                var itemData = _inventoryPresenter?.GetLocalItemData(slot);
                _matchHudPresenter.SetStatusText($"{itemData?.ItemName} → P{target + 1}");
            }
            else
            {
                _matchHudPresenter.SetStatusText("선택 실패");
            }
        }

        void CancelTargetSelection()
        {
            _waitingForTarget = false;
            _matchHudPresenter?.SetStatusText("");
        }

        byte FindClickedAliveSeat()
        {
            var cam = Camera.main;
            if (cam == null) return byte.MaxValue;

            var mousePos = Mouse.current.position.ReadValue();
            var ray = cam.ScreenPointToRay(mousePos);
            var hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Collide);

            var match = _bridge.CurrentMatch;
            byte localSeat = _bridge.LocalSeatIndex;

            float closestDist = float.MaxValue;
            byte closestSeat = byte.MaxValue;

            foreach (var hit in hits)
            {
                var marker = hit.collider.GetComponentInParent<PlayerSeatMarker>();
                if (marker == null || marker.Player == null) continue;

                byte seat = marker.SeatIndex;
                if (seat == localSeat) continue;
                if (match.LifeStates == null || seat >= match.LifeStates.Length) continue;
                if (match.LifeStates[seat] != LifeState.Alive) continue;

                if (hit.distance < closestDist)
                {
                    closestDist = hit.distance;
                    closestSeat = seat;
                }
            }
            return closestSeat;
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
