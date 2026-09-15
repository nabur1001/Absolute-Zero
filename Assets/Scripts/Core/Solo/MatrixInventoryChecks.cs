#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using AbsoluteZero.Core.Combat;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Item.Data;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Player;
using UnityEngine;

namespace AbsoluteZero.Core.Solo
{
    public sealed partial class MatrixScenarioProbe
    {
        static void Require(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            Debug.Log("[MATRIX] ASSERT " + name);
        }
        void CheckServices(PlayerState[] players)
        {
            var root = MatchCompositionRoot.Instance;
            var roster = root.Roster;
            var state = root.NetworkState;
            var inventories = players.Select(p => Enumerable.Range(0, p.GetInventory().SlotStates.Count)
                .Select(i => p.GetInventory().SlotStates[i]).ToArray()).ToArray();
            var temperatures = players.Select(p => p.Temperature.Value).ToArray();
            var modifiers = new PlayerModifiers[_count];
            for (int i = 0; i < _count; i++) modifiers[i].Reset();
            var death = new AuthoritativeDeathService(roster, state);
            using var ghosts = new GhostSkillService(roster, modifiers);
            try
            {
                roster.SetLifeState(0, LifeState.Ghost);
                players[1].Temperature.Value = 30;
                Require(ghosts.TryUseFrostStrike(0, 1, state, death, players, roster, 1), "frost first use accepted");
                Require(players[1].Temperature.Value == 15 && state.ServerGetCooldown(0, 0) == 3, "frost damage and cooldown");
                Require(!ghosts.TryUseFrostStrike(0, 1, state, death, players, roster, 1) && players[1].Temperature.Value == 15,
                    "frost duplicate blocked by cooldown");
                Require(ghosts.TryUseChillAura(0, 2, state, modifiers, roster, 1), "chill accepted");
                Require(modifiers[2].FanSpeedMultiplier == 2 && modifiers[2].RecoveryMultiplier == .5f, "chill applies both modifiers");
                Require(!ghosts.TryUseChillAura(0, 2, state, modifiers, roster, 1), "chill duplicate blocked");
                ghosts.ExpireChillAuras(2, modifiers);
                Require(modifiers[2].FanSpeedMultiplier == 2, "chill retained through next turn");
                ghosts.ExpireChillAuras(3, modifiers);
                Require(modifiers[2].FanSpeedMultiplier == 1 && modifiers[2].RecoveryMultiplier == 1,
                    "chill expiry restores modifiers");
                Require(death.TryKill(1, DamageSource.None), "natural death accepted");
                Require(state.KillScores[0] == 0, "natural death awards no kill");
                Require(!death.TryKill(1, DamageSource.Create(0, DamageOrigin.Item)), "duplicate death rejected");
                Require(death.TryKill(2, DamageSource.Create(2, DamageOrigin.Item)) && state.KillScores[2] == 0,
                    "self kill awards no score");
                Require(death.TryKill(3, DamageSource.Create(0, DamageOrigin.GhostFrost)) && state.KillScores[0] == 1,
                    "ghost receives kill credit");
                Require(death.ConsumeDeathMask() == 14 && death.ConsumeDeathMask() == 0, "death mask consumed once");
                ghosts.Dispose(); ghosts.Dispose();
                Require(!ghosts.TryUseFrostStrike(0, 1, state, death, players, roster, 5), "disposed ghost service rejects calls");
            }
            finally
            {
                for (int i = 0; i < _count; i++)
                {
                    roster.SetLifeState((byte)i, LifeState.Alive);
                    players[i].Temperature.Value = temperatures[i];
                    var slots = players[i].GetInventory().SlotStates;
                    slots.Clear(); foreach (var slot in inventories[i]) slots.Add(slot);
                }
                state.ServerResetKillScores(); state.GhostCooldowns.Clear();
            }
        }
        void CheckInventory(PlayerState[] players)
        {
            var a = players[0].GetInventory(); var b = players[1].GetInventory();
            var savedA = Enumerable.Range(0, a.SlotStates.Count).Select(i => a.SlotStates[i]).ToArray();
            var savedB = Enumerable.Range(0, b.SlotStates.Count).Select(i => b.SlotStates[i]).ToArray();
            float savedTemperature = players[0].Temperature.Value;
            var items = ItemManager.Instance.GetAllItems();
            short attack = (short)Array.FindIndex(items, i => i.ItemName == "Ice Cream");
            short alternate = (short)Array.FindIndex(items, i => i.ItemName == "Hot Pack");
            short sub = (short)Array.FindIndex(items, i => i.SlotType == ItemSlotType.Sub && i.Persistence == ItemPersistence.RandomConsumable);
            var queue = players[0].GetActionQueue();
            try
            {
                a.SlotStates.Clear(); b.SlotStates.Clear();
                a.GrantSpecificItem(attack);
                var copy = a.SlotStates[0]; copy.RemainingUses = 2; a.SlotStates[0] = copy;
                a.SlotStates.Add(copy); a.SlotStates.Add(copy);
                var constrained = new ItemDropTable(new[] { items[attack], items[alternate] });
                Require(a.FillRandomSlotsWithSeparateCopies(4, constrained) == 1, "top-up grants only one empty place");
                Require(a.SlotStates.Count == 4 && a.SlotStates[0].RemainingUses == 2
                    && a.SlotStates[1].RemainingUses == 2 && a.SlotStates[2].RemainingUses == 2,
                    "top-up preserves existing copies and uses");
                Require(a.SlotStates[3].ItemId == alternate, "top-up enforces three-copy limit");
                Require(a.FillRandomSlotsWithSeparateCopies(4, constrained) == 0, "full capacity grants nothing");

                a.SlotStates.Clear(); a.GrantSpecificItem(attack);
                queue.SetSelected(0, items[attack], 1);
                b.StealRandomItem(a);
                Require(!queue.selectedAction.HasValue && a.SlotStates[0].IsEmpty, "stealing selected copy cancels selection");
                a.SlotStates.Clear(); a.GrantSpecificItem(sub);
                queue.SetSelected(0, items[sub], 1);
                a.RerollAllRandom(new ItemDropTable(new[] { items[sub] }));
                Require(!queue.selectedAction.HasValue, "reroll cancels even same-ID replacement");
                a.SlotStates.Clear(); a.SlotStates.Add(ItemSlotNetData.Empty); a.GrantSpecificItem(attack);
                queue.SetSelected(1, items[attack], 1);
                a.CompactSlots();
                Require(queue.selectedAction.HasValue && queue.selectedAction.Value.SlotIndex == 0,
                    "compaction retains selected copy");
                queue.Clear();

                var rule = MatchCompositionRoot.Instance.ActiveConfig.Rule;
                var table = ItemManager.Instance.GetRuleAwareDropTable(rule);
                if (!rule.IsTarotAllowed)
                {
                    Require(table.Roll(i => i is SpecialItemDataSO s && s.SpecialEffect == SpecialEffectType.RevealOpponent) == null,
                        "actual Multi table cannot roll Tarot");
                    var thresholds = new bool[3];
                    a.SlotStates.Clear(); players[0].Temperature.Value = 9;
                    new TemperatureSystem().CheckThresholds(players[0], a, thresholds, table, true, rule.MaxRandomItems);
                    Require(thresholds.All(x => x), "30/20/10 thresholds recorded");
                    Require(Enumerable.Range(0, a.SlotStates.Count).All(i => a.GetItemData(i) is not SpecialItemDataSO s
                        || s.SpecialEffect != SpecialEffectType.RevealOpponent), "threshold grants exclude Tarot");
                    string before = string.Join(",", Enumerable.Range(0, a.SlotStates.Count).Select(i => a.SlotStates[i].ItemId + "/" + a.SlotStates[i].RemainingUses));
                    new TemperatureSystem().CheckThresholds(players[0], a, thresholds, table, true, rule.MaxRandomItems);
                    Require(before == string.Join(",", Enumerable.Range(0, a.SlotStates.Count).Select(i => a.SlotStates[i].ItemId + "/" + a.SlotStates[i].RemainingUses)),
                        "threshold grant is idempotent");
                }
            }
            finally
            {
                a.SlotStates.Clear(); foreach (var slot in savedA) a.SlotStates.Add(slot);
                b.SlotStates.Clear(); foreach (var slot in savedB) b.SlotStates.Add(slot);
                players[0].Temperature.Value = savedTemperature;
                queue.Clear();
            }
        }
    }
}
#endif
