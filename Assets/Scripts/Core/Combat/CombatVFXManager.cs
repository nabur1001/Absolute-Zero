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

        uint _activeSequence;
        bool _sequenceCompleted;

        Transform _hugMovedTransform;
        Vector3 _hugSavedPos;

        readonly Dictionary<GameObject, ObjectPool<GameObject>> _particlePools = new();

        void CompletePresentationSequence()
        {
            if (_sequenceCompleted) return;
            _sequenceCompleted = true;

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

            Debug.Log($"[CombatVFX] Presentation complete: seq={_activeSequence}");

            try
            {
                var nm = NetworkManager.Singleton;
                if (nm != null && nm.IsConnectedClient)
                {
                    var localPlayer = nm.LocalClient?.PlayerObject?.GetComponent<PlayerState>();
                    localPlayer?.PresentationAckServerRpc(_activeSequence);
                }
            }
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
        }

        void OnDestroy()
        {
            TurnManager.OnCombatResult -= OnCombatResult;
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

                int deadIdx = result.WinnerIndex >= 0 ? 1 - result.WinnerIndex : -1;
                bool firstActionKilled = result.WinnerIndex >= 0
                    && result.EventCount == 1
                    && result.Event0Source == (byte)firstIdx;

                LogAttackTimingSummary(firstIdx, firstItemId, secondIdx, secondItemId, deadIdx);

                float seqStart = Time.time;
                yield return _waitIntro;

                Debug.Log($"[CombatVFX] Sequence seq={_activeSequence}: first=P{firstIdx}(item={firstItemId}), second=P{secondIdx}(item={secondItemId}), deadIdx={deadIdx}");

                var firstItemData = firstItemId >= 0 ? ItemManager.Instance?.GetItemData(firstItemId) : null;
                var secondItemData = secondItemId >= 0 ? ItemManager.Instance?.GetItemData(secondItemId) : null;
                bool secondIsDefending = secondItemData != null && secondItemData.Category == ItemCategory.Defense;
                bool firstIsDefending = firstItemData != null && firstItemData.Category == ItemCategory.Defense;

                if (firstItemId >= 0)
                {
                    Debug.Log($"[CombatVFX] Playing FIRST item sequence: P{firstIdx} item={firstItemId}, targetDefending={secondIsDefending}");
                    OnAttackerChanged?.Invoke(firstIdx);
                    yield return StartCoroutine(PlayItemSequence(firstIdx, firstItemId, nm, secondIsDefending, result));
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
                    Debug.Log($"[CombatVFX] Playing SECOND item sequence: P{secondIdx} item={secondItemId}, targetDefending={firstIsDefending}");
                    OnAttackerChanged?.Invoke(secondIdx);
                    yield return StartCoroutine(PlayItemSequence(secondIdx, secondItemId, nm, firstIsDefending, result));
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
                CompletePresentationSequence();
            }
        }

        IEnumerator PlayItemSequence(int userIdx, short itemId, NetworkManager nm, bool targetDefending, CombatResultData result)
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

            if (isLocalUser && itemData.Category == ItemCategory.Buff)
                ScreenVFXManager.Instance.PlayRecoveryVFX();
            else if (!isLocalUser && itemData.Category == ItemCategory.Debuff)
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
                        if (isAttack && targetVisual != null)
                        {
                            if (targetDefending)
                            {
                                if (h == 0)
                                    targetVisual.PlayCombatAnimation("defence");
                            }
                            else
                            {
                                targetVisual.PlayDamageFlash();
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
                    targetVisual.ReturnToIdle();

                if (itemData.ItemName == "Screwdriver" && targetVisual != null)
                    TintTargetFanBlue(targetVisual);
            }
            else if (itemData.ItemName == "Cat")
            {
                ApplyEventTemps(userIdx, result);
                float catMinDur = itemData.AnimDuration > 0f ? itemData.AnimDuration : 1.5f;
                yield return StartCoroutine(PlayCatSpriteSequence(userIdx, targetIdx, isLocalUser, catMinDur));
            }
            else if ((isAttack || isRecovery) && itemData.EffectHitCount > 0)
            {
                ApplyEventTemps(userIdx, result);
                if (isAttack && targetVisual != null)
                {
                    if (targetDefending)
                    {
                        targetVisual.PlayCombatAnimation("defence");
                    }
                    else
                    {
                        targetVisual.PlayDamageFlash();
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
                    targetVisual.ReturnToIdle();
            }

            bool isTargetOpponent = isLocalUser;
            string itemName = itemData.ItemName;

            if ((itemName == "Samgyetang" || itemName == "Ice Cream" || itemName == "Iced Americano")
                && targetVisual != null && !isTargetOpponent)
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

        IEnumerator PlayCatSpriteSequence(int userIdx, int targetIdx, bool isLocalUser, float minDuration = 1.5f)
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
                yield break;
            }

            Transform catItemTransform = isLocalUser ? FindCatItemView() : null;
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
            var destMarker = GameObject.Find($"{destMarkerPrefix}{randomIdx}");
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

            InventoryPresenter.Instance?.UnlockRebuild();

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

            var sp = GameObject.Find(playerIndex == 0 ? "SpawnPoint_1" : "SpawnPoint_2");
            return sp != null ? sp.transform.position + Vector3.up * 1.5f : Vector3.zero;
        }

        IEnumerator PlayFeedReaction(AZPlayerVisual targetVisual, int targetIdx, string itemName)
        {
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
            yield return _waitFeedHalf;

            if (feedSpriteGO != null) Destroy(feedSpriteGO);
            targetVisual.ReturnToIdle();
        }

        IEnumerator PlayHugSequence(int userIdx, int targetIdx,
            AZPlayerVisual userVisual, AZPlayerVisual targetVisual,
            bool isLocalUser, bool isTargetOpponent)
        {
            if (isLocalUser)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    var startPos = cam.transform.position;
                    var targetPos = GetPlayerWorldPos(targetIdx);
                    var approachPos = Vector3.Lerp(startPos, targetPos, 0.4f);

                    float t = 0f;
                    while (t < 0.5f)
                    {
                        t += Time.deltaTime;
                        cam.transform.position = Vector3.Lerp(startPos, approachPos, Mathf.SmoothStep(0f, 1f, t / 0.5f));
                        yield return null;
                    }

                    yield return _waitHug03;

                    t = 0f;
                    while (t < 1f)
                    {
                        t += Time.deltaTime;
                        cam.transform.position = Vector3.Lerp(approachPos, startPos, Mathf.SmoothStep(0f, 1f, t / 1f));
                        yield return null;
                    }
                    cam.transform.position = startPos;
                }
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

                userVisual.PlayCombatAnimation("hug");
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
