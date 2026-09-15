using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Player;
using AbsoluteZero.Core.Turn;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AbsoluteZero.Core.Game
{
    /// <summary>
    /// 개발용 아이템 지급 치트 (에디터/개발 빌드 전용, 호스트에서만 동작).
    /// F1 집게손 / F2 안아줘요 / F3 드라이버 / F4 랜덤 / F5 물총 / F6 집게손 / F7 청테이프 / F8 스마트폰
    /// — 양쪽 플레이어 모두에게 지급. 미니게임을 드랍운과 무관하게 즉시 테스트하기 위한 도구.
    /// </summary>
    public class DebugItemGranter : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.f1Key.wasPressedThisFrame) GrantByName("Claw Machine");
            if (kb.f2Key.wasPressedThisFrame) GrantByName("Hug T-shirt");
            if (kb.f3Key.wasPressedThisFrame) GrantByName("Screwdriver");
            if (kb.f4Key.wasPressedThisFrame) GrantRandom();
            if (kb.f5Key.wasPressedThisFrame) GrantByName("Water Gun");
            if (kb.f6Key.wasPressedThisFrame) GrantByName("Claw Machine");
            if (kb.f7Key.wasPressedThisFrame) GrantByName("Blue Tape");
            if (kb.f8Key.wasPressedThisFrame) GrantByName("Smartphone");
        }

        static void GrantByName(string itemName)
        {
            var itemManager = ItemManager.Instance;
            if (itemManager == null) return;

            var items = itemManager.GetAllItems();
            short itemId = -1;
            for (short i = 0; i < items.Length; i++)
            {
                if (items[i] != null && items[i].ItemName == itemName)
                {
                    itemId = i;
                    break;
                }
            }
            if (itemId < 0)
            {
                Debug.LogWarning($"[DebugItemGranter] '{itemName}' not found in registry");
                return;
            }

            foreach (var inv in FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None))
            {
                bool granted = inv.GrantSpecificItem(itemId);
                Debug.Log($"[DebugItemGranter] {itemName} → {inv.gameObject.name}: {(granted ? "지급" : "실패")}");
            }
        }

        static void GrantRandom()
        {
            var itemManager = ItemManager.Instance;
            if (itemManager == null) return;
            var dropTable = TurnManager.Instance != null
                ? TurnManager.Instance.GetDropTable()
                : itemManager.GetDropTable();

            foreach (var inv in FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None))
                inv.GrantRandomItems(1, dropTable);
            Debug.Log("[DebugItemGranter] 랜덤 아이템 +1 (양쪽)");
        }
#endif
    }
}
