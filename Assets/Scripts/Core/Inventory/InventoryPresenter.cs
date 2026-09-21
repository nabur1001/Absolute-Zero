using System;
using System.Collections.Generic;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Item.Data;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Network;
using AbsoluteZero.Core.Player;
using AbsoluteZero.Core.Turn;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AbsoluteZero.Core.Inventory
{
    public class InventoryPresenter : MonoBehaviour
    {
        public static InventoryPresenter Instance { get; private set; }

        // --- Player references ---
        PlayerState _localPlayer;
        PlayerState _opponentPlayer;
        PlayerInventory _localInventory;
        PlayerInventory _opponentInventory;
        bool _localBound;
        bool _opponentBound;

        // --- Local item views ---
        ItemWorldView[] _localViews;
        int _confirmedSlotIndex = -1;
        int _confirmedItemId = -1;
        int _pendingItemId = -1;
        bool _needsLocalRebuild;
        bool _fullRedistribute;

        // --- Rebuild lock (E1: deadlock prevention) ---
        bool _rebuildLocked;
        float _rebuildLockTime;
        const float REBUILD_LOCK_TIMEOUT = 15f;

        // --- Opponent item views ---
        GameObject[] _opponentItemObjects;
        bool _needsOpponentRebuild;

        // --- Selection arrow ---
        GameObject _selectionArrow;
        SpriteRenderer _arrowRenderer;
        Texture2D _arrowTexture;

        // --- Layout constants ---
        const float FALLBACK_SPACING = 0.9f;
        const float FALLBACK_Y = 0.5f;

        // --- Events ---
        public event Action OnLocalInventoryChanged;
        public event Action OnOpponentInventoryChanged;
        public event Action<int> OnLocalSlotValueChanged;
        public event Action OnSelectionChanged;
        public event Action OnBannedStateChanged;
        public event Action OnViewsRebuilt;
        public event Action<int> OnWorldItemClicked;
        public event Action<int, int> OnItemConfirmRequested;

        void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        void OnDestroy()
        {
            Unbind();
            if (_arrowTexture != null) Destroy(_arrowTexture);
            if (_selectionArrow != null) Destroy(_selectionArrow);
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            bool multi = IsMultiMode();
            if (!_localBound || (!multi && !_opponentBound))
                TryBindPlayers();

            // E2: auto-detect opponent disconnect
            if (!multi && _opponentBound && _opponentPlayer == null)
            {
                DestroyOpponentViews();
                UnbindOpponent();
            }

            if (_localBound)
                HandleClick();

            if (_selectionArrow != null && _selectionArrow.activeSelf)
            {
                if (TurnManager.Instance == null
                    || TurnManager.Instance.CurrentPhase.Value != TurnPhase.PrepPhase)
                {
                    _selectionArrow.SetActive(false);
                }
                else if (_confirmedSlotIndex >= 0
                    && _localViews != null
                    && _confirmedSlotIndex < _localViews.Length
                    && _localViews[_confirmedSlotIndex] != null)
                {
                    var basePos = _localViews[_confirmedSlotIndex].transform.position
                                  + new Vector3(0f, 0.6f, 0f);
                    float bounce = Mathf.Sin(Time.time * 4f) * 0.05f;
                    _selectionArrow.transform.position = basePos + new Vector3(0f, bounce, 0f);
                }
            }
        }

        void LateUpdate()
        {
            // E1: timeout fallback for rebuild lock
            if (_rebuildLocked && Time.time - _rebuildLockTime > REBUILD_LOCK_TIMEOUT)
            {
                Debug.LogWarning("[InventoryPresenter] Rebuild lock timeout — force unlock");
                UnlockRebuild();
                return;
            }

            if (_needsLocalRebuild && CanRebuild())
            {
                _needsLocalRebuild = false;
                RebuildLocalViews();
            }

            if (!IsMultiMode() && _needsOpponentRebuild && CanRebuild())
            {
                _needsOpponentRebuild = false;
                RebuildOpponentItems();
            }
        }

        // ─── Binding ────────────────────────────────────────────

        void TryBindPlayers()
        {
            if (NetworkManager.Singleton == null) return;

            if (!_localBound)
                TryBindLocal();

            if (!IsMultiMode() && !_opponentBound && _localBound)
                TryBindOpponent();
        }

        static bool IsMultiMode()
        {
            var root = MatchCompositionRoot.Instance;
            return root != null && root.ActiveConfig != null
                && root.ActiveConfig.Mode == GameMode.Multi;
        }

        void TryBindLocal()
        {
            var localObj = NetworkManager.Singleton.SpawnManager?.GetLocalPlayerObject();
            if (localObj == null) return;

            var ps = localObj.GetComponent<PlayerState>();
            if (ps == null) return;

            var inv = ps.GetInventory();
            if (inv == null || inv.SlotStates == null || inv.SlotStates.Count == 0) return;
            if (ItemManager.Instance == null) return;

            if (!inv.IsRegistryReady)
                ItemManager.Instance.InitializeClientRegistry(inv);
            if (!inv.IsRegistryReady) return;

            _localPlayer = ps;
            _localInventory = inv;

            if (HoverRaycaster.Instance == null)
                gameObject.AddComponent<HoverRaycaster>();

            _localInventory.SlotStates.OnListChanged += OnLocalSlotStatesChanged;
            _localPlayer.HasSelectedItem.OnValueChanged += OnHasSelectedItemChanged;
            _localPlayer.IsBasicBlocked.OnValueChanged += OnBasicBlockedChanged;

            _localBound = true;

            // E5: snapshot — items may already exist before subscription
            if (_localInventory.SlotStates.Count > 0)
                _needsLocalRebuild = true;

            Debug.Log($"[InventoryPresenter] Local bound — {inv.SlotStates.Count} slots");
        }

        void TryBindOpponent()
        {
            var players = FindObjectsByType<PlayerState>(FindObjectsSortMode.None);
            PlayerState opp = null;
            foreach (var p in players)
            {
                if (p != _localPlayer) { opp = p; break; }
            }
            if (opp == null) return;

            var inv = opp.GetInventory();
            if (inv == null || inv.SlotStates == null || inv.SlotStates.Count == 0) return;
            if (ItemManager.Instance == null) return;

            if (!inv.IsRegistryReady)
                ItemManager.Instance.InitializeClientRegistry(inv);

            _opponentPlayer = opp;
            _opponentInventory = inv;

            _opponentInventory.SlotStates.OnListChanged += OnOpponentSlotStatesChanged;
            _opponentBound = true;

            // E5: snapshot
            if (_opponentInventory.SlotStates.Count > 0)
                _needsOpponentRebuild = true;

            Debug.Log($"[InventoryPresenter] Opponent bound — {inv.SlotStates.Count} slots");
        }

        // ─── Safe Unbind (E2) ───────────────────────────────────

        void Unbind()
        {
            UnbindLocal();
            UnbindOpponent();
        }

        void UnbindLocal()
        {
            if (_localInventory != null && _localInventory.SlotStates != null)
                _localInventory.SlotStates.OnListChanged -= OnLocalSlotStatesChanged;

            if (_localPlayer != null)
            {
                _localPlayer.HasSelectedItem.OnValueChanged -= OnHasSelectedItemChanged;
                _localPlayer.IsBasicBlocked.OnValueChanged -= OnBasicBlockedChanged;
            }

            _localBound = false;
        }

        void UnbindOpponent()
        {
            if (_opponentInventory != null && _opponentInventory.SlotStates != null)
                _opponentInventory.SlotStates.OnListChanged -= OnOpponentSlotStatesChanged;

            _opponentBound = false;
        }

        // ─── Rebuild Lock (E1) ──────────────────────────────────

        public void LockRebuild()
        {
            _rebuildLocked = true;
            _rebuildLockTime = Time.time;
        }

        public void UnlockRebuild()
        {
            _rebuildLocked = false;
            if (_needsLocalRebuild)
            {
                _needsLocalRebuild = false;
                RebuildLocalViews();
            }
            if (!IsMultiMode() && _needsOpponentRebuild)
            {
                _needsOpponentRebuild = false;
                RebuildOpponentItems();
            }
        }

        // E3: combined guard
        bool CanRebuild()
        {
            if (_rebuildLocked) return false;
            if (IceboxController.Instance != null && IceboxController.Instance.IsAnimating) return false;
            return true;
        }

        // ─── OnListChanged Handlers ─────────────────────────────

        void OnLocalSlotStatesChanged(NetworkListEvent<ItemSlotNetData> evt)
        {
            if (_localInventory == null) { Unbind(); return; }

            Debug.Log($"[InventoryPresenter] LocalSlotChanged: type={evt.Type}, index={evt.Index}");

            switch (evt.Type)
            {
                case NetworkListEvent<ItemSlotNetData>.EventType.Add:
                case NetworkListEvent<ItemSlotNetData>.EventType.Remove:
                case NetworkListEvent<ItemSlotNetData>.EventType.RemoveAt:
                case NetworkListEvent<ItemSlotNetData>.EventType.Insert:
                    _needsLocalRebuild = true;
                    break;

                case NetworkListEvent<ItemSlotNetData>.EventType.Clear:
                case NetworkListEvent<ItemSlotNetData>.EventType.Full:
                    _needsLocalRebuild = true;
                    _fullRedistribute = true;
                    break;

                case NetworkListEvent<ItemSlotNetData>.EventType.Value:
                    HandleLocalSlotValueChange(evt.Index);
                    break;
            }
        }

        void HandleLocalSlotValueChange(int index)
        {
            if (_localViews == null || index < 0 || index >= _localViews.Length)
                return;

            var slot = _localInventory.SlotStates[index];

            if (slot.IsEmpty)
            {
                _needsLocalRebuild = true;
                return;
            }

            if (_localViews[index] == null)
            {
                _needsLocalRebuild = true;
                return;
            }

            var itemData = _localInventory.GetItemData(index);
            string currentName = _localViews[index].gameObject.name;
            string expectedName = itemData != null ? $"Item_{index}_{itemData.ItemName}" : "";
            if (currentName != expectedName)
            {
                _needsLocalRebuild = true;
                return;
            }

            string name = itemData != null ? itemData.ItemName : "Empty";
            string uses = slot.IsUnlimited ? "∞" : $"{slot.RemainingUses}";
            _localViews[index].UpdateDisplay(name, uses, slot.IsUsable);

            UpdateSelectionVisuals();
            OnLocalSlotValueChanged?.Invoke(index);
        }

        void OnOpponentSlotStatesChanged(NetworkListEvent<ItemSlotNetData> evt)
        {
            if (_opponentInventory == null) { UnbindOpponent(); return; }
            _needsOpponentRebuild = true;
        }

        void OnHasSelectedItemChanged(bool oldVal, bool newVal)
        {
            if (!newVal)
            {
                _confirmedSlotIndex = -1;
                _confirmedItemId = -1;
            }
            UpdateSelectionVisuals();
            OnSelectionChanged?.Invoke();
        }

        void OnBasicBlockedChanged(bool oldVal, bool newVal)
        {
            Debug.Log($"[InventoryPresenter] BasicBlocked: {oldVal} → {newVal}");
            UpdateBannedOverlays();
            OnBannedStateChanged?.Invoke();
        }

        // ─── Local View Management ──────────────────────────────

        void RebuildLocalViews()
        {
            bool animateAll = _fullRedistribute;
            _fullRedistribute = false;

            var previousSlots = new HashSet<int>();
            if (!animateAll && _localViews != null)
            {
                for (int i = 0; i < _localViews.Length; i++)
                    if (_localViews[i] != null) previousSlots.Add(i);
            }

            DestroyLocalViews();

            // E4: re-locate confirmed item by ItemId after rebuild
            _confirmedSlotIndex = -1;

            SpawnLocalViews();

            // E4: restore confirmed slot by ItemId
            if (_confirmedItemId >= 0 && _localPlayer != null && _localPlayer.HasSelectedItem.Value)
                ResolveConfirmedSlotByItemId();

            if (animateAll)
                TriggerIceboxAnimation();
            else
                TriggerIceboxAnimationPartial(previousSlots);

            UpdateSelectionVisuals();
            UpdateBannedOverlays();
            OnViewsRebuilt?.Invoke();
            OnLocalInventoryChanged?.Invoke();
        }

        void SpawnLocalViews()
        {
            int count = _localInventory.SlotStates.Count;
            _localViews = new ItemWorldView[count];

            int randomMarker = 0;
            int basicMarker = 0;

            for (int i = 0; i < count; i++)
            {
                var slot = _localInventory.SlotStates[i];
                if (slot.IsEmpty) continue;

                var itemData = _localInventory.GetItemData(i);
                if (itemData == null) continue;

                string markerName;
                if (itemData.Persistence == ItemPersistence.RandomConsumable)
                {
                    markerName = $"PlayerItem{randomMarker + 1}";
                    randomMarker++;
                }
                else
                {
                    markerName = $"PlayerItem{9 + basicMarker}";
                    basicMarker++;
                }

                var go = new GameObject();
                go.transform.SetParent(transform, false);

                var marker = GameObject.Find(markerName);
                if (marker != null)
                    go.transform.position = marker.transform.position;
                else
                {
                    float totalWidth = (count - 1) * FALLBACK_SPACING;
                    go.transform.localPosition = new Vector3(
                        -totalWidth / 2f + i * FALLBACK_SPACING, FALLBACK_Y, 0f);
                }

                var view = go.AddComponent<ItemWorldView>();
                view.Initialize(i, itemData.ItemName, Color.white);

                string uses = slot.IsUnlimited ? "∞" : $"{slot.RemainingUses}";
                view.UpdateDisplay(itemData.ItemName, uses, slot.IsUsable);

                _localViews[i] = view;
            }
        }

        void DestroyLocalViews()
        {
            if (_localViews == null) return;
            foreach (var v in _localViews)
                if (v != null) Destroy(v.gameObject);
            _localViews = null;
        }

        // ─── Opponent View Management ───────────────────────────

        void RebuildOpponentItems()
        {
            if (_opponentInventory == null) return;

            DestroyOpponentViews();

            int slotCount = _opponentInventory.SlotStates.Count;
            var activeItems = new List<GameObject>();

            int randomMarker = 0;
            int basicMarker = 0;

            for (int i = 0; i < slotCount; i++)
            {
                var slot = _opponentInventory.SlotStates[i];
                if (slot.IsEmpty) continue;

                var itemData = _opponentInventory.GetItemData(i);
                if (itemData == null) continue;

                string markerName;
                if (itemData.Persistence == ItemPersistence.RandomConsumable)
                {
                    markerName = $"EnemyItem{randomMarker + 1}";
                    randomMarker++;
                }
                else
                {
                    markerName = $"EnemyItem{9 + basicMarker}";
                    basicMarker++;
                }

                string itemName = itemData.ItemName;
                var go = new GameObject($"OppItem_{i}_{itemName}");

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = GameSprites.GetItemSprite(itemName);
                sr.sortingOrder = 5;

                var marker = GameObject.Find(markerName);
                if (marker != null)
                    go.transform.position = marker.transform.position;

                activeItems.Add(go);
            }

            _opponentItemObjects = activeItems.ToArray();
            OnOpponentInventoryChanged?.Invoke();
        }

        void DestroyOpponentViews()
        {
            if (_opponentItemObjects == null) return;
            foreach (var go in _opponentItemObjects)
                if (go != null) Destroy(go);
            _opponentItemObjects = null;
        }

        // ─── Selection & Banned ─────────────────────────────────

        void UpdateSelectionVisuals()
        {
            if (_localViews == null || _localPlayer == null || _localInventory == null) return;

            bool hasSelected = _localPlayer.HasSelectedItem.Value;

            for (int i = 0; i < _localViews.Length; i++)
            {
                if (_localViews[i] == null) continue;

                bool isThisSelected = hasSelected && _confirmedSlotIndex == i;

                if (_localViews[i].Hover != null)
                    _localViews[i].Hover.SetSelected(isThisSelected);

                if (!isThisSelected)
                {
                    var itemData = _localInventory.GetItemData(i);
                    bool isBanned = _localPlayer.IsBasicBlocked.Value
                                    && itemData != null
                                    && itemData.SlotType == ItemSlotType.Main;
                    bool usable = i < _localInventory.SlotStates.Count && _localInventory.SlotStates[i].IsUsable;
                    _localViews[i].SetInteractable(!isBanned && !hasSelected && usable);
                }
            }

            UpdateArrowVisibility();
        }

        void EnsureArrow()
        {
            if (_selectionArrow != null) return;
            _selectionArrow = new GameObject("SelectionArrow");

            const int size = 32;
            _arrowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            int halfW = size / 2;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float ny = (float)y / size;
                float hw = halfW * (1f - ny);
                bool inside = x >= halfW - hw && x <= halfW + hw;
                _arrowTexture.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
            _arrowTexture.Apply();
            _arrowTexture.filterMode = FilterMode.Bilinear;

            _arrowRenderer = _selectionArrow.AddComponent<SpriteRenderer>();
            _arrowRenderer.sprite = Sprite.Create(
                _arrowTexture,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0f),
                100f);
            _arrowRenderer.color = new Color(1f, 0.85f, 0.2f);
            _arrowRenderer.sortingOrder = 95;
            _selectionArrow.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
            _selectionArrow.SetActive(false);
        }

        void UpdateArrowVisibility()
        {
            EnsureArrow();
            bool visible = _localPlayer != null
                && _localPlayer.HasSelectedItem.Value
                && _confirmedSlotIndex >= 0
                && _localViews != null
                && _confirmedSlotIndex < _localViews.Length
                && _localViews[_confirmedSlotIndex] != null
                && TurnManager.Instance != null
                && TurnManager.Instance.CurrentPhase.Value == TurnPhase.PrepPhase;

            _selectionArrow.SetActive(visible);
        }

        void UpdateBannedOverlays()
        {
            if (_localViews == null || _localInventory == null || _localPlayer == null) return;

            bool blocked = _localPlayer.IsBasicBlocked.Value;

            for (int i = 0; i < _localViews.Length; i++)
            {
                if (_localViews[i] == null) continue;

                var itemData = _localInventory.GetItemData(i);
                if (itemData == null) continue;

                bool isBanned = blocked && itemData.SlotType == ItemSlotType.Main;
                _localViews[i].SetBanned(isBanned);
                if (isBanned)
                    _localViews[i].SetInteractable(false);
            }
        }

        // ─── Click Handling ─────────────────────────────────────

        void HandleClick()
        {
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;

            var hovered = HoverRaycaster.Instance?.CurrentHovered;
            if (hovered == null) return;

            var view = hovered.GetComponent<ItemWorldView>();
            if (view == null) return;

            if (!CanSelectItem(view.SlotIndex)) return;
            OnWorldItemClicked?.Invoke(view.SlotIndex);
        }

        bool CanSelectItem(int slotIndex)
        {
            if (_localPlayer == null) return false;
            if (_localPlayer.IsReady.Value) return false;

            if (IceboxController.Instance != null && IceboxController.Instance.IsAnimating) return false;

            var tm = TurnManager.Instance;
            if (tm == null || tm.CurrentPhase.Value != TurnPhase.PrepPhase) return false;

            if (slotIndex < 0 || slotIndex >= _localInventory.SlotStates.Count) return false;
            if (!_localInventory.SlotStates[slotIndex].IsUsable) return false;

            var itemData = _localInventory.GetItemData(slotIndex);
            if (itemData == null) return false;

            if (_localPlayer.IsBasicBlocked.Value && itemData.Persistence == ItemPersistence.Permanent)
                return false;

            if (!_localPlayer.HasSelectedItem.Value) return true;
            return slotIndex == _confirmedSlotIndex;
        }

        // ─── Confirm Flow (E4: ItemId-based) ────────────────────

        public void RequestItemConfirm(int slotIndex)
        {
            if (_localInventory == null) return;
            if (slotIndex < 0 || slotIndex >= _localInventory.SlotStates.Count) return;
            _pendingItemId = _localInventory.SlotStates[slotIndex].ItemId;
            OnItemConfirmRequested?.Invoke(slotIndex, _pendingItemId);
        }

        public int ResolvePendingSlotIndex()
        {
            if (_pendingItemId < 0 || _localInventory == null) return -1;
            for (int i = 0; i < _localInventory.SlotStates.Count; i++)
                if (_localInventory.SlotStates[i].ItemId == _pendingItemId)
                    return i;
            return -1;
        }

        public void NotifyItemConfirmed(int slotIndex)
        {
            _confirmedSlotIndex = slotIndex;
            if (_localInventory != null && slotIndex >= 0 && slotIndex < _localInventory.SlotStates.Count)
                _confirmedItemId = _localInventory.SlotStates[slotIndex].ItemId;
            _pendingItemId = -1;
            UpdateSelectionVisuals();
        }

        public void CancelPendingItem()
        {
            _pendingItemId = -1;
        }

        public bool IsConfirmedSlot(int slotIndex)
        {
            return _localPlayer != null && _localPlayer.HasSelectedItem.Value
                && slotIndex == _confirmedSlotIndex;
        }

        void ResolveConfirmedSlotByItemId()
        {
            for (int i = 0; i < _localInventory.SlotStates.Count; i++)
            {
                if (_localInventory.SlotStates[i].ItemId == _confirmedItemId)
                {
                    _confirmedSlotIndex = i;
                    return;
                }
            }
            _confirmedSlotIndex = -1;
            _confirmedItemId = -1;
        }

        // ─── Icebox Animation ───────────────────────────────────

        void TriggerIceboxAnimation()
        {
            var icebox = IceboxController.Instance;
            if (icebox == null || _localViews == null) return;

            int count = 0;
            for (int i = 0; i < _localViews.Length; i++)
                if (_localViews[i] != null) count++;

            if (count == 0) return;

            var transforms = new Transform[count];
            int idx = 0;
            for (int i = 0; i < _localViews.Length; i++)
                if (_localViews[i] != null)
                    transforms[idx++] = _localViews[i].transform;

            icebox.PlayDistribution(transforms);
        }

        void TriggerIceboxAnimationPartial(HashSet<int> previousSlots)
        {
            var icebox = IceboxController.Instance;
            if (icebox == null || _localViews == null) return;

            int count = 0;
            for (int i = 0; i < _localViews.Length; i++)
                if (_localViews[i] != null && !previousSlots.Contains(i)) count++;

            if (count == 0) return;

            var transforms = new Transform[count];
            int idx = 0;
            for (int i = 0; i < _localViews.Length; i++)
                if (_localViews[i] != null && !previousSlots.Contains(i))
                    transforms[idx++] = _localViews[i].transform;

            icebox.PlayDistribution(transforms);
        }

        // ─── Accessors ──────────────────────────────────────────

        public int LocalSlotCount => _localInventory?.SlotStates?.Count ?? 0;

        public ItemSlotNetData GetLocalSlot(int index)
        {
            if (_localInventory == null || index < 0 || index >= _localInventory.SlotStates.Count)
                return ItemSlotNetData.Empty;
            return _localInventory.SlotStates[index];
        }

        public ItemDataSO GetLocalItemData(int index)
        {
            return _localInventory?.GetItemData(index);
        }

        public ItemWorldView GetLocalView(int index)
        {
            if (_localViews == null || index < 0 || index >= _localViews.Length) return null;
            return _localViews[index];
        }

        public Transform FindLocalViewByName(string nameFragment)
        {
            if (_localViews == null) return null;
            foreach (var v in _localViews)
                if (v != null && v.gameObject.name.Contains(nameFragment))
                    return v.transform;
            return null;
        }

        public PlayerInventory GetLocalInventory() => _localInventory;
        public PlayerInventory GetOpponentInventory() => _opponentInventory;
        public PlayerState GetLocalPlayer() => _localPlayer;
        public bool IsLocalBound => _localBound;
        public bool IsOpponentBound => _opponentBound;
    }
}
