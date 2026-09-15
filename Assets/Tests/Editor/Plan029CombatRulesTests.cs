using System;
using System.Collections.Generic;
using AbsoluteZero.Core.Buff;
using AbsoluteZero.Core.Combat;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Item.Data;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Player;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace AbsoluteZero.Tests
{
    public sealed class Plan029CombatRulesTests
    {
        readonly List<ScriptableObject> _created = new();

        [TestCase(CombatImpactFlags.Defense, true)]
        [TestCase(0, false)]
        [TestCase(CombatImpactFlags.Damage, false)]
        [TestCase(CombatImpactFlags.Defense | CombatImpactFlags.Damage, false)]
        [TestCase(CombatImpactFlags.Defense | CombatImpactFlags.Recovery, false)]
        public void FoodPresentation_OnlySuppressesIngestionForFullServerBlock(int flags, bool blocked)
        {
            var wire = RoundTrip(new CombatResultData { EventCount = 1, Event0ImpactFlags = (byte)flags });
            Assert.That(CombatVFXManager.IsFullyBlockedImpact(wire.Event0ImpactFlags), Is.EqualTo(blocked));
        }

        [TestCase("Attack/IceCream")]
        [TestCase("Attack/IcedAmericano")]
        [TestCase("Debuff/Samgyetang")]
        public void ConfiguredMask_BlocksFoodWithoutSchedulingDelayedEffects(string foodPath)
        {
            var mask = UnityEditor.AssetDatabase.LoadAssetAtPath<DefenseItemDataSO>("Assets/Data/Items/Random/Defense/Mask.asset");
            var food = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDataSO>("Assets/Data/Items/Random/" + foodPath + ".asset");
            Assert.That(mask, Is.Not.Null);
            Assert.That(food, Is.Not.Null);
            var modifiers = new PlayerModifiers[2];
            modifiers[1].ActiveDefense = new DefenseInfo { ItemId = 20, Filter = mask.Filter, BlockAmount = mask.BlockAmount };
            var snap = CreateSnapshot(new[] { 20f, 20f }, new[] { Slots(10), Slots(20) },
                new[] { ItemEffectRuleSnapshot.From(food, 10), ItemEffectRuleSnapshot.From(mask, 20) }, modifiers);
            var outcome = new CombatResolver().ResolveMultiAction(snap, new ActionIntent(0, 0, 10, 1, 1));
            Assert.That(outcome.TemperatureDeltas, Is.All.EqualTo(0));
            Assert.That(outcome.EventCount, Is.GreaterThan(0));
            Assert.That(CombatVFXManager.IsFullyBlockedImpact(outcome.OrderedEvents[0].ImpactFlags), Is.True);
            Assert.That(outcome.OrderedEvents[0].DefenseItemId, Is.EqualTo(20));
            Assert.That(outcome.ScheduledCount, Is.Zero);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void DuelDefenseMetadata_PreservesAttackerAndDefenderAcrossNetwork(int actor)
        {
            var result = new CombatResult();
            result.Events.Add(new CombatEvent {
                Type = CombatEventType.MainEffect, SourcePlayer = actor, TargetPlayer = 1 - actor,
                ItemId = 10, DefenseItemId = 20,
                ImpactFlags = CombatImpactFlags.Defense | CombatImpactFlags.Damage
            });
            var copy = RoundTrip(result.ToNetData());
            Assert.That(copy.Event0Source, Is.EqualTo(actor));
            Assert.That(copy.Event0Target, Is.EqualTo(1 - actor));
            Assert.That(copy.Event0DefenseItemId, Is.EqualTo(20));
            Assert.That(copy.Event0ImpactFlags, Is.EqualTo(CombatImpactFlags.Defense | CombatImpactFlags.Damage));
        }

        [Test]
        public void RemoteDefense_PreparesHiddenItemAndClearsAfterReaction()
        {
            var owner = new GameObject("DefenseVisualTest");
            var item = new GameObject("item");
            item.transform.SetParent(owner.transform);
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
            try
            {
                var visual = owner.AddComponent<AZPlayerVisual>();
                var renderer = item.AddComponent<SpriteRenderer>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(AZPlayerVisual).GetField("_itemRenderer", flags).SetValue(visual, renderer);
                typeof(AZPlayerVisual).GetField("_itemTransform", flags).SetValue(visual, item.transform);
                item.SetActive(false);
                visual.PrepareDefenseSprite(sprite);
                Assert.That(item.activeSelf, Is.True);
                Assert.That(renderer.sprite, Is.SameAs(sprite));
                visual.ReturnToIdle();
                Assert.That(item.activeSelf, Is.False);
                Assert.That(renderer.sprite, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void VisualSlots_AreUniqueForEveryLocalSeat_IndependentOfArrivalOrder(int count)
        {
            for (int local = 0; local < count; local++)
            {
                var used = new HashSet<int>();
                for (int remote = count - 1; remote >= 0; remote--)
                {
                    int slot = AZPlayerVisual.GetRemoteVisualSlot(remote, local);
                    if (remote == local) { Assert.That(slot, Is.EqualTo(-1)); continue; }
                    Assert.That(slot, Is.InRange(0, count - 2));
                    Assert.That(used.Add(slot), Is.True);
                    Assert.That(slot, Is.EqualTo(remote < local ? remote : remote - 1));
                }
                Assert.That(used.Count, Is.EqualTo(count - 1));
            }
            Assert.That(AZPlayerVisual.GetRemoteVisualSlot(1, -1), Is.EqualTo(-1));
        }

        [Test]
        public void PresentationBarrier_DisconnectDoesNotReleaseOtherClients_AndRejectsLateAck()
        {
            var barrier = new PresentationBarrier();
            Assert.That(barrier.Begin(7, new ulong[] { 0, 12, 42 }), Is.True);
            barrier.ReceiveAck(6, 12);
            barrier.HandleDisconnect(42);
            barrier.ReceiveAck(7, 0);
            Assert.That(barrier.IsActive, Is.True);
            barrier.ReceiveAck(7, 12);
            Assert.That(barrier.IsComplete, Is.True);
            Assert.That(barrier.Begin(8, new ulong[] { 0, 12 }), Is.True);
            barrier.ReceiveAck(7, 12);
            barrier.ReceiveAck(8, 0);
            Assert.That(barrier.IsActive, Is.True);
            barrier.ReceiveAck(8, 12);
            Assert.That(barrier.IsComplete, Is.True);
        }

        [Test]
        public void ForcedPresentationSettlement_RestoresCameraAndSettlesExactlyOnce()
        {
            var owner = new GameObject("Plan029VfxTest");
            var camera = new GameObject("Plan029CameraTest");
            int settled = 0;
            System.Action<uint> handler = seq => { if (seq == 19) settled++; };
            CombatVFXManager.OnPresentationSettled += handler;
            try
            {
                var vfx = owner.AddComponent<CombatVFXManager>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(CombatVFXManager).GetField("_activeSequence", flags).SetValue(vfx, (uint)19);
                typeof(CombatVFXManager).GetField("_movedCamera", flags).SetValue(vfx, camera.transform);
                var original = new Vector3(0, 5, -5);
                typeof(CombatVFXManager).GetField("_savedCameraPos", flags).SetValue(vfx, original);
                camera.transform.position = new Vector3(1, 2, 3);
                vfx.ForceSettleMultiPresentation(19);
                vfx.ForceSettleMultiPresentation(19);
                Assert.That(camera.transform.position, Is.EqualTo(original));
                Assert.That(vfx.HasPendingPresentation(19), Is.False);
                Assert.That(settled, Is.EqualTo(1));
            }
            finally
            {
                CombatVFXManager.OnPresentationSettled -= handler;
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(camera);
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var instance in _created)
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            _created.Clear();
        }

        [TestCase(3)]
        [TestCase(4)]
        public void EveryActorTargetPair_ChangesOnlyTheTarget_AndSnapshotRemainsImmutable(int count)
        {
            var attack = CreateAttack(5, DamageFilter.Temperature);
            var temperatures = new float[count];
            var inventories = new InventorySnapshot[count];
            for (int i = 0; i < count; i++) { temperatures[i] = 20; inventories[i] = Slots(10); }
            var snap = CreateSnapshot(temperatures, inventories, new[] { ItemEffectRuleSnapshot.From(attack, 10) });
            for (byte actor = 0; actor < count; actor++)
                for (byte target = 0; target < count; target++)
                {
                    if (actor == target) continue;
                    var result = new CombatResolver().ResolveMultiAction(snap, new ActionIntent(actor, 0, 10, target, 1));
                    for (int seat = 0; seat < count; seat++)
                    {
                        Assert.That(result.TemperatureDeltas[seat], Is.EqualTo(seat == target ? -5 : 0));
                        Assert.That(snap.CurrentTemperatures[seat], Is.EqualTo(20));
                    }
                    Assert.That(result.InventoryChangeCount, Is.EqualTo(1));
                    Assert.That(result.OrderedEvents[0].SourcePlayer, Is.EqualTo(actor));
                    Assert.That(result.OrderedEvents[0].TargetPlayer, Is.EqualTo(target));
                }
        }

        [TestCase(254, 0, 1)]
        [TestCase(0, 254, 1)]
        [TestCase(0, 0, 254)]
        public void InvalidActorSlotOrTarget_DoesNotMutateOrConsume(int actor, int slot, int target)
        {
            var snap = CreateTwoSeatSnapshot(CreateAttack(5, DamageFilter.Temperature), 0, DamageFilter.Temperature);
            var result = new CombatResolver().ResolveMultiAction(snap, new ActionIntent((byte)actor, (byte)slot, 10, (byte)target, 1));
            Assert.That(result.EventCount, Is.Zero);
            Assert.That(result.InventoryChangeCount, Is.Zero);
            Assert.That(result.TemperatureDeltas, Is.All.EqualTo(0));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void GhostActorOrTarget_CannotExecuteOrdinaryAttack(bool ghostActor)
        {
            var snap = CreateTwoSeatSnapshot(CreateAttack(5, DamageFilter.Temperature), 0, DamageFilter.Temperature);
            snap.LifeStates[ghostActor ? 0 : 1] = LifeState.Ghost;
            var result = new CombatResolver().ResolveMultiAction(snap, new ActionIntent(0, 0, 10, 1, 1));
            Assert.That(result.EventCount, Is.Zero);
            Assert.That(result.InventoryChangeCount, Is.Zero);
        }

        [Test]
        public void Barrier_TimeoutIsBounded_AndOldDuplicateOrUnknownAcksCannotReleaseNextSequence()
        {
            var barrier = new PresentationBarrier();
            barrier.Begin(50, new ulong[] { 0, 33 });
            var wait = barrier.WaitForCompletion(0);
            Assert.That(wait.MoveNext(), Is.False);
            Assert.That(barrier.State, Is.EqualTo(BarrierState.TimedOut));
            barrier.Begin(51, new ulong[] { 0, 33 });
            barrier.ReceiveAck(50, 33);
            barrier.ReceiveAck(51, 99);
            barrier.ReceiveAck(51, 0);
            barrier.ReceiveAck(51, 0);
            Assert.That(barrier.IsActive, Is.True);
            barrier.ReceiveAck(51, 33);
            Assert.That(barrier.IsComplete, Is.True);
            barrier.Reset();
            Assert.That(barrier.State, Is.EqualTo(BarrierState.Idle));
        }

        [TestCase(3)]
        [TestCase(4)]
        public void EveryWinnerSubset_ProducesExactlyTheJointCrossingMask(int count)
        {
            for (byte mask = 1; mask < (1 << count); mask++)
            {
                var before = new int[count]; var after = new int[count];
                for (int i = 0; i < count; i++) { before[i] = 4; after[i] = (mask & (1 << i)) != 0 ? 5 : 4; }
                Assert.That(MultiVictoryRules.FindThresholdCrossings(before, after, 5), Is.EqualTo(mask));
                Assert.That(MultiVictoryRules.FindThresholdCrossings(after, after, 5), Is.Zero);
            }
        }

        [TestCase(2f, DamageFilter.Temperature, 3f, true, true)]
        [TestCase(float.MaxValue, DamageFilter.Temperature, 0f, true, false)]
        [TestCase(float.MaxValue, DamageFilter.Food, 5f, false, true)]
        public void MultiAction_EmitsAuthoritativeDefenseAndDamageMetadata(
            float blockAmount, DamageFilter defenseFilter, float expectedDamage,
            bool expectsDefense, bool expectsDamage)
        {
            var attack = CreateAttack(5f, DamageFilter.Temperature);
            var snap = CreateTwoSeatSnapshot(attack, blockAmount, defenseFilter);

            var result = new CombatResolver().ResolveMultiAction(
                snap, new ActionIntent(0, 0, 10, 1, 100));

            Assert.That(result.EventCount, Is.EqualTo(1));
            Assert.That(result.TemperatureDeltas[1], Is.EqualTo(-expectedDamage).Within(0.001f));
            var evt = result.OrderedEvents[0];
            Assert.That((evt.ImpactFlags & CombatImpactFlags.Defense) != 0, Is.EqualTo(expectsDefense));
            Assert.That((evt.ImpactFlags & CombatImpactFlags.Damage) != 0, Is.EqualTo(expectsDamage));
            Assert.That(evt.DefenseItemId, Is.EqualTo(expectsDefense ? (short)20 : (short)-1));
            Assert.That(result.InventoryChangeCount, Is.EqualTo(1));
        }

        [Test]
        public void MultiAction_WhenSelectedCopyChanged_ProducesNoEffectOrConsumption()
        {
            var attack = CreateAttack(5f, DamageFilter.Temperature);
            var snap = CreateTwoSeatSnapshot(attack, 0f, DamageFilter.Temperature,
                actorSlotItemId: 11);

            var result = new CombatResolver().ResolveMultiAction(
                snap, new ActionIntent(0, 0, 10, 1, 100));

            Assert.That(result.EventCount, Is.Zero);
            Assert.That(result.InventoryChangeCount, Is.Zero);
            Assert.That(result.TemperatureDeltas[1], Is.Zero);
        }

        [Test]
        public void ActionQueue_CompactionShiftsIdentity_ButRemovedCopyCancels()
        {
            var item = CreateAttack(1f, DamageFilter.Temperature);
            var queue = new ActionQueue();
            queue.SetSelected(3, item, 1);
            queue.SetSub(2, item, 1);

            queue.OnSlotRemoved(1);
            Assert.That(queue.selectedAction.Value.SlotIndex, Is.EqualTo(2));
            Assert.That(queue.subAction.Value.SlotIndex, Is.EqualTo(1));

            queue.InvalidateSlot(2);
            Assert.That(queue.selectedAction.HasValue, Is.False);
            Assert.That(queue.subAction.HasValue, Is.True);
        }

        [Test]
        public void MultiOrder_DefenseFirst_ThenReadyTickTemperatureAndSeat()
        {
            var attack = CreateAttack(1f, DamageFilter.Temperature);
            var defense = CreateDefense(2f, DamageFilter.Temperature);
            var rules = new[]
            {
                ItemEffectRuleSnapshot.From(attack, 10),
                ItemEffectRuleSnapshot.From(defense, 20)
            };
            var snap = CreateSnapshot(
                new[] { 20f, 20f, 10f, 10f },
                new[]
                {
                    Slots(10), Slots(20), Slots(10), Slots(10)
                },
                rules);
            var intents = new[]
            {
                new ActionIntent(0, 0, 10, 1, 200),
                new ActionIntent(1, 0, 20, ActionIntent.NoTarget, 999),
                new ActionIntent(2, 0, 10, 0, 100),
                new ActionIntent(3, 0, 10, 0, 100)
            };

            var order = new CombatResolver().BuildActionOrder(snap, intents, 4);

            Assert.That(order, Is.EqualTo(new[] { 1, 2, 3, 0 }));
        }

        [Test]
        public void VictoryBoundary_ReturnsOnlyNewSingleOrJointCrossings()
        {
            Assert.That(MultiVictoryRules.FindThresholdCrossings(
                new[] { 4, 2, 0, 4 }, new[] { 5, 2, 0, 4 }, 5), Is.EqualTo(0b0001));
            Assert.That(MultiVictoryRules.FindThresholdCrossings(
                new[] { 4, 4, 1, 0 }, new[] { 5, 5, 1, 0 }, 5), Is.EqualTo(0b0011));
            Assert.That(MultiVictoryRules.FindThresholdCrossings(
                new[] { 5, 4, 0, 0 }, new[] { 6, 4, 0, 0 }, 5), Is.Zero);
        }

        [Test]
        public void MultiResolution_ThrowsInsteadOfSilentlyDroppingOverflow()
        {
            var result = MultiCombatResolution.Create(4);
            for (int i = 0; i < MultiCombatResolution.MaxEvents; i++)
                result.AddEvent(new CombatEvent());

            Assert.Throws<InvalidOperationException>(() => result.AddEvent(new CombatEvent()));
        }

        [Test]
        public void EventAndTerminalMetadata_RoundTripThroughNgoSerializer()
        {
            var originalEvent = new CombatEventNetData
            {
                ActorSeat = 3,
                TargetSeat = 1,
                ItemId = 14,
                EventType = (byte)CombatEventType.MainEffect,
                ActorResultTemp = 21f,
                TargetResultTemp = 8f,
                Flags = (byte)(CombatImpactFlags.Defense | CombatImpactFlags.Damage),
                DefenseItemId = 7
            };
            var eventCopy = RoundTrip(originalEvent);
            Assert.That(eventCopy.ActorSeat, Is.EqualTo(originalEvent.ActorSeat));
            Assert.That(eventCopy.TargetSeat, Is.EqualTo(originalEvent.TargetSeat));
            Assert.That(eventCopy.Flags, Is.EqualTo(originalEvent.Flags));
            Assert.That(eventCopy.DefenseItemId, Is.EqualTo(originalEvent.DefenseItemId));

            var terminal = new MultiTerminalResultNetData
            {
                DecidingSequence = 42,
                Outcome = MultiMatchOutcome.JointVictory,
                WinnerMask = 0b0101,
                Released = true
            };
            Assert.That(RoundTrip(terminal), Is.EqualTo(terminal));
        }

        [Test]
        public void OneVsOneResult_RoundTripsAuthoritativeDefenseMetadata()
        {
            var original = new CombatResultData
            {
                FirstPlayerIndex = 1,
                WinnerIndex = 1,
                EventCount = 2,
                Event0Source = 1,
                Event0Target = 0,
                Event0ItemId = 12,
                Event0ImpactFlags = CombatImpactFlags.Defense | CombatImpactFlags.Damage,
                Event0DefenseItemId = 3,
                Event1Source = 0,
                Event1Target = 1,
                Event1ItemId = 9,
                Event1ImpactFlags = CombatImpactFlags.Defense,
                Event1DefenseItemId = 7,
                ResultSequence = 91,
                EndsMatch = true
            };

            var copy = RoundTrip(original);

            Assert.That(copy.EventCount, Is.EqualTo(2));
            Assert.That(copy.Event0ImpactFlags, Is.EqualTo(original.Event0ImpactFlags));
            Assert.That(copy.Event0DefenseItemId, Is.EqualTo(3));
            Assert.That(copy.Event1ImpactFlags, Is.EqualTo(CombatImpactFlags.Defense));
            Assert.That(copy.Event1DefenseItemId, Is.EqualTo(7));
            Assert.That(copy.ResultSequence, Is.EqualTo(91));
            Assert.That(copy.EndsMatch, Is.True);
        }

        [Test]
        public void MultiDropFilter_ExcludesTarotWithoutChangingRawRegistryEligibility()
        {
            var tarot = Create<SpecialItemDataSO>();
            tarot.DropWeight = 100f;
            tarot.SpecialEffect = SpecialEffectType.RevealOpponent;
            var attack = CreateAttack(1f, DamageFilter.Temperature);
            attack.DropWeight = 1f;

            var raw = new ItemDropTable(new ItemDataSO[] { tarot, attack });
            var multi = new ItemDropTable(new ItemDataSO[] { tarot, attack }, item =>
                item is not SpecialItemDataSO special
                || special.SpecialEffect != SpecialEffectType.RevealOpponent);

            Assert.That(raw.Roll(item => ReferenceEquals(item, tarot)), Is.SameAs(tarot));
            Assert.That(multi.Roll(item => ReferenceEquals(item, tarot)), Is.Null);
            Assert.That(multi.Roll(), Is.SameAs(attack));
        }

        AttackItemDataSO CreateAttack(float damage, DamageFilter filter)
        {
            var item = Create<AttackItemDataSO>();
            item.Category = ItemCategory.Attack;
            item.Damage = damage;
            item.AttackFilter = filter;
            item.MaxUses = 1;
            return item;
        }

        DefenseItemDataSO CreateDefense(float amount, DamageFilter filter)
        {
            var item = Create<DefenseItemDataSO>();
            item.Category = ItemCategory.Defense;
            item.BlockAmount = amount;
            item.Filter = filter;
            item.MaxUses = 1;
            return item;
        }

        T Create<T>() where T : ScriptableObject
        {
            var instance = ScriptableObject.CreateInstance<T>();
            _created.Add(instance);
            return instance;
        }

        MatchCombatSnapshot CreateTwoSeatSnapshot(AttackItemDataSO attack,
            float defenseAmount, DamageFilter defenseFilter, short actorSlotItemId = 10)
        {
            var defense = CreateDefense(defenseAmount, defenseFilter);
            var modifiers = new PlayerModifiers[2];
            if (defenseAmount > 0f)
            {
                modifiers[1].ActiveDefense = new DefenseInfo
                {
                    ItemId = 20,
                    Filter = defenseFilter,
                    BlockAmount = defenseAmount
                };
            }
            return CreateSnapshot(
                new[] { 20f, 20f },
                new[] { Slots(actorSlotItemId), Slots(20) },
                new[]
                {
                    ItemEffectRuleSnapshot.From(attack, 10),
                    ItemEffectRuleSnapshot.From(defense, 20)
                },
                modifiers);
        }

        static InventorySnapshot Slots(short itemId)
            => new(0, new[] { new SlotSnapshot(itemId, false, 1) });

        static MatchCombatSnapshot CreateSnapshot(float[] temperatures,
            InventorySnapshot[] inventories, ItemEffectRuleSnapshot[] rules,
            PlayerModifiers[] modifiers = null)
        {
            int seats = temperatures.Length;
            var life = new LifeState[seats];
            var ready = new bool[seats];
            for (int i = 0; i < seats; i++)
            {
                life[i] = LifeState.Alive;
                ready[i] = true;
                inventories[i] = new InventorySnapshot((byte)i, inventories[i].Slots);
            }
            return new MatchCombatSnapshot(
                temperatures, temperatures, modifiers ?? new PlayerModifiers[seats], life,
                inventories, Array.Empty<ScheduledEffectSnapshot>(), new int[seats], ready,
                EnvironmentType.None, default, rules);
        }

        static T RoundTrip<T>(T value) where T : unmanaged, INetworkSerializable
        {
            using var writer = new FastBufferWriter(256, Allocator.Temp);
            writer.WriteNetworkSerializable(value);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out T copy);
            return copy;
        }
    }
}
