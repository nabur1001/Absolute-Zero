using System;
using System.Collections.Generic;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Network;
using AbsoluteZero.Core.Player;
using Unity.Netcode;
using UnityEngine;

namespace AbsoluteZero.Core.Inventory
{
    /// <summary>
    /// Client-local four-player presentation layout. The owning player is always
    /// represented by the south/FPS view; remote visual slots are west, north and east.
    /// This component never changes authoritative seat identity or gameplay state.
    /// </summary>
    public sealed class MultiPerspectiveLayout : MonoBehaviour
    {
        sealed class RemoteInventoryBinding
        {
            public PlayerState Player;
            public PlayerInventory Inventory;
            public NetworkList<ItemSlotNetData>.OnListChangedDelegate Changed;
            public readonly List<GameObject> Views = new();
        }

        static readonly Vector3[] RemotePlayerPositions =
        {
            new(-4.35f, 1.60f, 4.45f),
            new(0f, 1.60f, 7.85f),
            new(4.35f, 1.60f, 4.45f)
        };

        readonly Dictionary<byte, RemoteInventoryBinding> _bindings = new();
        PlayerState _localPlayer;
        int _localSeat = -1;
        float _nextReconcile;
        bool _visualSlotsPositioned;
        bool _localIceboxPositioned;

        // Centered behind the local item row so the Ready control and arrow tail stay visible.
        static readonly Vector3 LocalIceboxPosition = new(0f, 0.05f, 3.75f);

        void Update()
        {
            if (!IsMultiScene()) return;

            if (!_visualSlotsPositioned)
                PositionRemoteVisualSlots();
            if (!_localIceboxPositioned)
                PositionLocalIcebox();

            if (Time.unscaledTime < _nextReconcile) return;
            _nextReconcile = Time.unscaledTime + 0.5f;
            ReconcileBindings();
        }

        void OnDestroy()
        {
            foreach (var binding in _bindings.Values)
                Unbind(binding);
            _bindings.Clear();
        }

        static bool IsMultiScene()
        {
            var root = MatchCompositionRoot.Instance;
            return root != null && root.ActiveConfig != null
                && root.ActiveConfig.Mode == GameMode.Multi;
        }

        void PositionRemoteVisualSlots()
        {
            int found = 0;
            for (int slot = 0; slot < RemotePlayerPositions.Length; slot++)
            {
                var visual = FindSceneObject($"EnemyPlayer_{slot}");
                if (visual == null) continue;
                visual.transform.position = RemotePlayerPositions[slot];
                found++;
            }

            _visualSlotsPositioned = found == RemotePlayerPositions.Length;
            if (_visualSlotsPositioned)
                Debug.Log("[MultiLayout] Remote visual slots positioned west/north/east");
        }

        void PositionLocalIcebox()
        {
            var spawnPoint = FindSceneObject("BoxSpawnPoint");
            if (spawnPoint == null) return;
            spawnPoint.transform.position = LocalIceboxPosition;
            var icebox = IceboxController.Instance;
            if (icebox == null || !icebox.TrySetPresentationPosition(LocalIceboxPosition)) return;
            _localIceboxPositioned = true;
            Debug.Log($"[MultiLayout] Local icebox positioned south-center at {LocalIceboxPosition}");
        }

        void ReconcileBindings()
        {
            var players = FindObjectsByType<PlayerState>(FindObjectsSortMode.None);
            _localPlayer = null;
            foreach (var player in players)
            {
                if (player != null && player.IsSpawned && player.IsOwner)
                {
                    _localPlayer = player;
                    _localSeat = player.PlayerIndex;
                    break;
                }
            }
            if (_localPlayer == null || _localSeat < 0) return;

            var present = new HashSet<byte>();
            foreach (var player in players)
            {
                if (player == null || !player.IsSpawned || player == _localPlayer || player.PlayerIndex < 0)
                    continue;

                byte seat = (byte)player.PlayerIndex;
                present.Add(seat);
                var inventory = player.GetInventory();
                if (inventory == null || inventory.SlotStates == null || inventory.SlotStates.Count == 0)
                    continue;
                if (!inventory.IsRegistryReady)
                    ItemManager.Instance?.InitializeClientRegistry(inventory);
                if (!inventory.IsRegistryReady) continue;

                if (_bindings.TryGetValue(seat, out var existing))
                {
                    if (existing.Player == player && existing.Inventory == inventory) continue;
                    Unbind(existing);
                    _bindings.Remove(seat);
                }

                var binding = new RemoteInventoryBinding
                {
                    Player = player,
                    Inventory = inventory
                };
                binding.Changed = _ => Rebuild(binding);
                inventory.SlotStates.OnListChanged += binding.Changed;
                _bindings.Add(seat, binding);
                Rebuild(binding);
            }

            var stale = new List<byte>();
            foreach (var pair in _bindings)
                if (!present.Contains(pair.Key) || pair.Value.Player == null)
                    stale.Add(pair.Key);
            foreach (byte seat in stale)
            {
                Unbind(_bindings[seat]);
                _bindings.Remove(seat);
            }
        }

        void Rebuild(RemoteInventoryBinding binding)
        {
            DestroyViews(binding);
            if (binding.Player == null || binding.Inventory == null || _localSeat < 0) return;

            int slot = AZPlayerVisual.GetRemoteVisualSlot(binding.Player.PlayerIndex, _localSeat);
            if (slot < 0 || slot >= RemotePlayerPositions.Length) return;

            int visibleIndex = 0;
            for (int i = 0; i < binding.Inventory.SlotStates.Count; i++)
            {
                var state = binding.Inventory.SlotStates[i];
                if (state.IsEmpty) continue;
                var item = binding.Inventory.GetItemData(i);
                if (item == null) continue;

                var go = new GameObject($"RemoteSeat{binding.Player.PlayerIndex}_Item{i}_{item.ItemName}");
                go.transform.SetParent(transform, false);
                go.transform.position = ItemPosition(slot, visibleIndex);
                go.transform.rotation = Quaternion.Euler(0f, 0f, ItemRotation(slot));
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = GameSprites.GetItemSprite(item.ItemName);
                renderer.sortingOrder = 8;
                if (renderer.sprite != null)
                {
                    Vector2 size = renderer.sprite.bounds.size;
                    float largestSide = Mathf.Max(size.x, size.y);
                    go.transform.localScale = Vector3.one * (largestSide > 0f ? 0.72f / largestSide : 0.38f);
                }
                else
                {
                    go.transform.localScale = Vector3.one * 0.38f;
                }

                var label = new GameObject("Uses");
                label.transform.SetParent(go.transform, false);
                label.transform.localPosition = new Vector3(0f, -0.72f, -0.01f);
                var text = label.AddComponent<TextMesh>();
                text.text = state.IsUnlimited ? "--" : state.RemainingUses.ToString();
                text.fontSize = 24;
                text.characterSize = 0.06f;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.color = Color.white;
                label.GetComponent<MeshRenderer>().sortingOrder = 9;

                binding.Views.Add(go);
                visibleIndex++;
            }

            Debug.Log($"[MultiLayout] Seat {binding.Player.PlayerIndex} inventory placed at visual slot {slot} ({visibleIndex} items)");
        }

        static Vector3 ItemPosition(int slot, int index)
        {
            int column = index % 4;
            int row = index / 4;
            float across = (column - 1.5f) * 0.62f;
            float inward = row * 0.72f;

            return slot switch
            {
                0 => new Vector3(-3.20f + inward, 0.72f, 4.45f + across),
                1 => new Vector3(across, 0.72f, 6.55f - inward),
                2 => new Vector3(3.20f - inward, 0.72f, 4.45f - across),
                _ => Vector3.zero
            };
        }

        static float ItemRotation(int slot) => slot switch
        {
            0 => -90f,
            1 => 180f,
            2 => 90f,
            _ => 0f
        };

        static GameObject FindSceneObject(string objectName)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
                    if (candidate.name == objectName) return candidate.gameObject;
            return null;
        }

        static void DestroyViews(RemoteInventoryBinding binding)
        {
            foreach (var view in binding.Views)
                if (view != null) Destroy(view);
            binding.Views.Clear();
        }

        static void Unbind(RemoteInventoryBinding binding)
        {
            if (binding.Inventory != null && binding.Inventory.SlotStates != null && binding.Changed != null)
                binding.Inventory.SlotStates.OnListChanged -= binding.Changed;
            DestroyViews(binding);
        }
    }
}
