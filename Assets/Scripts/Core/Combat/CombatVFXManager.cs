using System.Collections;
using System.Collections.Generic;
using AbsoluteZero.Core.Audio;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Item.Data;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Player;
using AbsoluteZero.Core.Player.Identity;
using AbsoluteZero.Core.Turn;
using AbsoluteZero.Core.Inventory;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Pool;

namespace AbsoluteZero.Core.Combat
{
    public class CombatVFXManager : MonoBehaviour
    {
        public static CombatVFXManager Instance { get; private set; }

        [SerializeField] GameObject _hitEffectPrefab;
        [SerializeField] GameObject _iceBreakEffectPrefab;
        [SerializeField] GameObject _finalBreakEffectPrefab;

        public bool IsPlaying { get; private set; }

        public static event System.Action OnTempOverridesClear;
        public static event System.Action<float, float> OnTempTargetsOverride;
        public static event System.Action<int, float> OnPlayerTempOverride;
        public static event System.Action<int> OnAttackerChanged;
        public static event System.Action<uint> OnPresentationSettled;

        uint _activeSequence;
        bool _sequenceCompleted;
        Coroutine _multiPresentationQueueRoutine;
        uint _lastCompletedMultiSequence;
        readonly Queue<PendingMultiPresentation> _pendingMultiPresentations = new();
        public bool HasPendingPresentation(uint sequence)
        {
            if (!_sequenceCompleted && _activeSequence == sequence) return true;
            foreach (var pending in _pendingMultiPresentations)
                if (pending.Sequence == sequence) return true;
            return false;
        }

        public void ForceSettleMultiPresentation(uint sequence)
        {
            if (!HasPendingPresentation(sequence)) return;

            Debug.LogWarning($"[CombatVFX] Authoritative settlement forced for seq={sequence}");
            StopAllCoroutines();
            RestorePresentationCamera();
            FPSVisualController.Instance?.ReturnToIdle();
            _multiPresentationQueueRoutine = null;
            _pendingMultiPresentations.Clear();

            foreach (var visual in FindObjectsByType<AZPlayerVisual>(FindObjectsSortMode.None))
            {
                if (visual == null) continue;
                if (visual.IsGhostTransitionPending || visual.IsDead)
                    visual.SettleDeathPresentation();
                else
                    visual.ReturnToIdle();
            }

            DestroyTransientPresentationObject("FeedSprite");
            DestroyTransientPresentationObject("CatAnim");
            DestroyTransientPresentationObject("CatAnimTemp");

            _activeSequence = sequence;
            _sequenceCompleted = false;
            CompletePresentationSequence(sequence);
            _lastCompletedMultiSequence = sequence;
        }

        static void DestroyTransientPresentationObject(string objectName)
        {
            var transient = GameObject.Find(objectName);
            if (transient != null) Destroy(transient);
        }

        struct PendingMultiPresentation
        {
            public bool IsCombat;
            public CombatResolutionBatchNetData Batch;
            public byte DeathMask;
            public bool EndsRound;
            public uint Sequence;
        }

        Transform _hugMovedTransform;
        Vector3 _hugSavedPos;
        Transform _movedCamera;
        Vector3 _savedCameraPos;

        void RestorePresentationCamera()
        {
            if (_movedCamera != null) _movedCamera.position = _savedCameraPos;
            _movedCamera = null;
        }

        readonly Dictionary<GameObject, ObjectPool<GameObject>> _particlePools = new();

        void CompletePresentationSequence(uint sequence)
        {
            if (_sequenceCompleted || sequence != _activeSequence) return;
            _sequenceCompleted = true;
            RestorePresentationCamera();

            if (_hugMovedTransform != null)
            {
                _hugMovedTransform.position = _hugSavedPos;
                _hugMovedTransform = null;
            }

            try { InventoryPresenter.Instance?.UnlockRebuild(); }
            catch (System.Exception e) { Debug.LogException(e); }

            try { OnTempOverridesClear?.Invoke(); }
            catch (System.Exception e) { Debug.LogException(e); }

            IsPlaying = false;

            try { OnAttackerChanged?.Invoke(-1); }
            catch (System.Exception e) { Debug.LogException(e); }

            Debug.Log($"[CombatVFX] Presentation complete: seq={sequence}");

            try
            {
                var nm = NetworkManager.Singleton;
                if (nm != null && nm.IsConnectedClient)
                {
                    var localPlayer = nm.LocalClient?.PlayerObject?.GetComponent<PlayerState>();
                    localPlayer?.PresentationAckServerRpc(sequence);
                }
            }
            catch (System.Exception e) { Debug.LogException(e); }
            try { OnPresentationSettled?.Invoke(sequence); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        static readonly WaitForSeconds _waitIntro = new(0.5f);
        static readonly WaitForSeconds _waitBriefPause = new(0.3f);
        static readonly WaitForSeconds _waitDamageReact = new(0.7f);
        static readonly WaitForSeconds _waitFeedHalf = new(0.5f);
        static readonly WaitForSeconds _waitBuldak07 = new(0.7f);
        static readonly WaitForSeconds _waitBuldak02 = new(0.2f);
        static readonly WaitForSeconds _waitHug03 = new(0.3f);
        static readonly WaitForSeconds _waitHug08 = new(0.8f);
        static readonly WaitForSeconds _waitCatWake = new(0.4f);
        static readonly WaitForSeconds _waitCatReady = new(0.3f);

        const float MIN_ACTION_DURATION = 3f;

        void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        void Start()
        {
            TurnManager.OnCombatResult += OnCombatResult;
            TurnManager.OnMultiCombatResult += OnMultiCombatResult;
            TurnManager.OnMultiDeathPresentation += OnMultiDeathPresentation;
        }

        void OnDestroy()
        {
            RestorePresentationCamera();
            TurnManager.OnCombatResult -= OnCombatResult;
            TurnManager.OnMultiCombatResult -= OnMultiCombatResult;
            TurnManager.OnMultiDeathPresentation -= OnMultiDeathPresentation;
            _pendingMultiPresentations.Clear();
            if (!_sequenceCompleted)
            {
                try { InventoryPresenter.Instance?.UnlockRebuild(); }
                catch (System.Exception e) { Debug.LogException(e); }
                try { OnTempOverridesClear?.Invoke(); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
            if (_hugMovedTransform != null)
            {
                _hugMovedTransform.position = _hugSavedPos;
                _hugMovedTransform = null;
            }
            foreach (var pool in _particlePools.Values)
                pool.Dispose();
            _particlePools.Clear();
            if (Instance == this) Instance = null;
        }

        void OnCombatResult(CombatResultData result)
        {
            Debug.Log($"[CombatVFX] OnCombatResult received — winner={result.WinnerIndex}, firstIdx={result.FirstPlayerIndex}, P1Main={result.P1MainItemId}, P2Main={result.P2MainItemId}");
            StartCoroutine(PlayCombatVFXSequence(result));
        }

        void OnMultiCombatResult(CombatResolutionBatchNetData batch)
        {
            Debug.Log($"[CombatVFX] OnMultiCombatResult received — seats={batch.SeatCount}, events={batch.EventCount}, seq={batch.ResultSequence}");
            EnqueueMultiPresentation(new PendingMultiPresentation
            {
                IsCombat = true,
                Batch = batch,
                Sequence = batch.ResultSequence
            });
        }

        void OnMultiDeathPresentation(byte deathMask, bool endsRound, uint presentationId)
        {
            Debug.Log($"[CombatVFX] OnMultiDeathPresentation — deathMask={deathMask:X2}, endsRound={endsRound}, seq={presentationId}");
            EnqueueMultiPresentation(new PendingMultiPresentation
            {
                DeathMask = deathMask,
                EndsRound = endsRound,
                Sequence = presentationId
            });
        }

        void EnqueueMultiPresentation(PendingMultiPresentation pending)
        {
            if (!_sequenceCompleted && _activeSequence == pending.Sequence) return;
            if (!_sequenceCompleted && pending.Sequence < _activeSequence)
            {
                Debug.LogWarning($"[CombatVFX] Stale presentation ignored: seq={pending.Sequence}, active={_activeSequence}");
                return;
            }
            if (pending.Sequence <= _lastCompletedMultiSequence)
            {
                SendPresentationAck(pending.Sequence);
                return;
            }
            if (_pendingMultiPresentations.Count >= 8)
            {
                Debug.LogError($"[CombatVFX] Presentation queue overflow at seq={pending.Sequence}; server timeout reconciliation required");
                return;
            }
            foreach (var queued in _pendingMultiPresentations)
                if (queued.Sequence == pending.Sequence) return;
            _pendingMultiPresentations.Enqueue(pending);
            if (_multiPresentationQueueRoutine == null)
                _multiPresentationQueueRoutine = StartCoroutine(DrainMultiPresentationQueue());
        }

        IEnumerator DrainMultiPresentationQueue()
        {
            while (_pendingMultiPresentations.Count > 0)
            {
                var pending = _pendingMultiPresentations.Dequeue();
                _activeSequence = pending.Sequence;
                _sequenceCompleted = false;
                if (pending.IsCombat)
                    yield return StartCoroutine(PlayMultiCombatVFXSequence(pending.Batch));
                else
                    yield return StartCoroutine(PlayMultiDeathSequence(
                        pending.DeathMask, pending.EndsRound, pending.Sequence));
                _lastCompletedMultiSequence = pending.Sequence;
            }
            _multiPresentationQueueRoutine = null;
        }

        static void SendPresentationAck(uint sequence)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsConnectedClient) return;
            var localPlayer = nm.LocalClient?.PlayerObject?.GetComponent<PlayerState>();
            localPlayer?.PresentationAckServerRpc(sequence);
        }

        IEnumerator PlayMultiDeathSequence(byte deathMask, bool endsRound, uint presentationId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                CompletePresentationSequence(presentationId);
                yield break;
            }

            var runningAnims = new List<Coroutine>();
            for (byte seat = 0; seat < 8; seat++)
            {
                if ((deathMask & (1 << seat)) == 0) continue;
                var visual = GetPlayerVisual(seat, nm);
                if (visual != null)
                    runningAnims.Add(visual.PlayDeathSequenceAndWait(endsRound));
            }

            foreach (var anim in runningAnims)
                yield return anim;

            CompletePresentationSequence(presentationId);
        }

        IEnumerator PlayCombatVFXSequence(CombatResultData result)
        {
            _activeSequence = result.ResultSequence;
            _sequenceCompleted = false;
            IsPlaying = true;

            try { InventoryPresenter.Instance?.LockRebuild(); }
            catch (System.Exception e) { Debug.LogException(e); }

            try { OnTempTargetsOverride?.Invoke(result.P1TempBeforeCombat, result.P2TempBeforeCombat); }
            catch (System.Exception e) { Debug.LogException(e); }

            try
            {
                var nm = NetworkManager.Singleton;
                if (nm == null) yield break;

                int firstIdx = result.FirstPlayerIndex;
                int secondIdx = 1 - firstIdx;

                short firstItemId = firstIdx == 0 ? result.P1MainItemId : result.P2MainItemId;
                short secondItemId = secondIdx == 0 ? result.P1MainItemId : result.P2MainItemId;

                // Defense is presented by the incoming attack, never as a separate turn.
                if (ItemManager.Instance?.GetItemData(firstItemId)?.Category == ItemCategory.Defense)
                    firstItemId = -1;
                if (ItemManager.Instance?.GetItemData(secondItemId)?.Category == ItemCategory.Defense)
                    secondItemId = -1;

                int deadIdx = result.WinnerIndex >= 0 ? 1 - result.WinnerIndex : -1;
                bool firstActionKilled = result.WinnerIndex >= 0
                    && result.EventCount == 1
                    && result.Event0Source == (byte)firstIdx;

                LogAttackTimingSummary(firstIdx, firstItemId, secondIdx, secondItemId, deadIdx);

                float seqStart = Time.time;
                yield return _waitIntro;

                Debug.Log($"[CombatVFX] Sequence seq={_activeSequence}: first=P{firstIdx}(item={firstItemId}), second=P{secondIdx}(item={secondItemId}), deadIdx={deadIdx}");

                if (firstItemId >= 0)
                {
                    GetImpactData(result, firstIdx, out byte impactFlags, out short defenseItemId);
                    Debug.Log($"[CombatVFX] Playing FIRST item sequence: P{firstIdx} item={firstItemId}, flags={impactFlags}");
                    OnAttackerChanged?.Invoke(firstIdx);
                    yield return StartCoroutine(PlayItemSequence(firstIdx, firstItemId, nm,
                        impactFlags, defenseItemId, result));
                }

                if (firstActionKilled && deadIdx >= 0)
                {
                    Debug.Log($"[CombatVFX] First action killed P{deadIdx} — playing death sequence");
                    var deadVisual = GetPlayerVisual(deadIdx, nm);
                    if (deadVisual != null)
                        yield return deadVisual.PlayDeathSequenceAndWait(result.EndsMatch);
                    yield break;
                }

                if (firstItemId >= 0 && secondItemId >= 0)
                    yield return _waitBriefPause;

                if (secondItemId >= 0)
                {
                    GetImpactData(result, secondIdx, out byte impactFlags, out short defenseItemId);
                    Debug.Log($"[CombatVFX] Playing SECOND item sequence: P{secondIdx} item={secondItemId}, flags={impactFlags}");
                    OnAttackerChanged?.Invoke(secondIdx);
                    yield return StartCoroutine(PlayItemSequence(secondIdx, secondItemId, nm,
                        impactFlags, defenseItemId, result));
                }

                if (!firstActionKilled && deadIdx >= 0)
                {
                    Debug.Log($"[CombatVFX] Second action killed P{deadIdx} — playing death sequence");
                    var deadVisual = GetPlayerVisual(deadIdx, nm);
                    if (deadVisual != null)
                        yield return deadVisual.PlayDeathSequenceAndWait(result.EndsMatch);
                }
                else
                {
                    float elapsed = Time.time - seqStart;
                    float pad = MIN_ACTION_DURATION - elapsed;
                    if (pad > 0f)
                    {
                        Debug.Log($"[CombatVFX] Padding {pad:F2}s to meet {MIN_ACTION_DURATION}s minimum");
                        yield return new WaitForSeconds(pad);
                    }
                }
            }
            finally
            {
                CompletePresentationSequence(result.ResultSequence);
            }
        }

        IEnumerator PlayMultiCombatVFXSequence(CombatResolutionBatchNetData batch)
        {
            _activeSequence = batch.ResultSequence;
            _sequenceCompleted = false;
            IsPlaying = true;

            try { InventoryPresenter.Instance?.LockRebuild(); }
            catch (System.Exception e) { Debug.LogException(e); }

            try
            {
                for (int s = 0; s < batch.SeatCount; s++)
                    OnPlayerTempOverride?.Invoke(s, batch.TempBefore[s]);

                var nm = NetworkManager.Singleton;
                if (nm == null) yield break;

                var deathShown = new HashSet<byte>();

                yield return _waitIntro;

                for (int orderIdx = 0; orderIdx < batch.SeatCount; orderIdx++)
                {
                    int actorSeat = batch.ActionOrder[orderIdx];
                    short mainItemId = batch.MainItemIds[actorSeat];

                    var mainEffects = new List<CombatEventNetData>();
                    var deathEvents = new List<CombatEventNetData>();
                    for (int e = 0; e < batch.EventCount; e++)
                    {
                        var evt = batch.Events[e];
                        if (evt.ActorSeat != actorSeat) continue;
                        if ((CombatEventType)evt.EventType == CombatEventType.Death)
                            deathEvents.Add(evt);
                        else if ((CombatEventType)evt.EventType == CombatEventType.MainEffect
                            || (CombatEventType)evt.EventType == CombatEventType.DefenseActivated)
                            mainEffects.Add(evt);
                    }

                    bool actionExecuted = mainEffects.Count > 0;
                    if (!actionExecuted) continue;

                    OnAttackerChanged?.Invoke(actorSeat);
                    Debug.Log($"[CombatVFX-Multi] Action {orderIdx}: P{actorSeat} item={mainItemId} effects={mainEffects.Count} deaths={deathEvents.Count}");

                    float actionStart = Time.time;
                    yield return StartCoroutine(PlayMultiItemSequence(
                        actorSeat, mainItemId, nm, mainEffects));

                    float actionPad = MIN_ACTION_DURATION - (Time.time - actionStart);
                    if (actionPad > 0f)
                        yield return new WaitForSeconds(actionPad);

                    foreach (var dEvt in deathEvents)
                    {
                        byte tgt = dEvt.TargetSeat;
                        if (deathShown.Contains(tgt)) continue;
                        var deadVisual = GetPlayerVisual(tgt, nm);
                        if (deadVisual == null) continue;
                        deathShown.Add(tgt);
                        bool endsMatch = batch.MatchWinnerMask != 0;
                        yield return deadVisual.PlayDeathSequenceAndWait(endsMatch);
                    }

                    bool anyDeath = deathEvents.Count > 0;
                    if (orderIdx < batch.SeatCount - 1 && !anyDeath)
                        yield return _waitBriefPause;
                }

            }
            finally
            {
                for (int s = 0; s < batch.SeatCount; s++)
                {
                    try { OnPlayerTempOverride?.Invoke(s, batch.TempAfter[s]); }
                    catch (System.Exception e) { Debug.LogException(e); }
                }

                CompletePresentationSequence(batch.ResultSequence);
            }
        }

        IEnumerator PlayMultiItemSequence(int actorSeat, short itemId, NetworkManager nm,
            List<CombatEventNetData> mainEffects)
        {
            var itemData = itemId >= 0 ? ItemManager.Instance?.GetItemData(itemId) : null;
            var actorVisual = GetPlayerVisual(actorSeat, nm);

            if (itemData == null || actorVisual == null) yield break;

            bool isLocalUser = actorVisual.IsOwner;
            bool isAttack = itemData.Category == ItemCategory.Attack;
            bool isRecovery = itemData.Category == ItemCategory.Recovery;
            int targetSeat = mainEffects.Count > 0 ? mainEffects[0].TargetSeat : actorSeat;
            var targetVisual = GetPlayerVisual(targetSeat, nm);

            if (isLocalUser && itemData.Category == ItemCategory.Buff)
                ScreenVFXManager.Instance.PlayRecoveryVFX();

            string userTrigger = isLocalUser
                ? itemData.AnimTrigger
                : (!string.IsNullOrEmpty(itemData.OpponentAnimTrigger) ? itemData.OpponentAnimTrigger : itemData.AnimTrigger);

            bool fullyBlocked = mainEffects.Count > 0;
            foreach (var evt in mainEffects)
                fullyBlocked &= IsFullyBlockedImpact(evt.Flags);
            System.Action impact = () =>
            {
                ApplyMultiEventTemps(mainEffects);
                PlayMultiImpactReactions(actorSeat, isAttack, isRecovery,
                    isLocalUser, 0, mainEffects, nm);
            };

            if (itemData.ItemName == "Hug T-shirt")
            {
                GameAudioManager.Instance?.PlayItemSfx(itemData.AnimTrigger, itemData.ItemName);
                yield return StartCoroutine(PlayHugSequence(actorSeat, targetSeat,
                    actorVisual, targetVisual, isLocalUser, !isLocalUser, impact, !fullyBlocked));
                actorVisual.ReturnToIdle();
                targetVisual?.ReturnToIdle();
                FPSVisualController.Instance?.ReturnToIdle();
                yield break;
            }

            bool isFeed = itemData.ItemName == "Samgyetang"
                || itemData.ItemName == "Ice Cream" || itemData.ItemName == "Iced Americano";
            if (isFeed && !fullyBlocked && targetVisual != null)
            {
                if (!isLocalUser) actorVisual.PlayCombatAnimation(userTrigger);
                if (isLocalUser)
                    FPSVisualController.Instance?.PlayFPSAnimation(itemData.AnimTrigger, itemData.ItemName);
                GameAudioManager.Instance?.PlayItemSfx(itemData.AnimTrigger, itemData.ItemName);
                yield return StartCoroutine(PlayFeedReaction(targetVisual, targetSeat, itemData.ItemName, impact));
                actorVisual.ReturnToIdle();
                FPSVisualController.Instance?.ReturnToIdle();
                yield break;
            }

            if (string.IsNullOrEmpty(userTrigger))
            {
                if (itemData.ItemName == "Cat")
                {
                    float catMinDur = itemData.AnimDuration > 0f
                        ? itemData.AnimDuration
                        : MIN_ACTION_DURATION;
                    yield return StartCoroutine(PlayCatSpriteSequence(
                        actorSeat, targetSeat, isLocalUser, catMinDur, impact));
                    yield break;
                }
                ApplyMultiEventTemps(mainEffects);
                int fallbackHits = Mathf.Max(1, itemData.EffectHitCount);
                for (int h = 0; h < fallbackHits; h++)
                {
                    PlayMultiImpactReactions(actorSeat, isAttack, isRecovery,
                        isLocalUser, h, mainEffects, nm);
                    if (h < fallbackHits - 1 && itemData.EffectInterval > 0f)
                        yield return new WaitForSeconds(itemData.EffectInterval);
                }
                yield break;
            }

            if (!isLocalUser)
            {
                var itemSprite = GameSprites.GetItemSprite(itemData.ItemName);
                actorVisual.SetItemSprite(itemSprite);
            }

            actorVisual.PlayCombatAnimation(userTrigger);
            GameAudioManager.Instance?.PlayItemSfx(itemData.AnimTrigger, itemData.ItemName);

            if (isLocalUser)
            {
                var fps = FPSVisualController.Instance;
                if (fps != null) fps.PlayFPSAnimation(itemData.AnimTrigger, itemData.ItemName);
            }

            float animLen = GetAnimDuration(itemData, actorVisual.GetAnimator(), userTrigger);

            if (itemData.EffectHitCount > 0 && itemData.EffectDelay > 0f)
            {
                yield return new WaitForSeconds(itemData.EffectDelay);
                ApplyMultiEventTemps(mainEffects);
                float remaining = animLen - itemData.EffectDelay;

                for (int h = 0; h < itemData.EffectHitCount; h++)
                {
                    PlayMultiImpactReactions(actorSeat, isAttack, isRecovery,
                        isLocalUser, h, mainEffects, nm);

                    if (h < itemData.EffectHitCount - 1 && itemData.EffectInterval > 0f)
                    {
                        yield return new WaitForSeconds(itemData.EffectInterval);
                        remaining -= itemData.EffectInterval;
                    }
                }

                if (remaining > 0f)
                    yield return new WaitForSeconds(remaining);
            }
            else
            {
                float firstHalf = animLen * 0.5f;
                if (firstHalf > 0f)
                    yield return new WaitForSeconds(firstHalf);
                ApplyMultiEventTemps(mainEffects);
                PlayMultiImpactReactions(actorSeat, isAttack, isRecovery,
                    isLocalUser, 0, mainEffects, nm);
                float secondHalf = animLen - firstHalf;
                if (secondHalf > 0f)
                    yield return new WaitForSeconds(secondHalf);
            }

            actorVisual.ReturnToIdle();
            if (isLocalUser)
            {
                var fps = FPSVisualController.Instance;
                if (fps != null) fps.ReturnToIdle();
            }

            foreach (var evt in mainEffects)
            {
                if ((evt.Flags & CombatImpactFlags.Defense) == 0) continue;
                var defendedVisual = GetPlayerVisual(evt.TargetSeat, nm);
                defendedVisual?.ReturnToIdle();
                if (defendedVisual != null && defendedVisual.IsOwner)
                    FPSVisualController.Instance?.ReturnToIdle();
            }

            string itemName = itemData.ItemName;
            if (itemName == "Red Card" && targetVisual != null)
            {
                targetVisual.PlayCombatAnimation("disappoint");
                yield return _waitDamageReact;
                targetVisual.ReturnToIdle();
            }
        }

        void ApplyMultiEventTemps(List<CombatEventNetData> events)
        {
            foreach (var evt in events)
            {
                OnPlayerTempOverride?.Invoke(evt.ActorSeat, evt.ActorResultTemp);
                OnPlayerTempOverride?.Invoke(evt.TargetSeat, evt.TargetResultTemp);
            }
        }

        void PlayMultiImpactReactions(int actorSeat, bool isAttack, bool isRecovery,
            bool isLocalUser, int hitIndex, List<CombatEventNetData> events,
            NetworkManager nm)
        {
            foreach (var evt in events)
            {
                int targetSeat = evt.TargetSeat;
                if (targetSeat == actorSeat) continue;
                var targetVisual = GetPlayerVisual(targetSeat, nm);
                if (targetVisual == null) continue;

                bool targetDefending = (evt.Flags & CombatImpactFlags.Defense) != 0;
                bool targetDamaged = (evt.Flags & CombatImpactFlags.Damage) != 0;
                if (!isAttack && !targetDefending && !targetDamaged) continue;

                if (targetDefending && hitIndex == 0)
                    PlayDefenseReaction(targetVisual, evt.DefenseItemId);
                if (!targetDamaged) continue;

                targetVisual.PlayDamageFlash(preserveCombatAnimation: targetDefending);
                if (!isLocalUser)
                {
                    PlayHitAt(GetPlayerWorldPos(targetSeat));
                    if (hitIndex == 0)
                    {
                        if (targetVisual.IsOwner)
                            CameraShake.Instance?.Shake(0.15f, 0.1f);
                        PlayIceBreakAt(GetPlayerWorldPos(targetSeat));
                    }
                }
                GameAudioManager.Instance?.PlayDamaged();
            }

            if (isRecovery && isLocalUser)
            {
                PlayHitAt(GetPlayerWorldPos(actorSeat));
                if (hitIndex == 0) ScreenVFXManager.Instance.PlayRecoveryVFX();
            }
        }

        void PlayDefenseReaction(AZPlayerVisual targetVisual, short defenseItemId)
        {
            if (targetVisual == null) return;

            var defenseItem = defenseItemId >= 0
                ? ItemManager.Instance?.GetItemData(defenseItemId)
                : null;
            string trigger = targetVisual.IsOwner
                ? defenseItem?.AnimTrigger
                : (!string.IsNullOrEmpty(defenseItem?.OpponentAnimTrigger)
                    ? defenseItem.OpponentAnimTrigger
                    : defenseItem?.AnimTrigger);
            if (string.IsNullOrEmpty(trigger)) trigger = "defence";

            if (!targetVisual.IsOwner)
                targetVisual.PrepareDefenseSprite(!string.IsNullOrEmpty(defenseItem?.ItemName)
                    ? GameSprites.GetItemSprite(defenseItem.ItemName) : null);
            targetVisual.PlayCombatAnimation(trigger);
            GameAudioManager.Instance?.PlayItemSfx(trigger, defenseItem?.ItemName);
            if (targetVisual.IsOwner)
                FPSVisualController.Instance?.PlayFPSAnimation(trigger, defenseItem?.ItemName);
        }

        public static bool IsFullyBlockedImpact(byte flags)
            => (flags & CombatImpactFlags.Defense) != 0
                && (flags & (CombatImpactFlags.Damage | CombatImpactFlags.Recovery)) == 0;

        static void GetImpactData(CombatResultData result, int actorSeat,
            out byte flags, out short defenseItemId)
        {
            flags = 0;
            defenseItemId = -1;
            if (result.EventCount > 0 && result.Event0Source == actorSeat)
            {
                flags = result.Event0ImpactFlags;
                defenseItemId = result.Event0DefenseItemId;
            }
            else if (result.EventCount > 1 && result.Event1Source == actorSeat)
            {
                flags = result.Event1ImpactFlags;
                defenseItemId = result.Event1DefenseItemId;
            }
        }

        IEnumerator PlayItemSequence(int userIdx, short itemId, NetworkManager nm,
            byte impactFlags, short defenseItemId, CombatResultData result)
        {
            var itemData = ItemManager.Instance?.GetItemData(itemId);
            if (itemData == null)
            {
                Debug.LogWarning($"[CombatVFX] PlayItemSequence: itemData NULL for itemId={itemId}");
                yield break;
            }

            int targetIdx = 1 - userIdx;
            var userVisual = GetPlayerVisual(userIdx, nm);
            var targetVisual = GetPlayerVisual(targetIdx, nm);

            bool isAttack = itemData.Category == ItemCategory.Attack;
            bool isRecovery = itemData.Category == ItemCategory.Recovery;
            bool isLocalUser = userVisual != null && userVisual.IsOwner;
            bool targetDefending = (impactFlags & CombatImpactFlags.Defense) != 0;
            bool targetDamaged = (impactFlags & CombatImpactFlags.Damage) != 0;
            bool fullyBlocked = IsFullyBlockedImpact(impactFlags);

            if (isLocalUser && itemData.Category == ItemCategory.Buff)
                ScreenVFXManager.Instance.PlayRecoveryVFX();
            else if (!isLocalUser && itemData.Category == ItemCategory.Debuff && targetDamaged)
                ScreenVFXManager.Instance.PlayHitVFX();

            string userTrigger = isLocalUser
                ? itemData.AnimTrigger
                : (!string.IsNullOrEmpty(itemData.OpponentAnimTrigger) ? itemData.OpponentAnimTrigger : itemData.AnimTrigger);
            bool hasUserAnim = userVisual != null && !string.IsNullOrEmpty(userTrigger);

            Debug.Log($"[CombatVFX] PlayItemSequence: P{userIdx} '{itemData.ItemName}' trigger='{userTrigger}' (1P='{itemData.AnimTrigger}' 3P='{itemData.OpponentAnimTrigger}') isLocal={isLocalUser} cat={itemData.Category}");

            if (hasUserAnim)
            {
                if (!isLocalUser && userVisual != null)
                {
                    var itemSprite = GameSprites.GetItemSprite(itemData.ItemName);
                    userVisual.SetItemSprite(itemSprite);
                    Debug.Log($"[CombatVFX] → 3P SetItemSprite('{itemData.ItemName}') sprite={itemSprite != null}");
                }

                Debug.Log($"[CombatVFX] → userVisual.PlayCombatAnimation('{userTrigger}')");
                userVisual.PlayCombatAnimation(userTrigger);

                bool isBuldak = itemData.ItemName == "Buldak Noodles";
                if (!isBuldak)
                    GameAudioManager.Instance?.PlayItemSfx(itemData.AnimTrigger, itemData.ItemName);
                else
                    StartCoroutine(PlayBuldakSfx(isLocalUser));

                if (isLocalUser)
                {
                    var fps = FPSVisualController.Instance;
                    Debug.Log($"[CombatVFX] → FPS isLocalUser=true, FPSInstance={fps != null}");
                    if (fps != null) fps.PlayFPSAnimation(itemData.AnimTrigger, itemData.ItemName);
                }

                float animLen = GetAnimDuration(itemData, userVisual.GetAnimator(), userTrigger);

                if (itemData.EffectHitCount > 0 && itemData.EffectDelay > 0f)
                {
                    yield return new WaitForSeconds(itemData.EffectDelay);
                    ApplyEventTemps(userIdx, result);
                    float remaining = animLen - itemData.EffectDelay;

                    for (int h = 0; h < itemData.EffectHitCount; h++)
                    {
                        if ((isAttack || targetDefending) && targetVisual != null)
                        {
                            if (targetDefending && h == 0)
                            {
                                PlayDefenseReaction(targetVisual, defenseItemId);
                            }
                            if (targetDamaged)
                            {
                                targetVisual.PlayDamageFlash(preserveCombatAnimation: targetDefending);
                                if (!isLocalUser)
                                {
                                    PlayHitAt(GetPlayerWorldPos(targetIdx));
                                    if (h == 0)
                                    {
                                        ScreenVFXManager.Instance.PlayHitVFX();
                                        CameraShake.Instance?.Shake(0.15f, 0.1f);
                                        PlayIceBreakAt(GetPlayerWorldPos(targetIdx));
                                    }
                                }
                                GameAudioManager.Instance?.PlayDamaged();
                            }
                        }
                        else if (isRecovery && userVisual != null)
                        {
                            if (isLocalUser)
                            {
                                PlayHitAt(GetPlayerWorldPos(userIdx));
                                if (h == 0) ScreenVFXManager.Instance.PlayRecoveryVFX();
                            }
                        }

                        if (h < itemData.EffectHitCount - 1 && itemData.EffectInterval > 0f)
                        {
                            yield return new WaitForSeconds(itemData.EffectInterval);
                            remaining -= itemData.EffectInterval;
                        }
                    }

                    if (remaining > 0f)
                        yield return new WaitForSeconds(remaining);
                }
                else
                {
                    yield return new WaitForSeconds(animLen * 0.5f);
                    ApplyEventTemps(userIdx, result);
                    if ((isAttack || targetDefending) && targetVisual != null)
                    {
                        if (targetDefending)
                            PlayDefenseReaction(targetVisual, defenseItemId);
                        if (targetDamaged)
                        {
                            targetVisual.PlayDamageFlash(preserveCombatAnimation: targetDefending);
                            if (!isLocalUser)
                            {
                                PlayHitAt(GetPlayerWorldPos(targetIdx));
                                ScreenVFXManager.Instance.PlayHitVFX();
                                CameraShake.Instance?.Shake(0.15f, 0.1f);
                                PlayIceBreakAt(GetPlayerWorldPos(targetIdx));
                            }
                            GameAudioManager.Instance?.PlayDamaged();
                        }
                    }
                    yield return new WaitForSeconds(animLen * 0.5f);
                }

                Debug.Log($"[CombatVFX] → userVisual.ReturnToIdle()");
                userVisual.ReturnToIdle();
                if (isLocalUser)
                {
                    var fps = FPSVisualController.Instance;
                    if (fps != null) fps.ReturnToIdle();
                }

                if (targetDefending && targetVisual != null)
                {
                    targetVisual.ReturnToIdle();
                    if (targetVisual.IsOwner)
                        FPSVisualController.Instance?.ReturnToIdle();
                }

                if (itemData.ItemName == "Screwdriver" && targetVisual != null)
                    TintTargetFanBlue(targetVisual);
            }
            else if (itemData.ItemName == "Cat")
            {
                ApplyEventTemps(userIdx, result);
                float catMinDur = itemData.AnimDuration > 0f ? itemData.AnimDuration : 1.5f;
                yield return StartCoroutine(PlayCatSpriteSequence(userIdx, targetIdx, isLocalUser, catMinDur));
            }
            else if ((isAttack || isRecovery || targetDefending) &&
                (itemData.EffectHitCount > 0 || targetDefending))
            {
                ApplyEventTemps(userIdx, result);
                if ((isAttack || targetDefending) && targetVisual != null)
                {
                    if (targetDefending)
                    {
                        PlayDefenseReaction(targetVisual, defenseItemId);
                    }
                    if (targetDamaged)
                    {
                        targetVisual.PlayDamageFlash(preserveCombatAnimation: targetDefending);
                        if (!isLocalUser)
                        {
                            PlayHitAt(GetPlayerWorldPos(targetIdx));
                            ScreenVFXManager.Instance.PlayHitVFX();
                            CameraShake.Instance?.Shake(0.15f, 0.1f);
                            PlayIceBreakAt(GetPlayerWorldPos(targetIdx));
                        }
                        GameAudioManager.Instance?.PlayDamaged();
                    }
                }
                else if (isRecovery)
                {
                    if (isLocalUser)
                    {
                        PlayHitAt(GetPlayerWorldPos(userIdx));
                        ScreenVFXManager.Instance.PlayRecoveryVFX();
                    }
                }
                yield return _waitDamageReact;
                if (targetDefending && targetVisual != null)
                {
                    targetVisual.ReturnToIdle();
                    if (targetVisual.IsOwner)
                        FPSVisualController.Instance?.ReturnToIdle();
                }
            }

            bool isTargetOpponent = isLocalUser;
            string itemName = itemData.ItemName;

            if ((itemName == "Samgyetang" || itemName == "Ice Cream" || itemName == "Iced Americano")
                && !fullyBlocked && targetVisual != null && !isTargetOpponent)
            {
                yield return StartCoroutine(PlayFeedReaction(targetVisual, targetIdx, itemName));
            }
            else if (itemName == "Red Card" && targetVisual != null && !isTargetOpponent)
            {
                targetVisual.PlayCombatAnimation("disappoint");
                yield return _waitDamageReact;
                targetVisual.ReturnToIdle();
            }
            else if (itemName == "Hug T-shirt")
            {
                yield return StartCoroutine(PlayHugSequence(userIdx, targetIdx, userVisual, targetVisual, isLocalUser, isTargetOpponent));
            }

            Debug.Log($"[CombatVFX] PlayItemSequence DONE: P{userIdx} '{itemData.ItemName}'");
        }

        void ApplyEventTemps(int userIdx, CombatResultData result)
        {
            if (result.EventCount > 0 && result.Event0Source == (byte)userIdx)
            {
                OnPlayerTempOverride?.Invoke(result.Event0Source, result.Event0UserTemp);
                OnPlayerTempOverride?.Invoke(result.Event0Target, result.Event0TargetTemp);
                return;
            }
            if (result.EventCount > 1 && result.Event1Source == (byte)userIdx)
            {
                OnPlayerTempOverride?.Invoke(result.Event1Source, result.Event1UserTemp);
                OnPlayerTempOverride?.Invoke(result.Event1Target, result.Event1TargetTemp);
            }
        }

        void LogAttackTimingSummary(int firstIdx, short firstItemId, int secondIdx, short secondItemId, int deadIdx)
        {
            Debug.Log("╔══════════════════════════════════════════════════════════════");
            Debug.Log("║ ATTACK PHASE — TIMING SUMMARY");
            Debug.Log("╠══════════════════════════════════════════════════════════════");

            LogItemTiming("FIRST", firstIdx, firstItemId);
            LogItemTiming("SECOND", secondIdx, secondItemId);

            if (deadIdx >= 0)
                Debug.Log($"║ DEATH: P{deadIdx} → freeze(0.33s) + hold(1.5s) + break = ~1.83s");

            Debug.Log("╚══════════════════════════════════════════════════════════════");
        }

        void LogItemTiming(string order, int playerIdx, short itemId)
        {
            if (itemId < 0)
            {
                Debug.Log($"║ {order}: P{playerIdx} — NO ITEM");
                return;
            }

            var itemData = ItemManager.Instance?.GetItemData(itemId);
            if (itemData == null)
            {
                Debug.Log($"║ {order}: P{playerIdx} — itemId={itemId} DATA NOT FOUND");
                return;
            }

            float animDur = itemData.AnimDuration > 0f ? itemData.AnimDuration : 0.8f;
            float totalHitTime = itemData.EffectDelay + (itemData.EffectHitCount - 1) * itemData.EffectInterval;
            float oppDur = !string.IsNullOrEmpty(itemData.OpponentAnimTrigger)
                ? (itemData.AnimDuration > 0f ? itemData.AnimDuration : 0.8f)
                : 0f;

            Debug.Log($"║ {order}: P{playerIdx} '{itemData.ItemName}' (id={itemId})");
            Debug.Log($"║   AnimTrigger='{itemData.AnimTrigger}' | OppTrigger='{itemData.OpponentAnimTrigger}'");
            Debug.Log($"║   AnimDuration={animDur:F2}s | EffectDelay={itemData.EffectDelay:F2}s");
            Debug.Log($"║   HitCount={itemData.EffectHitCount} | HitInterval={itemData.EffectInterval:F2}s | TotalHitTime={totalHitTime:F2}s");
            Debug.Log($"║   OppAnimDuration={oppDur:F2}s | EstTotal={animDur + oppDur:F2}s");
        }

        float GetAnimDuration(ItemDataSO itemData, Animator animator, string trigger = null)
        {
            if (itemData.AnimDuration > 0f) return itemData.AnimDuration;
            if (animator == null) return 0.8f;
            animator.Update(0f);
            var info = animator.GetCurrentAnimatorStateInfo(0);
            return info.length > 0f ? info.length : 0.8f;
        }

        IEnumerator PlayCatSpriteSequence(int userIdx, int targetIdx, bool isLocalUser, float minDuration = 1.5f,
            System.Action onImpact = null)
        {
            float startTime = Time.time;
            Debug.Log($"[CombatVFX] Cat sequence START — user=P{userIdx} target=P{targetIdx} isLocal={isLocalUser} minDur={minDuration}s");

            var nm = NetworkManager.Singleton;
            var userVisual = nm != null ? GetPlayerVisual(userIdx, nm) : null;

            GameAudioManager.Instance?.PlayItemSfx("", "Cat");

            var spSleep = Resources.Load<Sprite>("Cat/cat_sleep");
            var spWakeup = Resources.Load<Sprite>("Cat/cat_wakeup");
            var spJump = Resources.Load<Sprite>(isLocalUser ? "Cat/cat_jump" : "Cat/cat_jump2");
            var spRummage = Resources.Load<Sprite>("Cat/cat_rummage");

            if (spSleep == null)
            {
                Debug.LogWarning("[CombatVFX] Cat sprites not found — waiting minDuration");
                yield return new WaitForSeconds(minDuration);
                onImpact?.Invoke();
                yield break;
            }

            Transform catItemTransform = isLocalUser && onImpact == null ? FindCatItemView() : null;
            SpriteRenderer sr;
            GameObject go;
            Vector3 originalScale;
            Material originalMat = null;

            if (catItemTransform != null)
            {
                go = catItemTransform.gameObject;
                var cardChild = catItemTransform.Find("Card");
                sr = cardChild != null ? cardChild.GetComponent<SpriteRenderer>() : catItemTransform.GetComponentInChildren<SpriteRenderer>();
                if (sr == null)
                {
                    Debug.LogWarning("[CombatVFX] Cat item SpriteRenderer not found — waiting minDuration");
                    yield return new WaitForSeconds(minDuration);
                    onImpact?.Invoke();
                    yield break;
                }
                originalScale = go.transform.localScale;

                originalMat = sr.material;
                sr.material = new Material(Shader.Find("Sprites/Default"));

                sr.sprite = spSleep;
                sr.sortingOrder = 90;

                var hover = go.GetComponent<HoverEffect>();
                if (hover != null) hover.enabled = false;
                var col = go.GetComponent<Collider>();
                if (col != null) col.enabled = false;
                var label = catItemTransform.Find("Label");
                if (label != null) label.gameObject.SetActive(false);
                var banned = catItemTransform.Find("BannedOverlay");
                if (banned != null) banned.gameObject.SetActive(false);
                var outline = catItemTransform.Find("HoverOutline");
                if (outline == null)
                {
                    var cardOutline = cardChild != null ? cardChild.Find("HoverOutline") : null;
                    if (cardOutline != null) outline = cardOutline;
                }
                if (outline != null) outline.gameObject.SetActive(false);
            }
            else
            {
                go = new GameObject("CatAnim");
                sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = spSleep;
                sr.sortingOrder = 90;
                Vector3 userPos = GetPlayerWorldPos(userIdx);
                go.transform.position = userPos + new Vector3(-0.5f, 0.3f, 0f);
                originalScale = Vector3.one * 0.7f;
                go.transform.localScale = originalScale;
            }

            if (userVisual != null)
                userVisual.PlayCombatAnimation("jump");

            yield return _waitCatReady;

            sr.sprite = spWakeup;
            yield return _waitCatWake;

            sr.sprite = spJump;

            string destMarkerPrefix = isLocalUser ? "EnemyItem" : "PlayerItem";
            int randomIdx = Random.Range(1, 9);
            var destMarker = onImpact == null ? GameObject.Find($"{destMarkerPrefix}{randomIdx}") : null;
            Vector3 destPos = destMarker != null
                ? destMarker.transform.position
                : GetPlayerWorldPos(targetIdx) + new Vector3(0f, 0.3f, 0f);

            Vector3 arcStart = go.transform.position;
            bool flipX = destPos.x < arcStart.x;
            float baseScale = originalScale.x;
            go.transform.localScale = new Vector3(flipX ? -baseScale : baseScale, baseScale, originalScale.z);

            float arcDur = 0.8f;
            float arcHeight = 2.5f;
            float t = 0f;
            while (t < arcDur)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / arcDur);
                Vector3 linear = Vector3.Lerp(arcStart, destPos, p);
                float yOffset = arcHeight * 4f * p * (1f - p);
                go.transform.position = linear + new Vector3(0f, yOffset, 0f);
                yield return null;
            }
            go.transform.position = destPos;
            onImpact?.Invoke();

            if (catItemTransform != null)
            {
                var tempGO = new GameObject("CatAnimTemp");
                var tempSR = tempGO.AddComponent<SpriteRenderer>();
                tempSR.sprite = sr.sprite;
                tempSR.sortingOrder = sr.sortingOrder;
                tempSR.material = sr.material;
                tempGO.transform.position = go.transform.position;
                tempGO.transform.localScale = go.transform.localScale;
                go = tempGO;
                sr = tempSR;
                catItemTransform = null;
            }

            if (onImpact == null) InventoryPresenter.Instance?.UnlockRebuild();

            sr.sprite = spRummage;

            float rumbleDur = minDuration;
            float rumbleRange = 1.2f;
            float rumbleSpeed = 12f;
            t = 0f;
            while (t < rumbleDur)
            {
                t += Time.deltaTime;
                float xOff = Mathf.Sin(t * rumbleSpeed) * rumbleRange;
                go.transform.position = destPos + new Vector3(xOff, 0f, 0f);

                float s = baseScale + Mathf.Sin(t * rumbleSpeed * 2f) * 0.05f;
                float dir = Mathf.Sin(t * rumbleSpeed) >= 0f ? 1f : -1f;
                go.transform.localScale = new Vector3(dir * s, s, originalScale.z);
                yield return null;
            }

            float exitDur = 0.5f;
            Vector3 exitStart = go.transform.position;
            Vector3 exitEnd = exitStart + new Vector3(6f, 3f, 0f);
            t = 0f;
            while (t < exitDur)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / exitDur);
                go.transform.position = Vector3.Lerp(exitStart, exitEnd, p);

                float yArc = Mathf.Sin(p * Mathf.PI) * 1.5f;
                go.transform.position += new Vector3(0f, yArc, 0f);

                sr.color = new Color(1f, 1f, 1f, 1f - p);
                yield return null;
            }

            if (catItemTransform == null)
            {
                Destroy(go);
            }
            else
            {
                if (originalMat != null)
                    sr.material = originalMat;
                go.SetActive(false);
            }

            if (userVisual != null)
                userVisual.ReturnToIdle();

            float elapsed = Time.time - startTime;
            Debug.Log($"[CombatVFX] Cat sequence END — elapsed={elapsed:F2}s");
        }

        Transform FindCatItemView()
        {
            return InventoryPresenter.Instance?.FindLocalViewByName("Cat");
        }

        AZPlayerVisual GetPlayerVisual(int playerIndex, NetworkManager nm)
        {
            var mcr = MatchCompositionRoot.Instance;
            if (mcr != null
                && mcr.Registry.TryGetByPlayerIndex((byte)playerIndex, out var binding)
                && binding.NetworkObject != null)
            {
                return binding.NetworkObject.GetComponent<AZPlayerVisual>();
            }

            foreach (var kvp in nm.SpawnManager.SpawnedObjects)
            {
                var netObj = kvp.Value;
                if (netObj == null || !netObj.IsPlayerObject) continue;
                var ps = netObj.GetComponent<PlayerState>();
                if (ps != null && ps.PlayerIndex == playerIndex)
                    return netObj.GetComponent<AZPlayerVisual>();
            }
            return null;
        }

        public void PlayHitAt(Vector3 pos)
        {
            Debug.Log($"[CombatVFX] PlayHitAt({pos}) — prefab={(_hitEffectPrefab != null)}");
            SpawnParticle(_hitEffectPrefab, pos);
        }

        public void PlayIceBreakAt(Vector3 pos)
        {
            Debug.Log($"[CombatVFX] PlayIceBreakAt({pos}) — prefab={(_iceBreakEffectPrefab != null)}");
            SpawnParticle(_iceBreakEffectPrefab, pos);
        }

        public void PlayFinalBreakAt(Vector3 pos)
        {
            Debug.Log($"[CombatVFX] PlayFinalBreakAt({pos}) — prefab={(_finalBreakEffectPrefab != null)}");
            SpawnParticle(_finalBreakEffectPrefab, pos);
        }

        static readonly WaitForSeconds _waitParticleLife = new(3f);

        void SpawnParticle(GameObject prefab, Vector3 pos)
        {
            if (prefab == null) return;
            var pool = GetOrCreatePool(prefab);
            var go = pool.Get();
            go.transform.position = pos;
            go.transform.rotation = Quaternion.identity;

            var ps = go.GetComponent<ParticleSystem>();
            if (ps != null) ps.Play(true);

            StartCoroutine(ReturnToPoolAfterDelay(prefab, go));
        }

        ObjectPool<GameObject> GetOrCreatePool(GameObject prefab)
        {
            if (_particlePools.TryGetValue(prefab, out var pool))
                return pool;

            var captured = prefab;
            pool = new ObjectPool<GameObject>(
                createFunc: () =>
                {
                    var go = Instantiate(captured);
                    ConfigureParticleRenderers(go);
                    go.SetActive(false);
                    return go;
                },
                actionOnGet: go => go.SetActive(true),
                actionOnRelease: go =>
                {
                    var ps = go.GetComponent<ParticleSystem>();
                    if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    go.SetActive(false);
                },
                actionOnDestroy: go => Destroy(go),
                defaultCapacity: 2,
                maxSize: 6
            );
            _particlePools[prefab] = pool;
            return pool;
        }

        static void ConfigureParticleRenderers(GameObject go)
        {
            foreach (var psr in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                psr.sortingLayerName = "Default";
                psr.sortingOrder = 100;
                if (psr.sharedMaterial != null)
                {
                    psr.material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                    psr.material.SetInt("_ZWrite", 0);
                }
            }
        }

        IEnumerator ReturnToPoolAfterDelay(GameObject prefab, GameObject go)
        {
            yield return _waitParticleLife;
            if (go != null && _particlePools.TryGetValue(prefab, out var pool))
                pool.Release(go);
        }

        Vector3 GetPlayerWorldPos(int playerIndex)
        {
            var mcr = MatchCompositionRoot.Instance;
            if (mcr != null
                && mcr.Registry.TryGetByPlayerIndex((byte)playerIndex, out var binding)
                && binding.NetworkObject != null)
            {
                var visual = binding.NetworkObject.GetComponent<AZPlayerVisual>();
                if (visual != null)
                    return visual.GetVisualPosition();
                return binding.NetworkObject.transform.position;
            }

            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                foreach (var kvp in nm.SpawnManager.SpawnedObjects)
                {
                    var netObj = kvp.Value;
                    if (netObj == null || !netObj.IsPlayerObject) continue;
                    var ps = netObj.GetComponent<PlayerState>();
                    if (ps != null && ps.PlayerIndex == playerIndex)
                    {
                        var v = netObj.GetComponent<AZPlayerVisual>();
                        if (v != null) return v.GetVisualPosition();
                        return netObj.transform.position;
                    }
                }
            }

            var sp = GameObject.Find($"SpawnPoint_{playerIndex + 1}");
            return sp != null ? sp.transform.position + Vector3.up * 1.5f : Vector3.zero;
        }

        IEnumerator PlayFeedReaction(AZPlayerVisual targetVisual, int targetIdx, string itemName,
            System.Action onImpact = null)
        {
            if (onImpact != null && targetVisual.IsOwner)
                FPSVisualController.Instance?.PlayFPSAnimation("feed", itemName);
            else
                targetVisual.PlayCombatAnimation("feed");

            var sprite = GameSprites.GetItemSprite(itemName);
            GameObject feedSpriteGO = null;
            if (sprite != null)
            {
                feedSpriteGO = new GameObject("FeedSprite");
                var sr = feedSpriteGO.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = 90;
                feedSpriteGO.transform.position = GetPlayerWorldPos(targetIdx) + new Vector3(-0.05f, 0.5f, 0f);
                feedSpriteGO.transform.localScale = Vector3.one * 0.8f;
            }

            yield return _waitFeedHalf;
            onImpact?.Invoke();
            yield return _waitFeedHalf;

            if (feedSpriteGO != null) Destroy(feedSpriteGO);
            targetVisual.ReturnToIdle();
            if (onImpact != null && targetVisual.IsOwner)
                FPSVisualController.Instance?.ReturnToIdle();
        }

        IEnumerator PlayHugSequence(int userIdx, int targetIdx,
            AZPlayerVisual userVisual, AZPlayerVisual targetVisual,
            bool isLocalUser, bool isTargetOpponent, System.Action onImpact = null,
            bool contactAllowed = true)
        {
            if (isLocalUser)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    var startPos = cam.transform.position;
                    _movedCamera = cam.transform;
                    _savedCameraPos = startPos;
                    var targetPos = GetPlayerWorldPos(targetIdx);
                    var approachPos = Vector3.Lerp(startPos, targetPos, 0.4f);

                    float t = 0f;
                    while (t < 0.5f)
                    {
                        t += Time.deltaTime;
                        cam.transform.position = Vector3.Lerp(startPos, approachPos, Mathf.SmoothStep(0f, 1f, t / 0.5f));
                        yield return null;
                    }

                    if (onImpact != null && contactAllowed)
                        FPSVisualController.Instance?.PlayFPSAnimation("hug", "Hug T-shirt");
                    onImpact?.Invoke();
                    yield return _waitHug03;

                    t = 0f;
                    while (t < 1f)
                    {
                        t += Time.deltaTime;
                        cam.transform.position = Vector3.Lerp(approachPos, startPos, Mathf.SmoothStep(0f, 1f, t / 1f));
                        yield return null;
                    }
                    cam.transform.position = startPos;
                    _movedCamera = null;
                }
                else onImpact?.Invoke();
            }

            if (isTargetOpponent && userVisual != null)
            {
                var userTf = userVisual.GetVisualRoot() ?? userVisual.transform;
                var userStartPos = userTf.position;
                _hugMovedTransform = userTf;
                _hugSavedPos = userStartPos;
                var targetPos = GetPlayerWorldPos(targetIdx);

                userVisual.PlayCombatAnimation("jump");
                yield return _waitHug03;

                float moveDur = 0.4f;
                float t = 0f;
                while (t < moveDur)
                {
                    t += Time.deltaTime;
                    userTf.position = Vector3.Lerp(userStartPos, targetPos, Mathf.SmoothStep(0f, 1f, t / moveDur));
                    yield return null;
                }

                if (contactAllowed) userVisual.PlayCombatAnimation("hug");
                onImpact?.Invoke();
                yield return _waitHug08;

                t = 0f;
                while (t < 0.5f)
                {
                    t += Time.deltaTime;
                    userTf.position = Vector3.Lerp(targetPos, userStartPos, Mathf.SmoothStep(0f, 1f, t / 0.5f));
                    yield return null;
                }
                userTf.position = userStartPos;
                _hugMovedTransform = null;
                userVisual.ReturnToIdle();
            }
        }

        IEnumerator PlayBuldakSfx(bool isLocalUser)
        {
            yield return _waitBuldak07;
            GameAudioManager.Instance?.PlayItemSfx("eat", "Buldak Noodles");
            if (isLocalUser)
            {
                yield return _waitBuldak02;
                GameAudioManager.Instance?.PlayItemSfx("eat", "Buldak Noodles");
                yield return _waitBuldak02;
                GameAudioManager.Instance?.PlayItemSfx("eat", "Buldak Noodles");
            }
        }

        void TintTargetFanBlue(AZPlayerVisual targetVisual)
        {
            var root = targetVisual.GetVisualRoot();
            if (root == null) return;

            var fan = root.Find("fan");
            if (fan == null) fan = root.Find("Fan");
            if (fan == null)
            {
                Debug.LogWarning("[CombatVFX] TintTargetFanBlue: fan child not found");
                return;
            }

            var sr = fan.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.color = new Color(0.4f, 0.6f, 1f);
                Debug.Log("[CombatVFX] TintTargetFanBlue: fan color set to blue");
            }
        }
    }
}
