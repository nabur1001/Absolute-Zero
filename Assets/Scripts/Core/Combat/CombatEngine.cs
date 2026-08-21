using AbsoluteZero.Core.Buff;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Item.Data;
using AbsoluteZero.Core.Player;
using UnityEngine;

namespace AbsoluteZero.Core.Combat
{
    public struct CombatSnapshot
    {
        public float P1TempAtTurnStart;
        public float P2TempAtTurnStart;
        public float P1TempBeforeCombat;
        public float P2TempBeforeCombat;
        public short P1SubItemId;
        public short P2SubItemId;
        public short P1MainItemId;
        public short P2MainItemId;
    }

    public class CombatEngine
    {
        readonly CombatResolver _resolver = new();

        public CombatSnapshot CapturePreCombatState(
            PlayerState p1, PlayerState p2,
            float p1TempAtTurnStart, float p2TempAtTurnStart)
        {
            var q1 = p1.GetActionQueue();
            var q2 = p2.GetActionQueue();

            return new CombatSnapshot
            {
                P1TempAtTurnStart = p1TempAtTurnStart,
                P2TempAtTurnStart = p2TempAtTurnStart,
                P1TempBeforeCombat = p1.Temperature.Value,
                P2TempBeforeCombat = p2.Temperature.Value,
                P1SubItemId = q1.subAction.HasValue
                    ? p1.GetInventory().SlotStates[q1.subAction.Value.SlotIndex].ItemId
                    : (short)-1,
                P2SubItemId = q2.subAction.HasValue
                    ? p2.GetInventory().SlotStates[q2.subAction.Value.SlotIndex].ItemId
                    : (short)-1,
                P1MainItemId = q1.selectedAction.HasValue
                    ? p1.GetInventory().SlotStates[q1.selectedAction.Value.SlotIndex].ItemId
                    : (short)-1,
                P2MainItemId = q2.selectedAction.HasValue
                    ? p2.GetInventory().SlotStates[q2.selectedAction.Value.SlotIndex].ItemId
                    : (short)-1,
            };
        }

        public CombatResult ResolveCombat(
            PlayerState p1, PlayerState p2,
            PlayerModifiers[] modifiers,
            TemperatureSystem tempSystem,
            BuffDebuffSystem buffSystem,
            EnvironmentType environment,
            CombatSnapshot snapshot,
            ref uint resultSequence,
            ItemDropTable dropTable = null)
        {
            var q1 = p1.GetActionQueue();
            var q2 = p2.GetActionQueue();

            Debug.Log("[COMBAT] --- Resolving main combat ---");
            var result = _resolver.Resolve(
                q1, q2, modifiers, p1, p2, tempSystem, buffSystem, environment, dropTable);

            result.P1TempAtTurnStart = snapshot.P1TempAtTurnStart;
            result.P2TempAtTurnStart = snapshot.P2TempAtTurnStart;
            result.P1TempBeforeCombat = snapshot.P1TempBeforeCombat;
            result.P2TempBeforeCombat = snapshot.P2TempBeforeCombat;
            result.P1TempAfterCombat = p1.Temperature.Value;
            result.P2TempAfterCombat = p2.Temperature.Value;
            result.P1SubItemId = snapshot.P1SubItemId;
            result.P2SubItemId = snapshot.P2SubItemId;
            result.P1MainItemId = snapshot.P1MainItemId;
            result.P2MainItemId = snapshot.P2MainItemId;
            result.ResultSequence = ++resultSequence;

            string winText = result.WinnerIndex >= 0 ? $"P{result.WinnerIndex} WINS" : "no death";
            Debug.Log($"[COMBAT] ===== COMBAT RESULT seq={result.ResultSequence}: {winText} =====");
            Debug.Log($"[COMBAT] Final temps: P0={p1.Temperature.Value:F1}° | P1={p2.Temperature.Value:F1}°");

            return result;
        }

        public void ExecuteSubItems(
            PlayerState player, int playerIndex, PlayerState opponent,
            PlayerModifiers[] modifiers,
            TemperatureSystem tempSystem, BuffDebuffSystem buffSystem,
            ItemDropTable dropTable)
        {
            var queue = player.GetActionQueue();
            if (!queue.subAction.HasValue)
            {
                Debug.Log($"[COMBAT] ExecuteSubItems: P{playerIndex} — no sub item");
                return;
            }

            var action = queue.subAction.Value;
            var inventory = player.GetInventory();

            float userTempBefore = player.Temperature.Value;
            float opponentTempBefore = opponent.Temperature.Value;

            Debug.Log($"[COMBAT] ExecuteSubItems: P{playerIndex} using '{action.ItemData.ItemName}' (slot={action.SlotIndex})");
            Debug.Log($"[COMBAT] ExecuteSubItems BEFORE: P{playerIndex}={userTempBefore:F1}° Opp={opponentTempBefore:F1}°");

            var ctx = new ItemContext
            {
                User = player,
                Target = opponent,
                UserIndex = playerIndex,
                TargetIndex = opponent.PlayerIndex,
                UserInventory = inventory,
                TargetInventory = opponent.GetInventory(),
                AllModifiers = modifiers,
                TempSystem = tempSystem,
                BuffSystem = buffSystem,
                DropTable = dropTable,
                SlotIndex = action.SlotIndex,
                UserSlot = inventory.SlotStates[action.SlotIndex],
            };

            action.ItemData.ExecuteEffect(ctx);
            inventory.ConsumeItem(action.SlotIndex);

            Debug.Log($"[COMBAT] ExecuteSubItems AFTER: P{playerIndex}={player.Temperature.Value:F1}° Opp={opponent.Temperature.Value:F1}°");
        }
    }
}
