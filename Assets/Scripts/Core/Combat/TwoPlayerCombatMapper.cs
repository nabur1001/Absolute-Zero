namespace AbsoluteZero.Core.Combat
{
    public static class TwoPlayerCombatMapper
    {
        public static float GetTempAtTurnStart(in CombatResultData d, int idx)
            => idx == 0 ? d.P1TempAtTurnStart : d.P2TempAtTurnStart;

        public static float GetTempBeforeCombat(in CombatResultData d, int idx)
            => idx == 0 ? d.P1TempBeforeCombat : d.P2TempBeforeCombat;

        public static float GetTempAfterCombat(in CombatResultData d, int idx)
            => idx == 0 ? d.P1TempAfterCombat : d.P2TempAfterCombat;

        public static short GetSubItemId(in CombatResultData d, int idx)
            => idx == 0 ? d.P1SubItemId : d.P2SubItemId;

        public static short GetMainItemId(in CombatResultData d, int idx)
            => idx == 0 ? d.P1MainItemId : d.P2MainItemId;

        public static float GetTempAtTurnStart(CombatResult r, int idx)
            => idx == 0 ? r.P1TempAtTurnStart : r.P2TempAtTurnStart;

        public static float GetTempBeforeCombat(CombatResult r, int idx)
            => idx == 0 ? r.P1TempBeforeCombat : r.P2TempBeforeCombat;

        public static float GetTempAfterCombat(CombatResult r, int idx)
            => idx == 0 ? r.P1TempAfterCombat : r.P2TempAfterCombat;

        public static short GetSubItemId(CombatResult r, int idx)
            => idx == 0 ? r.P1SubItemId : r.P2SubItemId;

        public static short GetMainItemId(CombatResult r, int idx)
            => idx == 0 ? r.P1MainItemId : r.P2MainItemId;

        public static float GetTempAtTurnStart(in CombatSnapshot s, int idx)
            => idx == 0 ? s.P1TempAtTurnStart : s.P2TempAtTurnStart;

        public static float GetTempBeforeCombat(in CombatSnapshot s, int idx)
            => idx == 0 ? s.P1TempBeforeCombat : s.P2TempBeforeCombat;

        public static short GetSubItemId(in CombatSnapshot s, int idx)
            => idx == 0 ? s.P1SubItemId : s.P2SubItemId;

        public static short GetMainItemId(in CombatSnapshot s, int idx)
            => idx == 0 ? s.P1MainItemId : s.P2MainItemId;
    }
}
