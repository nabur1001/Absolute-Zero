using AbsoluteZero.Core.Combat;
using AbsoluteZero.Core.Item;
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
    }
}
