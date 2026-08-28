using System;
using System.Collections.Generic;
using AbsoluteZero.Core.Item;
using Unity.Netcode;

namespace AbsoluteZero.Core.Combat
{
    public class CombatResult
    {
        public int FirstPlayerIndex;
        public int WinnerIndex = -1;
        public List<CombatEvent> Events = new();

        public float P1TempAtTurnStart;
        public float P2TempAtTurnStart;
        public float P1TempBeforeCombat;
        public float P2TempBeforeCombat;
        public float P1TempAfterCombat;
        public float P2TempAfterCombat;
        public short P1SubItemId = -1;
        public short P2SubItemId = -1;
        public short P1MainItemId = -1;
        public short P2MainItemId = -1;
        public uint ResultSequence;

        public CombatResultData ToNetData()
        {
            var data = new CombatResultData
            {
                FirstPlayerIndex = (byte)FirstPlayerIndex,
                WinnerIndex = (sbyte)WinnerIndex,
                EventCount = (byte)Math.Min(Events.Count, 2),
                P1TempAtTurnStart = P1TempAtTurnStart,
                P2TempAtTurnStart = P2TempAtTurnStart,
                P1TempBeforeCombat = P1TempBeforeCombat,
                P2TempBeforeCombat = P2TempBeforeCombat,
                P1TempAfterCombat = P1TempAfterCombat,
                P2TempAfterCombat = P2TempAfterCombat,
                P1SubItemId = P1SubItemId,
                P2SubItemId = P2SubItemId,
                P1MainItemId = P1MainItemId,
                P2MainItemId = P2MainItemId,
                ResultSequence = ResultSequence
            };

            if (Events.Count > 0)
            {
                data.Event0Type = Events[0].Type;
                data.Event0Source = (byte)Events[0].SourcePlayer;
                data.Event0Target = (byte)Events[0].TargetPlayer;
                data.Event0ItemId = Events[0].ItemId;
                data.Event0UserTemp = Events[0].UserResultTemp;
                data.Event0TargetTemp = Events[0].TargetResultTemp;
            }

            if (Events.Count > 1)
            {
                data.Event1Type = Events[1].Type;
                data.Event1Source = (byte)Events[1].SourcePlayer;
                data.Event1Target = (byte)Events[1].TargetPlayer;
                data.Event1ItemId = Events[1].ItemId;
                data.Event1UserTemp = Events[1].UserResultTemp;
                data.Event1TargetTemp = Events[1].TargetResultTemp;
            }

            return data;
        }
    }

    public struct CombatEvent
    {
        public CombatEventType Type;
        public int SourcePlayer;
        public int TargetPlayer;
        public short ItemId;
        public float UserResultTemp;
        public float TargetResultTemp;
    }

    public struct CombatResultData : INetworkSerializable
    {
        public byte FirstPlayerIndex;
        public sbyte WinnerIndex;
        public byte EventCount;

        public CombatEventType Event0Type;
        public byte Event0Source;
        public byte Event0Target;
        public short Event0ItemId;
        public float Event0UserTemp;
        public float Event0TargetTemp;

        public CombatEventType Event1Type;
        public byte Event1Source;
        public byte Event1Target;
        public short Event1ItemId;
        public float Event1UserTemp;
        public float Event1TargetTemp;

        public float P1TempAtTurnStart;
        public float P2TempAtTurnStart;
        public float P1TempBeforeCombat;
        public float P2TempBeforeCombat;
        public float P1TempAfterCombat;
        public float P2TempAfterCombat;
        public short P1SubItemId;
        public short P2SubItemId;
        public short P1MainItemId;
        public short P2MainItemId;
        public uint ResultSequence;
        public bool EndsMatch;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref FirstPlayerIndex);
            serializer.SerializeValue(ref WinnerIndex);
            serializer.SerializeValue(ref EventCount);

            serializer.SerializeValue(ref Event0Type);
            serializer.SerializeValue(ref Event0Source);
            serializer.SerializeValue(ref Event0Target);
            serializer.SerializeValue(ref Event0ItemId);
            serializer.SerializeValue(ref Event0UserTemp);
            serializer.SerializeValue(ref Event0TargetTemp);

            serializer.SerializeValue(ref Event1Type);
            serializer.SerializeValue(ref Event1Source);
            serializer.SerializeValue(ref Event1Target);
            serializer.SerializeValue(ref Event1ItemId);
            serializer.SerializeValue(ref Event1UserTemp);
            serializer.SerializeValue(ref Event1TargetTemp);

            serializer.SerializeValue(ref P1TempAtTurnStart);
            serializer.SerializeValue(ref P2TempAtTurnStart);
            serializer.SerializeValue(ref P1TempBeforeCombat);
            serializer.SerializeValue(ref P2TempBeforeCombat);
            serializer.SerializeValue(ref P1TempAfterCombat);
            serializer.SerializeValue(ref P2TempAfterCombat);
            serializer.SerializeValue(ref P1SubItemId);
            serializer.SerializeValue(ref P2SubItemId);
            serializer.SerializeValue(ref P1MainItemId);
            serializer.SerializeValue(ref P2MainItemId);
            serializer.SerializeValue(ref ResultSequence);
            serializer.SerializeValue(ref EndsMatch);
        }
    }
}
