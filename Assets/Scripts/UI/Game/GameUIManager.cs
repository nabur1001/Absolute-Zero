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
        byte _snappedTargetSeat = byte.MaxValue;
        Transform _snappedTargetTransform;
        TargetingArrowPresenter _targetingArrowPresenter;
        const float TargetSnapEnterPixels = 105f;
        const float TargetSnapReleasePixels = 155f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Transform _debugHoverTarget;
#endif

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
            gameObject.AddComponent<GhostSkillVFXPresenter>();

            SpawnInventoryPresenter();
            _targetingArrowPresenter = gameObject.AddComponent<TargetingArrowPresenter>();
            _targetingArrowPresenter.Initialize();
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
            presenterGO.AddComponent<MultiPerspectiveLayout>();
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

            if (_inventoryPresenter.IsConfirmedSlot(slotIndex))
            {
                if (_commands.TryCancelSelection())
                {
                    GameAudioManager.Instance?.PlayButtonClick();
                    _matchHudPresenter.SetStatusText("선택 취소");
                }
                return;
            }

            if (_waitingForTarget)
            {
                bool sameItem = _inventoryPresenter.ResolvePendingSlotIndex() == slotIndex;
                CancelTargetSelection();
                if (sameItem) return;
            }

            var itemData = _inventoryPresenter.GetLocalItemData(slotIndex);
            if (itemData == null) return;

            bool isMulti = _bridge.CurrentMatch.Mode == GameMode.Multi;
            bool needsTarget = itemData.GetTargetMode() == TargetMode.SingleTarget;

            if (isMulti && needsTarget)
            {
                _pendingTargetSlot = (byte)slotIndex;
                _inventoryPresenter.RequestItemConfirm(slotIndex);
                _waitingForTarget = true;
                _snappedTargetSeat = byte.MaxValue;
                _snappedTargetTransform = null;
                _matchHudPresenter.SetStatusText($"{itemData.ItemName} — 대상을 조준한 뒤 클릭하세요");
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
            UpdateTargetingArrow();

            if ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame))
            {
                CancelTargetSelection();
                return;
            }

            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
                return;

            if (UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                return;

            byte target = _snappedTargetSeat;
            if (target == byte.MaxValue) return;

            int resolvedSlot = _inventoryPresenter.ResolvePendingSlotIndex();
            if (resolvedSlot < 0)
            {
                CancelTargetSelection();
                return;
            }
            byte slot = (byte)resolvedSlot;
            _waitingForTarget = false;
            _targetingArrowPresenter?.Hide();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugHoverTarget = null;
#endif

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
            _snappedTargetSeat = byte.MaxValue;
            _snappedTargetTransform = null;
            _inventoryPresenter?.CancelPendingItem();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugHoverTarget = null;
#endif
            _targetingArrowPresenter?.Hide();
            _matchHudPresenter?.SetStatusText("");
        }

        void UpdateTargetingArrow()
        {
            var cam = Camera.main;
            if (cam == null || Mouse.current == null || _inventoryPresenter == null)
            {
                CancelTargetSelection();
                return;
            }

            int resolvedSlot = _inventoryPresenter.ResolvePendingSlotIndex();
            var itemView = _inventoryPresenter.GetLocalView(resolvedSlot);
            if (resolvedSlot < 0 || itemView == null)
            {
                CancelTargetSelection();
                return;
            }

            Vector2 pointer = Mouse.current.position.ReadValue();
            Vector2 targetScreen = pointer;
            bool snapped = TryResolveSnap(pointer, out byte targetSeat, out Transform targetTransform, out targetScreen);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_debugHoverTarget != null)
            {
                targetTransform = _debugHoverTarget;
                var marker = targetTransform.GetComponentInParent<PlayerSeatMarker>();
                targetSeat = marker != null ? marker.SeatIndex : byte.MaxValue;
                targetScreen = TargetSnapScreenPosition(cam, targetTransform);
                snapped = targetSeat != byte.MaxValue;
            }
#endif

            _snappedTargetSeat = snapped ? targetSeat : byte.MaxValue;
            _snappedTargetTransform = snapped ? targetTransform : null;

            Vector3 tailWorld = itemView.transform.position + Vector3.up * 0.2f;
            Vector2 tailScreen = cam.WorldToScreenPoint(tailWorld);
            int visualSlot = snapped
                ? AZPlayerVisual.GetRemoteVisualSlot(targetSeat, _bridge.LocalSeatIndex)
                : -1;
            bool straight = snapped && visualSlot == 1;
            float bendDirection = snapped
                ? (visualSlot == 0 ? -1f : visualSlot == 2 ? 1f : 0f)
                : Mathf.Sign(targetScreen.x - tailScreen.x);
            Vector2 arrowHeadScreen = snapped
                ? TargetArrowScreenPosition(cam, targetTransform)
                : targetScreen;
            _targetingArrowPresenter?.Show(tailScreen, arrowHeadScreen, straight, bendDirection);
        }

        bool TryResolveSnap(Vector2 pointer, out byte targetSeat, out Transform targetTransform,
            out Vector2 targetScreen)
        {
            targetSeat = byte.MaxValue;
            targetTransform = null;
            targetScreen = pointer;
            var cam = Camera.main;
            if (cam == null) return false;

            if (_snappedTargetSeat != byte.MaxValue
                && TryGetEligibleTarget(_snappedTargetSeat, out var current))
            {
                Vector2 currentScreen = TargetSnapScreenPosition(cam, current);
                if (Vector2.Distance(pointer, currentScreen) <= TargetSnapReleasePixels)
                {
                    targetSeat = _snappedTargetSeat;
                    targetTransform = current;
                    targetScreen = currentScreen;
                    return true;
                }
            }

            float closest = TargetSnapEnterPixels;
            foreach (var marker in FindObjectsByType<PlayerSeatMarker>(FindObjectsSortMode.None))
            {
                if (marker == null || marker.Player == null) continue;
                byte seat = marker.SeatIndex;
                if (!IsEligibleTarget(seat)) continue;
                Vector2 screen = TargetSnapScreenPosition(cam, marker.transform);
                float distance = Vector2.Distance(pointer, screen);
                if (distance > closest) continue;
                closest = distance;
                targetSeat = seat;
                targetTransform = marker.transform;
                targetScreen = screen;
            }
            return targetSeat != byte.MaxValue;
        }

        bool TryGetEligibleTarget(byte seat, out Transform target)
        {
            target = null;
            if (!IsEligibleTarget(seat)) return false;
            foreach (var marker in FindObjectsByType<PlayerSeatMarker>(FindObjectsSortMode.None))
            {
                if (marker != null && marker.Player != null && marker.SeatIndex == seat)
                {
                    target = marker.transform;
                    return true;
                }
            }
            return false;
        }

        bool IsEligibleTarget(byte seat)
        {
            var match = _bridge.CurrentMatch;
            return seat != _bridge.LocalSeatIndex
                && match.LifeStates != null
                && seat < match.LifeStates.Length
                && match.LifeStates[seat] == LifeState.Alive;
        }

        static Vector2 TargetSnapScreenPosition(Camera cam, Transform target)
        {
            var collider = target.GetComponentInChildren<Collider>();
            Vector3 world = collider != null
                ? collider.bounds.center + Vector3.up * collider.bounds.extents.y * 0.35f
                : target.position + Vector3.up;
            return cam.WorldToScreenPoint(world);
        }

        static Vector2 TargetArrowScreenPosition(Camera cam, Transform target)
        {
            Bounds visualBounds = default;
            bool hasVisualBounds = false;
            foreach (var renderer in target.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                string objectName = renderer.gameObject.name.ToLowerInvariant();
                if (objectName.Contains("item") || objectName.Contains("fan")
                    || objectName.Contains("effect") || objectName.Contains("outline")
                    || objectName.Contains("freeze") || objectName.Contains("ghost"))
                    continue;

                if (!hasVisualBounds)
                {
                    visualBounds = renderer.bounds;
                    hasVisualBounds = true;
                }
                else
                {
                    visualBounds.Encapsulate(renderer.bounds);
                }
            }

            Vector3 world = hasVisualBounds
                ? visualBounds.center - Vector3.up * visualBounds.extents.y
                : target.position + Vector3.down * 0.08f;
            Vector2 screen = cam.WorldToScreenPoint(world);
            screen.y -= Mathf.Clamp(Screen.height * 0.025f, 10f, 32f);
            return screen;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool DebugBeginTargetHover(byte slotIndex, byte targetSeat)
        {
            if (!_initialized || _inventoryPresenter == null || Mouse.current == null) return false;
            var item = _inventoryPresenter.GetLocalItemData(slotIndex);
            if (item == null || item.GetTargetMode() != TargetMode.SingleTarget) return false;

            PlayerSeatMarker targetMarker = null;
            foreach (var marker in FindObjectsByType<PlayerSeatMarker>(FindObjectsSortMode.None))
            {
                if (marker.Player != null && marker.SeatIndex == targetSeat)
                {
                    targetMarker = marker;
                    break;
                }
            }
            if (targetMarker == null || Camera.main == null) return false;

            _pendingTargetSlot = slotIndex;
            _inventoryPresenter.RequestItemConfirm(slotIndex);
            _waitingForTarget = true;
            _snappedTargetSeat = targetSeat;
            _snappedTargetTransform = targetMarker.transform;
            _debugHoverTarget = targetMarker.transform;
            _matchHudPresenter?.SetStatusText($"{item.ItemName} — hover target P{targetSeat + 1}");
            Vector3 screen = Camera.main.WorldToScreenPoint(targetMarker.transform.position + Vector3.up);
            Mouse.current.WarpCursorPosition(new Vector2(screen.x, screen.y));
            return true;
        }

        public bool DebugTargetHoverVisible => _targetingArrowPresenter != null && _targetingArrowPresenter.IsVisible;
        public void DebugCancelTargetHover()
        {
            _debugHoverTarget = null;
            CancelTargetSelection();
        }
#endif

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
