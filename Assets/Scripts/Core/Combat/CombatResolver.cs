using System;
using AbsoluteZero.Core.Buff;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Item.Data;
using AbsoluteZero.Core.Match;
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
                && IsSelectedItemValid(firstQueue.selectedAction.Value, firstPlayer)
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
                && IsSelectedItemValid(secondQueue.selectedAction.Value, secondPlayer)
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

        static bool IsSelectedItemValid(QueuedAction action, PlayerState player)
        {
            var inventory = player != null ? player.GetInventory() : null;
            return inventory != null
                && action.SlotIndex < inventory.SlotStates.Count
                && inventory.SlotStates[action.SlotIndex].IsUsable
                && inventory.GetItemData(action.SlotIndex) == action.ItemData;
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

            var outcome = action.ItemData.ComputeEffect(ctx);
            ItemEffectApplicator.Apply(ctx, outcome);
            user.GetInventory().ConsumeItem(action.SlotIndex);

            Debug.Log($"[COMBAT] ExecuteMain AFTER: P{userIdx}={user.Temperature.Value:F1}° P{targetIdx}={target.Temperature.Value:F1}°");

            return new CombatEvent
            {
                Type = CombatEventType.MainEffect,
                SourcePlayer = userIdx,
                TargetPlayer = targetIdx,
                ItemId = capturedItemId,
                UserResultTemp = user.Temperature.Value,
                TargetResultTemp = target.Temperature.Value,
                ImpactFlags = BuildImpactFlags(outcome, targetTempBefore, target.Temperature.Value),
                DefenseItemId = GetDefenseItemId(outcome)
            };
        }

        static byte BuildImpactFlags(ItemEffectOutcome outcome, float targetBefore, float targetAfter)
        {
            byte flags = 0;
            var defense = outcome.TargetDefenseCheck;
            bool defenseMatched = outcome.Blocked || (defense.HasValue
                && (defense.Value.Filter == outcome.TargetDamageFilter || defense.Value.Filter == DamageFilter.All)
                && defense.Value.BlockAmount > 0f && outcome.TargetDamage > 0f);
            if (defenseMatched) flags |= CombatImpactFlags.Defense;
            if (targetAfter < targetBefore) flags |= CombatImpactFlags.Damage;
            if (targetAfter > targetBefore) flags |= CombatImpactFlags.Recovery;
            return flags;
        }

        static short GetDefenseItemId(ItemEffectOutcome outcome)
        {
            var defense = outcome.TargetDefenseCheck;
            return defense.HasValue ? defense.Value.ItemId : (short)-1;
        }

        void ApplyDefense(ActionQueue queue, PlayerModifiers[] modifiers, int playerIdx, PlayerState player)
        {
            if (queue.selectedAction.HasValue
                && IsSelectedItemValid(queue.selectedAction.Value, player)
                && queue.selectedAction.Value.ItemData is DefenseItemDataSO defItem)
            {
                Debug.Log($"[COMBAT] ApplyDefense: P{playerIdx} activated '{defItem.ItemName}' — filter={defItem.Filter}, block={defItem.BlockAmount}");
                modifiers[playerIdx].ActiveDefense = new DefenseInfo
                {
                    ItemId = player.GetInventory().SlotStates[queue.selectedAction.Value.SlotIndex].ItemId,
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

        public MultiCombatResolution ResolveMulti(MatchCombatSnapshot snap, ActionIntent[] intents)
        {
            int seatCount = snap.SeatCount;
            if (seatCount == 0 || seatCount > 8 || intents == null || intents.Length < seatCount)
                return MultiCombatResolution.Create(Math.Max(seatCount, 1));
            if (snap.LifeStates == null || snap.LifeStates.Length < seatCount
                || snap.Modifiers == null || snap.Modifiers.Length < seatCount
                || snap.CurrentTemperatures == null || snap.CurrentTemperatures.Length < seatCount
                || snap.IsReady == null || snap.IsReady.Length < seatCount
                || snap.ItemRules == null)
                return MultiCombatResolution.Create(seatCount);

            var resolution = MultiCombatResolution.Create(seatCount);
            float[] workingTemps = new float[seatCount];
            Array.Copy(snap.CurrentTemperatures, workingTemps, seatCount);

            var actionOrder = BuildActionOrder(snap, intents, seatCount);
            Array.Copy(actionOrder, resolution.ActionOrder, seatCount);

            for (int i = 0; i < seatCount; i++)
            {
                resolution.MainItemIds[i] = intents[i].IsEmpty ? (short)-1 : intents[i].ItemId;
                resolution.SubItemIds[i] = -1;
            }

            ApplyDefenseMulti(snap, intents, resolution);

            for (int orderIdx = 0; orderIdx < actionOrder.Length; orderIdx++)
            {
                int seat = actionOrder[orderIdx];
                var intent = intents[seat];
                if (intent.IsEmpty) continue;
                if (snap.LifeStates[seat] != LifeState.Alive) continue;
                if (resolution.IsDead((byte)seat)) continue;
                if (snap.Modifiers[seat].ActionNeutralized) continue;
                if (resolution.ModifierChanges[seat].ActionNeutralized == true) continue;

                if (!TryFindItemRule(snap.ItemRules, intent.ItemId, out var rule)) continue;
                if (rule.IsDefense) continue;
                if (!ValidateSlot(snap, (byte)seat, intent.SlotIndex, intent.ItemId)) continue;

                byte targetSeat = intent.HasTarget ? intent.TargetSeat : (byte)seat;
                if (targetSeat >= seatCount) continue;

                if (intent.HasTarget && (snap.LifeStates[targetSeat] == LifeState.Ghost || resolution.IsDead(targetSeat)))
                {
                    Debug.Log($"[COMBAT-MULTI] P{seat} action cancelled: target P{targetSeat} is dead/Ghost");
                    continue;
                }

                ResolveAction((byte)seat, targetSeat, intent.SlotIndex, rule, snap, workingTemps, resolution);
                AnnotateActionEvents(resolution, snap, workingTemps, seat);
                resolution.AddInventoryChange(new InventoryDelta
                {
                    SeatIndex = (byte)seat,
                    SlotIndex = intent.SlotIndex,
                    Consumed = true,
                    ItemId = intent.ItemId
                });

                CheckDeath((byte)seat, targetSeat, workingTemps, resolution);
            }

            for (int i = 0; i < seatCount; i++)
            {
                workingTemps[i] = Mathf.Clamp(workingTemps[i], TemperatureSystem.MIN_TEMP, TemperatureSystem.MAX_TEMP);
                resolution.TemperatureDeltas[i] = workingTemps[i] - snap.CurrentTemperatures[i];
            }

            return resolution;
        }

        public MultiCombatResolution ResolveMultiDefenses(MatchCombatSnapshot snap, ActionIntent[] intents)
        {
            int seatCount = snap.SeatCount;
            var resolution = MultiCombatResolution.Create(Math.Max(seatCount, 1));
            if (seatCount == 0 || intents == null || intents.Length < seatCount)
                return resolution;
            ApplyDefenseMulti(snap, intents, resolution);
            return resolution;
        }

        public MultiCombatResolution ResolveMultiAction(MatchCombatSnapshot snap, ActionIntent intent)
        {
            int seatCount = snap.SeatCount;
            var resolution = MultiCombatResolution.Create(Math.Max(seatCount, 1));
            if (seatCount == 0 || intent.IsEmpty || intent.SourceSeat >= seatCount
                || snap.LifeStates == null || snap.LifeStates.Length < seatCount
                || snap.Modifiers == null || snap.Modifiers.Length < seatCount
                || snap.CurrentTemperatures == null || snap.CurrentTemperatures.Length < seatCount
                || snap.ItemRules == null)
                return resolution;

            byte actorSeat = intent.SourceSeat;
            if (snap.LifeStates[actorSeat] != LifeState.Alive
                || snap.Modifiers[actorSeat].ActionNeutralized
                || !TryFindItemRule(snap.ItemRules, intent.ItemId, out var rule)
                || rule.IsDefense
                || !ValidateSlot(snap, actorSeat, intent.SlotIndex, intent.ItemId))
                return resolution;

            byte targetSeat = intent.HasTarget ? intent.TargetSeat : actorSeat;
            if (targetSeat >= seatCount
                || (intent.HasTarget && snap.LifeStates[targetSeat] != LifeState.Alive))
                return resolution;

            var workingTemps = (float[])snap.CurrentTemperatures.Clone();
            resolution.MainItemIds[actorSeat] = intent.ItemId;
            ResolveAction(actorSeat, targetSeat, intent.SlotIndex, rule, snap, workingTemps, resolution);
            AnnotateActionEvents(resolution, snap, workingTemps, actorSeat);
            resolution.AddInventoryChange(new InventoryDelta
            {
                SeatIndex = actorSeat,
                SlotIndex = intent.SlotIndex,
                Consumed = true,
                ItemId = intent.ItemId
            });
            CheckDeath(actorSeat, targetSeat, workingTemps, resolution);

            for (int i = 0; i < seatCount; i++)
                resolution.TemperatureDeltas[i] = workingTemps[i] - snap.CurrentTemperatures[i];
            return resolution;
        }

        public int[] BuildActionOrder(MatchCombatSnapshot snap, ActionIntent[] intents, int seatCount)
        {
            int[] order = new int[seatCount];
            for (int i = 0; i < seatCount; i++) order[i] = i;

            bool heatWave = snap.Environment == EnvironmentType.HeatWaveWarning;

            Array.Sort(order, (a, b) =>
            {
                bool aDefense = !intents[a].IsEmpty && TryFindItemRule(snap.ItemRules, intents[a].ItemId, out var aRule) && aRule.IsDefense;
                bool bDefense = !intents[b].IsEmpty && TryFindItemRule(snap.ItemRules, intents[b].ItemId, out var bRule) && bRule.IsDefense;
                if (aDefense != bDefense) return aDefense ? -1 : 1;

                if (heatWave)
                {
                    float tempA = snap.CurrentTemperatures[a];
                    float tempB = snap.CurrentTemperatures[b];
                    if (!Mathf.Approximately(tempA, tempB)) return tempA.CompareTo(tempB);
                }

                int tickA = intents[a].IsEmpty ? int.MaxValue : intents[a].ReadyServerTick;
                int tickB = intents[b].IsEmpty ? int.MaxValue : intents[b].ReadyServerTick;
                if (tickA != tickB) return tickA.CompareTo(tickB);

                float tA = snap.CurrentTemperatures[a];
                float tB = snap.CurrentTemperatures[b];
                if (!Mathf.Approximately(tA, tB)) return tA.CompareTo(tB);

                return a.CompareTo(b);
            });

            return order;
        }

        void ApplyDefenseMulti(MatchCombatSnapshot snap, ActionIntent[] intents, MultiCombatResolution resolution)
        {
            for (int i = 0; i < snap.SeatCount; i++)
            {
                if (intents[i].IsEmpty) continue;
                if (snap.LifeStates[i] != LifeState.Alive) continue;
                if (snap.Modifiers[i].ActionNeutralized) continue;
                if (!TryFindItemRule(snap.ItemRules, intents[i].ItemId, out var rule)) continue;
                if (!rule.IsDefense) continue;
                if (!ValidateSlot(snap, (byte)i, intents[i].SlotIndex, intents[i].ItemId)) continue;

                resolution.ModifierChanges[i].SeatIndex = (byte)i;
                resolution.ModifierChanges[i].ActiveDefense = new DefenseInfo
                {
                    ItemId = rule.ItemId,
                    Filter = rule.AttackFilter,
                    BlockAmount = rule.DefenseReduction
                };
                resolution.AddInventoryChange(new InventoryDelta
                {
                    SeatIndex = (byte)i,
                    SlotIndex = intents[i].SlotIndex,
                    Consumed = true,
                    ItemId = intents[i].ItemId
                });
            }
        }

        void ResolveAction(byte actorSeat, byte targetSeat, byte slotIndex, ItemEffectRuleSnapshot rule,
            MatchCombatSnapshot snap, float[] workingTemps, MultiCombatResolution resolution)
        {
            switch (rule.EffectKind)
            {
                case ItemEffectKind.Defense:
                    break;

                case ItemEffectKind.DirectDamage:
                    float blocked = GetBlockedAmount(targetSeat, rule.AttackFilter, resolution, snap);
                    float damage = Mathf.Max(0f, rule.BaseDamage - blocked);
                    workingTemps[targetSeat] -= damage;
                    ClampTemp(workingTemps, targetSeat);
                    if (damage > 0f)
                        resolution.LastDamageSources[targetSeat] = DamageSource.Create(actorSeat, DamageOrigin.Item);
                    resolution.AddEvent(new CombatEvent
                    {
                        Type = CombatEventType.MainEffect,
                        SourcePlayer = actorSeat,
                        TargetPlayer = targetSeat,
                        ItemId = rule.ItemId,
                        UserResultTemp = workingTemps[actorSeat],
                        TargetResultTemp = workingTemps[targetSeat]
                    });
                    break;

                case ItemEffectKind.Equalize:
                    float userTemp = workingTemps[actorSeat];
                    float diff = userTemp - workingTemps[targetSeat];
                    if (diff < 0f)
                    {
                        float eqBlocked = GetBlockedAmount(targetSeat, rule.AttackFilter, resolution, snap);
                        float eqDamage = Mathf.Max(0f, -diff - eqBlocked);
                        workingTemps[targetSeat] -= eqDamage;
                        if (eqDamage > 0f)
                            resolution.LastDamageSources[targetSeat] = DamageSource.Create(actorSeat, DamageOrigin.Item);
                    }
                    else if (diff > 0f)
                    {
                        workingTemps[targetSeat] += diff;
                    }
                    ClampTemp(workingTemps, targetSeat);
                    resolution.AddEvent(new CombatEvent
                    {
                        Type = CombatEventType.MainEffect,
                        SourcePlayer = actorSeat,
                        TargetPlayer = targetSeat,
                        ItemId = rule.ItemId,
                        UserResultTemp = workingTemps[actorSeat],
                        TargetResultTemp = workingTemps[targetSeat]
                    });
                    break;

                case ItemEffectKind.Recovery:
                    if (rule.HealPerUse != null && rule.HealPerUse.Length > 0)
                    {
                        int useIndex = 0;
                        if (rule.MaxUses > 0
                            && snap.Inventories != null
                            && actorSeat < snap.Inventories.Length
                            && snap.Inventories[actorSeat].Slots != null
                            && slotIndex < snap.Inventories[actorSeat].Slots.Length)
                        {
                            useIndex = rule.MaxUses - snap.Inventories[actorSeat].Slots[slotIndex].RemainingUses;
                        }
                        float heal = rule.HealPerUse[Mathf.Clamp(useIndex, 0, rule.HealPerUse.Length - 1)];
                        workingTemps[actorSeat] += heal;
                        ClampTemp(workingTemps, actorSeat);
                    }
                    resolution.AddEvent(new CombatEvent
                    {
                        Type = CombatEventType.MainEffect,
                        SourcePlayer = actorSeat,
                        TargetPlayer = actorSeat,
                        ItemId = rule.ItemId,
                        UserResultTemp = workingTemps[actorSeat],
                        TargetResultTemp = workingTemps[actorSeat]
                    });
                    break;

                case ItemEffectKind.Buff:
                    if (!Mathf.Approximately(rule.ImmediateTempDelta, 0f))
                    {
                        float preBuff = workingTemps[actorSeat];
                        workingTemps[actorSeat] += rule.ImmediateTempDelta;
                        ClampTemp(workingTemps, actorSeat);
                        if (workingTemps[actorSeat] < preBuff)
                            resolution.LastDamageSources[actorSeat] = DamageSource.Create(actorSeat, DamageOrigin.Item);
                    }
                    if (rule.HasScheduledEffect)
                    {
                        resolution.AddScheduled(new ScheduledEffectDelta
                        {
                            TargetSeat = actorSeat,
                            SourceSeat = actorSeat,
                            Type = rule.ScheduledType,
                            Value = rule.ScheduledValue,
                            DelayTurns = rule.ScheduledDelay
                        });
                    }
                    resolution.AddEvent(new CombatEvent
                    {
                        Type = CombatEventType.MainEffect,
                        SourcePlayer = actorSeat,
                        TargetPlayer = actorSeat,
                        ItemId = rule.ItemId,
                        UserResultTemp = workingTemps[actorSeat],
                        TargetResultTemp = workingTemps[actorSeat]
                    });
                    break;

                case ItemEffectKind.Debuff:
                    float debufBlocked = GetBlockedAmount(targetSeat, rule.AttackFilter, resolution, snap);
                    if (debufBlocked >= float.MaxValue)
                    {
                        resolution.AddEvent(new CombatEvent
                        {
                            Type = CombatEventType.DefenseActivated,
                            SourcePlayer = actorSeat,
                            TargetPlayer = targetSeat,
                            ItemId = rule.ItemId,
                            UserResultTemp = workingTemps[actorSeat],
                            TargetResultTemp = workingTemps[targetSeat]
                        });
                        break;
                    }
                    if (!Mathf.Approximately(rule.ImmediateTempDelta, 0f))
                    {
                        if (rule.ImmediateTempDelta > 0f)
                            workingTemps[targetSeat] += rule.ImmediateTempDelta;
                        else
                        {
                            float debufDmg = Mathf.Max(0f, -rule.ImmediateTempDelta - debufBlocked);
                            workingTemps[targetSeat] -= debufDmg;
                            if (debufDmg > 0f)
                                resolution.LastDamageSources[targetSeat] = DamageSource.Create(actorSeat, DamageOrigin.Item);
                        }
                        ClampTemp(workingTemps, targetSeat);
                    }
                    if (rule.HasScheduledEffect)
                    {
                        resolution.AddScheduled(new ScheduledEffectDelta
                        {
                            TargetSeat = targetSeat,
                            SourceSeat = actorSeat,
                            Type = rule.ScheduledType,
                            Value = rule.ScheduledValue,
                            DelayTurns = rule.ScheduledDelay
                        });
                    }
                    resolution.AddEvent(new CombatEvent
                    {
                        Type = CombatEventType.MainEffect,
                        SourcePlayer = actorSeat,
                        TargetPlayer = targetSeat,
                        ItemId = rule.ItemId,
                        UserResultTemp = workingTemps[actorSeat],
                        TargetResultTemp = workingTemps[targetSeat]
                    });
                    break;

                case ItemEffectKind.Sabotage:
                    if (rule.BlocksTargetBasics)
                    {
                        resolution.PlayerStateChanges[targetSeat].SeatIndex = targetSeat;
                        resolution.PlayerStateChanges[targetSeat].IsBasicBlocked = true;
                    }
                    if (rule.NeutralizesTarget)
                    {
                        resolution.ModifierChanges[targetSeat].SeatIndex = targetSeat;
                        resolution.ModifierChanges[targetSeat].ActionNeutralized = true;
                    }
                    if (rule.InventoryAction != InventoryMutationType.None)
                    {
                        resolution.AddInventoryChange(new InventoryDelta
                        {
                            SeatIndex = actorSeat,
                            MutationType = rule.InventoryAction,
                            TargetSeat = targetSeat
                        });
                    }
                    resolution.AddEvent(new CombatEvent
                    {
                        Type = CombatEventType.MainEffect,
                        SourcePlayer = actorSeat,
                        TargetPlayer = targetSeat,
                        ItemId = rule.ItemId,
                        UserResultTemp = workingTemps[actorSeat],
                        TargetResultTemp = workingTemps[targetSeat]
                    });
                    break;

                case ItemEffectKind.FanControl:
                    if (rule.HasScheduledEffect)
                    {
                        byte fanTarget = rule.IsSelfTarget ? actorSeat : targetSeat;
                        resolution.AddScheduled(new ScheduledEffectDelta
                        {
                            TargetSeat = fanTarget,
                            SourceSeat = actorSeat,
                            Type = rule.ScheduledType,
                            Value = rule.ScheduledValue,
                            DelayTurns = rule.ScheduledDelay
                        });
                    }
                    else
                    {
                        byte fanTarget = rule.IsSelfTarget ? actorSeat : targetSeat;
                        resolution.PlayerStateChanges[fanTarget].SeatIndex = fanTarget;
                        resolution.PlayerStateChanges[fanTarget].NewFanSpeed = rule.IsSelfTarget ? rule.FanSpeedValue : rule.TargetFanSpeedValue;
                    }
                    resolution.AddEvent(new CombatEvent
                    {
                        Type = CombatEventType.MainEffect,
                        SourcePlayer = actorSeat,
                        TargetPlayer = rule.IsSelfTarget ? actorSeat : targetSeat,
                        ItemId = rule.ItemId,
                        UserResultTemp = workingTemps[actorSeat],
                        TargetResultTemp = workingTemps[rule.IsSelfTarget ? actorSeat : targetSeat]
                    });
                    break;

                case ItemEffectKind.Special:
                    if (rule.GrantsExtraAction)
                    {
                        resolution.ModifierChanges[actorSeat].SeatIndex = actorSeat;
                        resolution.ModifierChanges[actorSeat].HasExtraAction = true;
                    }
                    if (rule.RequiresTargetReady)
                    {
                        if (snap.IsReady[targetSeat])
                        {
                            resolution.ModifierChanges[actorSeat].SeatIndex = actorSeat;
                            resolution.ModifierChanges[actorSeat].OpponentRevealed = true;
                        }
                    }
                    resolution.AddEvent(new CombatEvent
                    {
                        Type = CombatEventType.MainEffect,
                        SourcePlayer = actorSeat,
                        TargetPlayer = targetSeat,
                        ItemId = rule.ItemId,
                        UserResultTemp = workingTemps[actorSeat],
                        TargetResultTemp = workingTemps[targetSeat]
                    });
                    break;
            }
        }

        float GetBlockedAmount(byte targetSeat, DamageFilter attackFilter,
            MultiCombatResolution resolution, MatchCombatSnapshot snap)
        {
            var defense = resolution.ModifierChanges[targetSeat].ActiveDefense
                ?? snap.Modifiers[targetSeat].ActiveDefense;
            if (!defense.HasValue) return 0f;
            if (defense.Value.Filter == attackFilter || defense.Value.Filter == DamageFilter.All)
            {
                resolution.DefenseReactionMask |= (byte)(1 << targetSeat);
                resolution.DefenseItemIds[targetSeat] = defense.Value.ItemId;
                return defense.Value.BlockAmount;
            }
            return 0f;
        }

        static void AnnotateActionEvents(MultiCombatResolution resolution,
            MatchCombatSnapshot snap, float[] workingTemps, int actorSeat)
        {
            for (int i = 0; i < resolution.EventCount; i++)
            {
                var evt = resolution.OrderedEvents[i];
                if ((evt.Type != CombatEventType.MainEffect
                    && evt.Type != CombatEventType.DefenseActivated)
                    || evt.SourcePlayer != actorSeat)
                    continue;
                int target = evt.TargetPlayer;
                if (target >= 0 && target < snap.SeatCount)
                {
                    evt.DefenseItemId = -1;
                    if ((resolution.DefenseReactionMask & (1 << target)) != 0)
                    {
                        evt.ImpactFlags |= CombatImpactFlags.Defense;
                        evt.DefenseItemId = resolution.DefenseItemIds[target];
                    }
                    if (workingTemps[target] < snap.CurrentTemperatures[target])
                        evt.ImpactFlags |= CombatImpactFlags.Damage;
                    else if (workingTemps[target] > snap.CurrentTemperatures[target])
                        evt.ImpactFlags |= CombatImpactFlags.Recovery;
                }
                resolution.OrderedEvents[i] = evt;
            }
        }

        void CheckDeath(byte actorSeat, byte targetSeat, float[] workingTemps, MultiCombatResolution resolution)
        {
            workingTemps[targetSeat] = Mathf.Clamp(workingTemps[targetSeat], TemperatureSystem.MIN_TEMP, TemperatureSystem.MAX_TEMP);
            workingTemps[actorSeat] = Mathf.Clamp(workingTemps[actorSeat], TemperatureSystem.MIN_TEMP, TemperatureSystem.MAX_TEMP);

            if (workingTemps[targetSeat] <= 0f && !resolution.IsDead(targetSeat))
            {
                resolution.MarkDead(targetSeat);
                resolution.AddEvent(new CombatEvent
                {
                    Type = CombatEventType.Death,
                    SourcePlayer = actorSeat,
                    TargetPlayer = targetSeat,
                    UserResultTemp = workingTemps[actorSeat],
                    TargetResultTemp = workingTemps[targetSeat]
                });
            }
            if (workingTemps[actorSeat] <= 0f && !resolution.IsDead(actorSeat))
            {
                resolution.MarkDead(actorSeat);
                resolution.AddEvent(new CombatEvent
                {
                    Type = CombatEventType.Death,
                    SourcePlayer = actorSeat,
                    TargetPlayer = actorSeat,
                    UserResultTemp = workingTemps[actorSeat],
                    TargetResultTemp = workingTemps[actorSeat]
                });
            }
        }

        static bool TryFindItemRule(ItemEffectRuleSnapshot[] rules, short itemId, out ItemEffectRuleSnapshot rule)
        {
            for (int i = 0; i < rules.Length; i++)
            {
                if (rules[i].ItemId == itemId)
                {
                    rule = rules[i];
                    return true;
                }
            }
            rule = default;
            return false;
        }

        static bool ValidateSlot(MatchCombatSnapshot snap, byte seat, byte slotIndex, short itemId)
        {
            if (snap.Inventories == null || seat >= snap.Inventories.Length) return false;
            var slots = snap.Inventories[seat].Slots;
            if (slots == null || slotIndex >= slots.Length) return false;
            var slot = slots[slotIndex];
            if (slot.ItemId < 0) return false;
            if (slot.ItemId != itemId) return false;
            if (slot.RemainingUses <= 0 && !slot.IsUnlimited) return false;
            return true;
        }

        static void ClampTemp(float[] temps, byte seat)
        {
            temps[seat] = Mathf.Clamp(temps[seat], TemperatureSystem.MIN_TEMP, TemperatureSystem.MAX_TEMP);
        }
    }
}
