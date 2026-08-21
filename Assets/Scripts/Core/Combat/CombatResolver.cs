using AbsoluteZero.Core.Buff;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Item.Data;
using AbsoluteZero.Core.Player;
using UnityEngine;

namespace AbsoluteZero.Core.Combat
{
    public class CombatResolver
    {
        public CombatResult Resolve(
            ActionQueue p1Queue, ActionQueue p2Queue,
            PlayerModifiers[] modifiers,
            PlayerState p1, PlayerState p2,
            TemperatureSystem tempSystem,
            BuffDebuffSystem buffSystem,
            EnvironmentType environment = EnvironmentType.None,
            ItemDropTable dropTable = null)
        {
            var result = new CombatResult();

            string p1Action = p1Queue.selectedAction.HasValue ? p1Queue.selectedAction.Value.ItemData.ItemName : "NONE";
            string p2Action = p2Queue.selectedAction.HasValue ? p2Queue.selectedAction.Value.ItemData.ItemName : "NONE";
            Debug.Log($"[COMBAT] ===== CombatResolver.Resolve START =====");
            Debug.Log($"[COMBAT] P0 selected: {p1Action} | P1 selected: {p2Action}");
            Debug.Log($"[COMBAT] P0 temp: {p1.Temperature.Value:F1}° | P1 temp: {p2.Temperature.Value:F1}°");

            int firstIdx = DetermineOrder(p1Queue, p2Queue, p1, p2, environment);
            int secondIdx = 1 - firstIdx;
            result.FirstPlayerIndex = firstIdx;
            Debug.Log($"[COMBAT] Turn order: P{firstIdx} goes first, P{secondIdx} goes second");

            ApplyDefense(p1Queue, modifiers, 0, p1);
            ApplyDefense(p2Queue, modifiers, 1, p2);

            var firstQueue = firstIdx == 0 ? p1Queue : p2Queue;
            var secondQueue = secondIdx == 0 ? p1Queue : p2Queue;
            var firstPlayer = firstIdx == 0 ? p1 : p2;
            var secondPlayer = secondIdx == 0 ? p1 : p2;

            if (firstQueue.selectedAction.HasValue
                && !modifiers[firstIdx].ActionNeutralized
                && firstQueue.selectedAction.Value.ItemData is not DefenseItemDataSO)
            {
                Debug.Log($"[COMBAT] Executing FIRST player P{firstIdx} main: {firstQueue.selectedAction.Value.ItemData.ItemName}");
                result.Events.Add(ExecuteMain(
                    firstQueue.selectedAction.Value, firstPlayer, secondPlayer,
                    firstIdx, secondIdx, modifiers, tempSystem, buffSystem, dropTable));
            }
            else
            {
                string reason = !firstQueue.selectedAction.HasValue ? "no action selected"
                    : modifiers[firstIdx].ActionNeutralized ? "action NEUTRALIZED"
                    : "defense item (handled separately)";
                Debug.Log($"[COMBAT] FIRST player P{firstIdx} skipped: {reason}");
            }

            if (tempSystem.IsDead(secondPlayer))
            {
                result.WinnerIndex = firstIdx;
                return result;
            }
            if (tempSystem.IsDead(firstPlayer))
            {
                result.WinnerIndex = secondIdx;
                return result;
            }

            if (secondQueue.selectedAction.HasValue
                && !modifiers[secondIdx].ActionNeutralized
                && secondQueue.selectedAction.Value.ItemData is not DefenseItemDataSO)
            {
                Debug.Log($"[COMBAT] Executing SECOND player P{secondIdx} main: {secondQueue.selectedAction.Value.ItemData.ItemName}");
                result.Events.Add(ExecuteMain(
                    secondQueue.selectedAction.Value, secondPlayer, firstPlayer,
                    secondIdx, firstIdx, modifiers, tempSystem, buffSystem, dropTable));
            }
            else
            {
                string reason = !secondQueue.selectedAction.HasValue ? "no action selected"
                    : modifiers[secondIdx].ActionNeutralized ? "action NEUTRALIZED"
                    : "defense item (handled separately)";
                Debug.Log($"[COMBAT] SECOND player P{secondIdx} skipped: {reason}");
            }

            if (tempSystem.IsDead(firstPlayer))
                result.WinnerIndex = secondIdx;
            else if (tempSystem.IsDead(secondPlayer))
                result.WinnerIndex = firstIdx;

            return result;
        }

        int DetermineOrder(ActionQueue p1, ActionQueue p2, PlayerState p1State, PlayerState p2State,
            EnvironmentType environment)
        {
            if (environment == EnvironmentType.HeatWaveWarning)
            {
                if (p1State.Temperature.Value < p2State.Temperature.Value)
                {
                    Debug.Log($"[ENV] HeatWave order: P0 acts first (P0={p1State.Temperature.Value:F1}° < P1={p2State.Temperature.Value:F1}°)");
                    return 0;
                }
                if (p2State.Temperature.Value < p1State.Temperature.Value)
                {
                    Debug.Log($"[ENV] HeatWave order: P1 acts first (P1={p2State.Temperature.Value:F1}° < P0={p1State.Temperature.Value:F1}°)");
                    return 1;
                }
                Debug.Log($"[ENV] HeatWave: same temp ({p1State.Temperature.Value:F1}°) — falling back to ready order");
            }

            if (p1.readyTimestamp < p2.readyTimestamp) return 0;
            if (p2.readyTimestamp < p1.readyTimestamp) return 1;

            if (p1State.Temperature.Value < p2State.Temperature.Value) return 0;
            if (p2State.Temperature.Value < p1State.Temperature.Value) return 1;

            return 0;
        }

        CombatEvent ExecuteMain(QueuedAction action,
                                 PlayerState user, PlayerState target,
                                 int userIdx, int targetIdx,
                                 PlayerModifiers[] modifiers,
                                 TemperatureSystem tempSystem, BuffDebuffSystem buffSystem,
                                 ItemDropTable dropTable)
        {
            short capturedItemId = user.GetInventory().SlotStates[action.SlotIndex].ItemId;

            float userTempBefore = user.Temperature.Value;
            float targetTempBefore = target.Temperature.Value;

            Debug.Log($"[COMBAT] ExecuteMain: P{userIdx} uses '{action.ItemData.ItemName}' (slot={action.SlotIndex}, id={capturedItemId}) → P{targetIdx}");
            Debug.Log($"[COMBAT] ExecuteMain BEFORE: P{userIdx}={userTempBefore:F1}° P{targetIdx}={targetTempBefore:F1}°");

            var ctx = new ItemContext
            {
                User = user,
                Target = target,
                UserIndex = userIdx,
                TargetIndex = targetIdx,
                UserInventory = user.GetInventory(),
                TargetInventory = target.GetInventory(),
                AllModifiers = modifiers,
                TempSystem = tempSystem,
                BuffSystem = buffSystem,
                DropTable = dropTable,
                SlotIndex = action.SlotIndex,
                UserSlot = user.GetInventory().SlotStates[action.SlotIndex],
            };

            action.ItemData.ExecuteEffect(ctx);
            user.GetInventory().ConsumeItem(action.SlotIndex);

            Debug.Log($"[COMBAT] ExecuteMain AFTER: P{userIdx}={user.Temperature.Value:F1}° P{targetIdx}={target.Temperature.Value:F1}°");

            return new CombatEvent
            {
                Type = CombatEventType.MainEffect,
                SourcePlayer = userIdx,
                TargetPlayer = targetIdx,
                ItemId = capturedItemId,
                UserResultTemp = user.Temperature.Value,
                TargetResultTemp = target.Temperature.Value
            };
        }

        void ApplyDefense(ActionQueue queue, PlayerModifiers[] modifiers, int playerIdx, PlayerState player)
        {
            if (queue.selectedAction.HasValue && queue.selectedAction.Value.ItemData is DefenseItemDataSO defItem)
            {
                Debug.Log($"[COMBAT] ApplyDefense: P{playerIdx} activated '{defItem.ItemName}' — filter={defItem.Filter}, block={defItem.BlockAmount}");
                modifiers[playerIdx].ActiveDefense = new DefenseInfo
                {
                    Filter = defItem.Filter,
                    BlockAmount = defItem.BlockAmount
                };
                player.GetInventory().ConsumeItem(queue.selectedAction.Value.SlotIndex);
            }
            else
            {
                Debug.Log($"[COMBAT] ApplyDefense: P{playerIdx} — no defense item selected");
            }
        }
    }
}
