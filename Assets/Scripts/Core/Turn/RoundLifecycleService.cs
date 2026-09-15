using AbsoluteZero.Core.Combat;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Player;
using UnityEngine;

namespace AbsoluteZero.Core.Turn
{
    public class RoundLifecycleService
    {
        public int? DetermineDeathWinner(TemperatureSystem tempSystem, PlayerState p1, PlayerState p2)
        {
            bool p1Dead = tempSystem.IsDead(p1);
            bool p2Dead = tempSystem.IsDead(p2);
            if (!p1Dead && !p2Dead) return null;
            if (p1Dead && p2Dead) return -1;
            if (p1Dead) return 1;
            return 0;
        }

        public void ForceReady(PlayerState player)
        {
            player.IsReady.Value = true;
            player.IsFanActive.Value = false;
            player.GetActionQueue().SetReady(Time.time);
        }

        public void RevertFanUpgrade(PlayerState player)
        {
            if (!player.IsFanUpgraded.Value) return;
            Debug.Log($"[COMBAT] FanSpeed revert: P{player.PlayerIndex} {player.FanSpeed.Value} → {TemperatureSystem.DEFAULT_FAN_SPEED}");
            player.FanSpeed.Value = TemperatureSystem.DEFAULT_FAN_SPEED;
            player.IsFanUpgraded.Value = false;
        }

        public void ResetPlayersForNewRound(PlayerState p1, PlayerState p2)
        {
            p1.Temperature.Value = TemperatureSystem.MAX_TEMP;
            p2.Temperature.Value = TemperatureSystem.MAX_TEMP;
            p1.IsReady.Value = false;
            p2.IsReady.Value = false;
            p1.IsFanActive.Value = false;
            p2.IsFanActive.Value = false;
            p1.FanSpeed.Value = TemperatureSystem.DEFAULT_FAN_SPEED;
            p2.FanSpeed.Value = TemperatureSystem.DEFAULT_FAN_SPEED;
            p1.IsFanUpgraded.Value = false;
            p2.IsFanUpgraded.Value = false;

            p1.GetInventory().ResetForNewRound();
            p2.GetInventory().ResetForNewRound();
        }

        public void GrantStartingItems(PlayerState p1, PlayerState p2, ItemDropTable dropTable)
        {
            if (dropTable == null) return;
            p1.GetInventory().GrantRandomItems(4, dropTable);
            p2.GetInventory().GrantRandomItems(4, dropTable);
        }

        public void ResetSeatsForNewRound(MatchRoster roster,
            Buff.BuffDebuffSystem buffSystem)
        {
            int count = roster.RequiredPlayerCount;
            for (int i = 0; i < count; i++)
            {
                byte s = (byte)i;
                if (!roster.IsActive(s)) continue;

                roster.SetTemperature(s, TemperatureSystem.MAX_TEMP);
                roster.SetLifeState(s, LifeState.Alive);
                roster.SetFanSpeed(s, TemperatureSystem.DEFAULT_FAN_SPEED);
                roster.SetIsFanActive(s, false);
                roster.SetIsFanUpgraded(s, false);
                roster.SetIsBasicBlocked(s, false);
                var resetMods = default(PlayerModifiers);
                resetMods.FanSpeedMultiplier = 1f;
                resetMods.RecoveryMultiplier = 1f;
                roster.SetModifiers(s, resetMods);
                roster.ClearInventory(s);
            }

            buffSystem?.ClearAll();
        }

        public void ResetForNewTurn(PlayerState p1, PlayerState p2, PlayerModifiers[] modifiers)
        {
            p1.ResetForNewTurn();
            p2.ResetForNewTurn();
            modifiers[0].Reset();
            modifiers[1].Reset();

            p1.IsReady.Value = false;
            p2.IsReady.Value = false;
            p1.Temperature.Value = Mathf.Clamp(p1.Temperature.Value, 0f, TemperatureSystem.MAX_TEMP);
            p2.Temperature.Value = Mathf.Clamp(p2.Temperature.Value, 0f, TemperatureSystem.MAX_TEMP);

            p1.IsFanActive.Value = true;
            p2.IsFanActive.Value = true;
        }

        public void ResetForNewTurn(PlayerState[] players, PlayerModifiers[] modifiers)
        {
            for (int i = 0; i < modifiers.Length && i < players.Length; i++)
                modifiers[i].Reset();

            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == null) continue;
                if (players[i].CurrentLifeState.Value != LifeState.Alive) continue;
                players[i].ResetForNewTurn();
                players[i].IsReady.Value = false;
                players[i].Temperature.Value = Mathf.Clamp(
                    players[i].Temperature.Value, 0f, TemperatureSystem.MAX_TEMP);
                players[i].IsFanActive.Value = true;
            }
        }

        public void ResetPlayersForNewRound(PlayerState[] players)
        {
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == null) continue;
                players[i].Temperature.Value = TemperatureSystem.MAX_TEMP;
                players[i].CurrentLifeState.Value = LifeState.Alive;
                players[i].IsReady.Value = false;
                players[i].IsFanActive.Value = false;
                players[i].FanSpeed.Value = TemperatureSystem.DEFAULT_FAN_SPEED;
                players[i].IsFanUpgraded.Value = false;
                players[i].GetInventory()?.ResetForNewRound();
            }
        }

        public void GrantStartingItems(PlayerState[] players, ItemDropTable dropTable,
            int grantCount = 4, int maxRandomItems = int.MaxValue)
        {
            if (dropTable == null) return;
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == null) continue;
                players[i].GetInventory()?.GrantRandomItems(grantCount, dropTable, maxRandomItems);
            }
        }
    }
}
