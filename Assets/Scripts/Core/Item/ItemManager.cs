using AbsoluteZero.Core.Item.Data;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Player;
using Unity.Netcode;
using UnityEngine;

namespace AbsoluteZero.Core.Item
{
    public class ItemManager : NetworkBehaviour
    {
        public static ItemManager Instance { get; private set; }

        [Header("Item Registry")]
        [SerializeField] ItemDataSO[] allItems;

        [Header("Basic Item IDs (index in allItems)")]
        [SerializeField] short fanItemId = 0;
        [SerializeField] short windbreakerItemId = 1;
        [SerializeField] short warmTeaItemId = 2;
        [SerializeField] short catItemId = 3;

        ItemDropTable _dropTable;
        ItemDropTable _multiDropTable;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instance = this;

            if (allItems == null || allItems.Length == 0)
            {
                Debug.LogError("[ItemManager] allItems array is empty! Assign SO assets in Inspector.");
                return;
            }

            if (IsServer)
                _dropTable = new ItemDropTable(allItems);

            Debug.Log($"[ItemManager] {allItems.Length} items loaded from SO assets");
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            base.OnNetworkDespawn();
        }

        public void InitializePlayerInventory(PlayerInventory inventory)
        {
            if (!IsServer) return;

            inventory.Initialize(allItems);
            inventory.InitializeBasicItems(fanItemId, windbreakerItemId, warmTeaItemId, catItemId);

            if (_dropTable != null)
                inventory.GrantRandomItems(4, _dropTable);
        }

        public void InitializePlayerInventory(PlayerInventory inventory, IGameModeRule rule)
        {
            if (!IsServer || inventory == null || rule == null) return;
            if (allItems == null || allItems.Length == 0) return;

            inventory.Initialize(allItems);
            bool basicOk = inventory.InitializeBasicItems(fanItemId, windbreakerItemId, warmTeaId: warmTeaItemId, catId: catItemId,
                isWindbreakerUnlimited: rule.IsWindbreakerUnlimited);
            if (!basicOk) return;

            var dropTable = GetRuleAwareDropTable(rule);
            if (dropTable != null)
                inventory.GrantRandomItems(rule.InitialRandomItems, dropTable, rule.MaxRandomItems);
        }

        public void GrantDeathmatchItems(PlayerInventory inventory, IGameModeRule rule)
        {
            if (!IsServer || inventory == null || rule == null) return;
            if (rule.DeathmatchGrantCount <= 0) return;

            var dropTable = GetRuleAwareDropTable(rule);
            if (dropTable != null)
                inventory.FillRandomSlotsWithSeparateCopies(rule.MaxRandomItems, dropTable);
        }

        public ItemDropTable GetRuleAwareDropTable(IGameModeRule rule)
        {
            if (rule == null || rule.IsTarotAllowed)
                return _dropTable;
            if (allItems == null || allItems.Length == 0)
                return null;
            return _multiDropTable ??= new ItemDropTable(allItems, item =>
                !(item is SpecialItemDataSO sp && sp.SpecialEffect == SpecialEffectType.RevealOpponent));
        }

        public void InitializeClientRegistry(PlayerInventory inventory)
        {
            if (allItems != null)
                inventory.Initialize(allItems);
        }

        public ItemDropTable GetDropTable() => _dropTable;
        public ItemDataSO[] GetAllItems() => allItems;

        public ItemDataSO GetItemData(short itemId)
        {
            if (itemId < 0 || itemId >= allItems.Length) return null;
            return allItems[itemId];
        }
    }
}
