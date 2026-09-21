using System.Collections;
using System.Collections.Generic;
using AbsoluteZero.Core.Audio;
using AbsoluteZero.Core.Combat;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Cosmetic;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Network;
using Unity.Netcode;
using UnityEngine;

namespace AbsoluteZero.Core.Player
{
    public class AZPlayerVisual : NetworkBehaviour
    {
        static readonly Dictionary<string, string> TriggerFallback = new()
        {
            { "use", "attack" },
            { "gun", "attack" },
            { "tape", "attack" },
            { "fan", "swing" },
            { "mask", "defence" },
            { "feed", "attack" },
            { "disappoint", "damage" },
            { "jump", "attack" },
            { "hug", "attack" },
        };

        Transform _visualRoot;
        Animator _animator;
        PlayerState _playerState;
        SpriteRenderer[] _spriteRenderers;
        Material[] _cachedMaterials;
        Coroutine _flashCoroutine;
        Coroutine _animEndCoroutine;
        Coroutine _deathCoroutine;
        Coroutine _bindCoroutine;
        bool _isDead;
        bool _deathPresentationCompleted;
        bool _pendingGhostTransition;
        bool _isGhost;
        float _ghostAlpha = 1f;
        GameObject _ghostPlaceholder;
        SpriteRenderer _ghostPlaceholderRenderer;
        static Sprite _ghostPlaceholderSprite;
        public bool IsDead => _isDead;
        public bool IsGhost => _isGhost;
        public bool IsGhostTransitionPending => _pendingGhostTransition;
        Vector3 _deathSavedPos;

        SpriteRenderer _freezeRenderer;
        SpriteRenderer _fanRenderer;

        Transform _itemTransform;
        SpriteRenderer _itemRenderer;
        Sprite _freeze1;
        Sprite _freeze2;
        Sprite _freeze3;

        ParticleSystem _iceBreakParticle;
        ParticleSystem _finalBreakParticle;

        CosmeticVisualController _cosmeticController;
        string _pendingCosmeticDto;

        readonly WaitForSeconds _waitFlashEnd = new(0.5f);
        readonly WaitForSeconds _waitAnimEnd = new(0.6f);
        static readonly WaitForSeconds _waitFreezeTick = new(0.167f);
        static readonly WaitForSeconds _waitFreezeHold = new(1.5f);
        static readonly WaitForSeconds _waitBreakHide = new(0.4f);
        static readonly int FlashAmount = Shader.PropertyToID("_FlashAmount");
        static readonly int IsWindHash = Animator.StringToHash("isWind");
        static readonly int DegreeHash = Animator.StringToHash("degree");

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _playerState = GetComponent<PlayerState>();

            if (IsOwner)
            {
                Debug.Log($"[PlayerVisual] OnNetworkSpawn IsOwner — initializing FPS");
                FPSVisualController.EnsureInstance();
                return;
            }

            Debug.Log($"[PlayerVisual] OnNetworkSpawn IsRemote — setting up EnemyPlayer visuals");
            _playerState.CurrentLifeState.OnValueChanged += OnLifeStateChanged;
            _bindCoroutine = StartCoroutine(DeferredBindRoutine());
        }

        public override void OnNetworkDespawn()
        {
            if (_bindCoroutine != null) StopCoroutine(_bindCoroutine);
            _bindCoroutine = null;
            if (_playerState != null)
                _playerState.CurrentLifeState.OnValueChanged -= OnLifeStateChanged;
            ClearSeatMarker();
            base.OnNetworkDespawn();
        }

        void OnLifeStateChanged(LifeState prev, LifeState curr)
        {
            if (prev != LifeState.Ghost && curr == LifeState.Ghost
                && !_deathPresentationCompleted && _deathCoroutine == null)
            {
                _isDead = true;
                _pendingGhostTransition = true;
                return;
            }
            SyncGhostFromLifeState();
        }

        IEnumerator DeferredBindRoutine()
        {
            float identityDeadline = Time.realtimeSinceStartup + MatchCompositionRoot.InitializationTimeout;
            while (ComputeVisualSlot() < 0)
            {
                if (!IsSpawned || MatchCompositionRoot.Instance == null) yield break;
                if (MatchCompositionRoot.Instance.InitializationFailure != null) yield break;
                if (Time.realtimeSinceStartup >= identityDeadline)
                {
                    Debug.LogError("[PlayerVisual] Seat identity was not assigned before the initialization deadline");
                    yield break;
                }
                yield return null;
            }
            float elapsed = 0f;
            const float timeout = 5f;

            while (elapsed < timeout)
            {
                int visualSlot = ComputeVisualSlot();
                if (visualSlot >= 0)
                {
                    string slotName = ResolveSlotName(visualSlot);
                    if (TryBindToSlot(slotName))
                    {
                        Debug.Log($"[PlayerVisual] Bound to {slotName} (slot={visualSlot}, seat={_playerState.PlayerIndex})");
                        SyncGhostFromLifeState(initialSnapshot: true);
                        _bindCoroutine = null;
                        yield break;
                    }
                }

                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }

            Debug.LogError($"[PlayerVisual] Visual binding failed after {timeout}s — seat={_playerState?.PlayerIndex ?? -1}");
            _bindCoroutine = null;
        }

        int ComputeVisualSlot()
        {
            if (_playerState == null || _playerState.PlayerIndex < 0) return -1;

            int mySeat = _playerState.PlayerIndex;
            int localSeat = GetLocalPlayerSeat();
            if (localSeat < 0) return -1;

            return GetRemoteVisualSlot(mySeat, localSeat);
        }

        // Stable seats retain their visual slot even while other seats spawn/despawn.
        public static int GetRemoteVisualSlot(int seat, int localSeat)
        {
            if (seat < 0 || seat >= 4 || localSeat < 0 || localSeat >= 4 || seat == localSeat)
                return -1;
            return seat < localSeat ? seat : seat - 1;
        }

        GameObject FindSceneVisual(string slotName)
        {
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
                    if (candidate.name == slotName) return candidate.gameObject;
            return null;
        }

        int GetLocalPlayerSeat()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return -1;

            var allStates = FindObjectsByType<PlayerState>(FindObjectsSortMode.None);
            foreach (var ps in allStates)
            {
                if (ps.NetworkObject != null && ps.NetworkObject.OwnerClientId == nm.LocalClientId)
                    return ps.PlayerIndex;
            }
            return -1;
        }

        string ResolveSlotName(int visualSlot)
        {
            string indexed = $"EnemyPlayer_{visualSlot}";
            if (FindSceneVisual(indexed) != null) return indexed;
            if (visualSlot == 0 && FindSceneVisual("EnemyPlayer") != null)
                return "EnemyPlayer";
            return indexed;
        }

        bool TryBindToSlot(string slotName)
        {
            var enemyGO = FindSceneVisual(slotName);
            if (enemyGO == null) return false;
            var claimed = enemyGO.GetComponent<PlayerSeatMarker>();
            if (claimed != null && claimed.Player != null && claimed.Player != _playerState)
                return false;
            enemyGO.SetActive(true);

            _visualRoot = enemyGO.transform;
            _animator = enemyGO.GetComponent<Animator>();
            if (_animator == null)
                _animator = enemyGO.GetComponentInChildren<Animator>();

            Debug.Log($"[PlayerVisual] {slotName} bound: animator={(_animator != null)}, controller={(_animator?.runtimeAnimatorController != null ? _animator.runtimeAnimatorController.name : "NONE")}");

            _spriteRenderers = enemyGO.GetComponentsInChildren<SpriteRenderer>(true);
            _cachedMaterials = new Material[_spriteRenderers.Length];
            for (int i = 0; i < _spriteRenderers.Length; i++)
                _cachedMaterials[i] = _spriteRenderers[i].material;

            Debug.Log($"[PlayerVisual] {slotName}: {_spriteRenderers.Length} sprite renderers found");

            var fanChild = _visualRoot.Find("fan") ?? _visualRoot.Find("Fan");
            if (fanChild != null)
                _fanRenderer = fanChild.GetComponent<SpriteRenderer>();

            _itemTransform = _visualRoot.Find("item");
            if (_itemTransform != null)
            {
                _itemRenderer = _itemTransform.GetComponent<SpriteRenderer>();
                _itemTransform.gameObject.SetActive(false);
            }

            BuildFreezeObject(_visualRoot);
            EnsureGhostPlaceholder();

            var iceBreakT = _visualRoot.Find("IceBreakEffect");
            if (iceBreakT != null)
                _iceBreakParticle = iceBreakT.GetComponent<ParticleSystem>();

            var finalBreakT = _visualRoot.Find("FinalBreakEffect");
            if (finalBreakT != null)
                _finalBreakParticle = finalBreakT.GetComponent<ParticleSystem>();

            Debug.Log($"[PlayerVisual] Particles: iceBreak={(_iceBreakParticle != null)}, finalBreak={(_finalBreakParticle != null)}");

            InitCosmeticController(_visualRoot);

            if (!string.IsNullOrEmpty(_pendingCosmeticDto))
            {
                ApplyRemoteCosmetic(_pendingCosmeticDto);
                _pendingCosmeticDto = null;
            }

            SetupSeatMarker(enemyGO);

            return true;
        }

        void SetupSeatMarker(GameObject visualGO)
        {
            var marker = visualGO.GetComponent<PlayerSeatMarker>();
            if (marker == null)
                marker = visualGO.AddComponent<PlayerSeatMarker>();
            marker.SeatIndex = (byte)_playerState.PlayerIndex;
            marker.Player = _playerState;

            var col = visualGO.GetComponent<BoxCollider>();
            if (col == null)
            {
                col = visualGO.AddComponent<BoxCollider>();
                col.size = new Vector3(1.5f, 2f, 0.5f);
                col.center = new Vector3(0f, 1f, 0f);
            }
            col.enabled = true;
        }

        void ClearSeatMarker()
        {
            if (_visualRoot == null) return;
            var marker = _visualRoot.GetComponent<PlayerSeatMarker>();
            if (marker != null && marker.Player != _playerState) return;
            if (marker != null)
            {
                marker.Player = null;
                marker.SeatIndex = byte.MaxValue;
            }
            var col = _visualRoot.GetComponent<BoxCollider>();
            if (col != null)
                col.enabled = false;
        }

        void BuildFreezeObject(Transform visual)
        {
            _freeze1 = Resources.Load<Sprite>("freeze1");
            _freeze2 = Resources.Load<Sprite>("freeze2");
            _freeze3 = Resources.Load<Sprite>("freeze3");
            if (_freeze1 == null) return;

            var existing = visual.Find("freezeice");
            if (existing != null)
            {
                _freezeRenderer = existing.GetComponent<SpriteRenderer>();
                existing.gameObject.SetActive(false);
                return;
            }

            var freezeGO = new GameObject("freezeice");
            freezeGO.transform.SetParent(visual, false);
            freezeGO.transform.localPosition = new Vector3(0.18f, 0.08f, 0f);

            _freezeRenderer = freezeGO.AddComponent<SpriteRenderer>();
            _freezeRenderer.sortingOrder = 20;
            _freezeRenderer.color = new Color(1f, 1f, 1f, 0.6f);

            var bodyRenderer = visual.Find("body")?.GetComponent<SpriteRenderer>();
            if (bodyRenderer != null)
            {
                _freezeRenderer.sortingLayerID = bodyRenderer.sortingLayerID;
                _freezeRenderer.material = bodyRenderer.sharedMaterial;
            }

            freezeGO.SetActive(false);
        }

        public void PlayAnimation(string triggerName)
        {
            if (_animator == null) return;

            if (_animEndCoroutine != null)
                StopCoroutine(_animEndCoroutine);

            _animator.SetTrigger(triggerName);
            _animEndCoroutine = StartCoroutine(AnimEndRoutine());
        }

        IEnumerator AnimEndRoutine()
        {
            yield return _waitAnimEnd;
            if (_animator != null)
                _animator.SetTrigger("end");
            _animEndCoroutine = null;
        }

        public void SetWind(bool active)
        {
            if (_animator != null)
                _animator.SetBool(IsWindHash, active);
        }

        public void PlayDamageFlash(bool preserveCombatAnimation = false)
        {
            Debug.Log("[PlayerVisual] PlayDamageFlash");
            if (_flashCoroutine != null)
                StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(DamageFlashRoutine(preserveCombatAnimation));
        }

        IEnumerator DamageFlashRoutine(bool preserveCombatAnimation)
        {
            if (_animator != null && !preserveCombatAnimation)
                _animator.SetTrigger("damage");

            float duration = 0.15f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float flash = Mathf.Lerp(1f, 0f, elapsed / duration);
                SetFlashAmount(flash);
                yield return null;
            }

            SetFlashAmount(0f);
            yield return _waitFlashEnd;

            if (_animator != null && !preserveCombatAnimation)
                _animator.SetTrigger("end");

            _flashCoroutine = null;
        }

        void SetFlashAmount(float amount)
        {
            if (_cachedMaterials == null) return;
            for (int i = 0; i < _cachedMaterials.Length; i++)
            {
                if (_cachedMaterials[i] != null)
                    _cachedMaterials[i].SetFloat(FlashAmount, amount);
            }
        }

        void Update()
        {
            if (_spriteRenderers == null || _spriteRenderers.Length == 0 || _playerState == null) return;

            float temp = _playerState.Temperature.Value;
            float normalized = Mathf.Clamp01(temp / 37f);
            Color tint = Color.Lerp(new Color(0.7f, 0.85f, 1f), Color.white, normalized);
            tint.a = _ghostAlpha;

            for (int i = 0; i < _spriteRenderers.Length; i++)
            {
                if (_spriteRenderers[i] != null && _spriteRenderers[i] != _ghostPlaceholderRenderer)
                    _spriteRenderers[i].color = tint;
            }

            if (_fanRenderer != null && _playerState.IsFanUpgraded.Value)
                _fanRenderer.color = new Color(0.4f, 0.6f, 1f, _ghostAlpha);

            if (_animator != null)
            {
                _animator.SetBool(IsWindHash, _playerState.IsFanActive.Value);
                float degree = temp >= 20f ? 0f : temp >= 10f ? 1f : 2f;
                _animator.SetFloat(DegreeHash, degree);
            }

            if (_freezeRenderer != null && _freezeRenderer.gameObject.activeSelf && temp >= 37f)
                _freezeRenderer.gameObject.SetActive(false);
        }

        bool _endsMatch;

        public void PlayDeathSequence(bool endsMatch = false)
        {
            if (_deathCoroutine != null || _deathPresentationCompleted) return;
            Debug.Log($"[PlayerVisual] PlayDeathSequence START (endsMatch={endsMatch})");
            _endsMatch = endsMatch;
            _deathSavedPos = _visualRoot != null ? _visualRoot.position : transform.position;

            if (_flashCoroutine != null)
            {
                StopCoroutine(_flashCoroutine);
                _flashCoroutine = null;
                SetFlashAmount(0f);
            }
            if (_animEndCoroutine != null)
            {
                StopCoroutine(_animEndCoroutine);
                _animEndCoroutine = null;
            }

            if (_animator != null)
            {
                foreach (var p in _animator.parameters)
                    if (p.type == AnimatorControllerParameterType.Trigger)
                        _animator.ResetTrigger(p.nameHash);
                _animator.Play("playerA_freeze 0", 0, 0f);
            }

            _isDead = true;
            _ghostAlpha = 1f;
            if (_ghostPlaceholder != null) _ghostPlaceholder.SetActive(false);
            _deathCoroutine = StartCoroutine(DeathRoutine());
        }

        public Coroutine PlayDeathSequenceAndWait(bool endsMatch)
        {
            PlayDeathSequence(endsMatch);
            return _deathCoroutine;
        }

        public void SettleDeathPresentation()
        {
            if (_deathCoroutine != null)
            {
                StopCoroutine(_deathCoroutine);
                _deathCoroutine = null;
            }
            if (_freezeRenderer != null)
                _freezeRenderer.gameObject.SetActive(false);
            StopBreakParticles();

            if (_playerState != null
                && _playerState.CurrentLifeState.Value == LifeState.Ghost)
            {
                _isDead = true;
                ApplyGhostAppearanceAfterDeath();
            }
            _deathPresentationCompleted = true;
        }

        IEnumerator DeathRoutine()
        {
            yield return null;

            GameAudioManager.Instance?.PlayFreeze();

            if (_freezeRenderer != null)
            {
                _freezeRenderer.color = new Color(1f, 1f, 1f, 1f);
                _freezeRenderer.sprite = _freeze1;
                _freezeRenderer.gameObject.SetActive(true);
            }

            yield return _waitFreezeTick;
            if (_freezeRenderer != null && _freeze2 != null)
                _freezeRenderer.sprite = _freeze2;

            yield return _waitFreezeTick;
            if (_freezeRenderer != null && _freeze3 != null)
                _freezeRenderer.sprite = _freeze3;

            yield return _waitFreezeHold;

            GameAudioManager.Instance?.PlayIceBreak();
            CameraShake.Instance?.Shake(0.5f, 0.3f);
            PlayBreakParticles(_endsMatch);

            if (_endsMatch)
            {
                yield return _waitFreezeHold;
                ApplyGhostAppearanceAfterDeath();
                _deathPresentationCompleted = true;
                _deathCoroutine = null;
                yield break;
            }

            if (_freezeRenderer != null)
                _freezeRenderer.gameObject.SetActive(false);

            var mcr = MatchCompositionRoot.Instance;
            bool isMulti = mcr != null && mcr.ActiveConfig != null
                           && mcr.ActiveConfig.Mode == GameMode.Multi;

            if (isMulti)
                ApplyGhostAppearanceAfterDeath();

            if (_animator != null)
                _animator.Play("Idle_Tree", 0, 0f);

            _deathPresentationCompleted = true;
            _deathCoroutine = null;
        }

        public void ReviveVisual()
        {
            if (!_isDead) return;
            Debug.Log("[PlayerVisual] ReviveVisual");

            if (_deathCoroutine != null)
            {
                StopCoroutine(_deathCoroutine);
                _deathCoroutine = null;
            }

            if (_freezeRenderer != null)
                _freezeRenderer.gameObject.SetActive(false);

            StopBreakParticles();

            if (_visualRoot != null)
                _visualRoot.position = _deathSavedPos;

            if (_animator != null)
            {
                foreach (var p in _animator.parameters)
                    if (p.type == AnimatorControllerParameterType.Trigger)
                        _animator.ResetTrigger(p.nameHash);
                _animator.SetFloat(DegreeHash, 0f);
                _animator.Play("Idle_Tree", 0, 0f);
            }

            _isDead = false;
            _deathPresentationCompleted = false;
            _pendingGhostTransition = false;
            _isGhost = false;
            _ghostAlpha = 1f;
            SetCharacterRenderersVisible(true);
            if (_ghostPlaceholder != null) _ghostPlaceholder.SetActive(false);
        }

        void SyncGhostFromLifeState(bool initialSnapshot = false)
        {
            if (_playerState == null) return;
            var mcr = MatchCompositionRoot.Instance;
            bool isMulti = mcr != null && mcr.ActiveConfig != null
                           && mcr.ActiveConfig.Mode == GameMode.Multi;
            if (!isMulti) return;

            bool shouldBeGhost = _playerState.CurrentLifeState.Value == LifeState.Ghost;

            if (shouldBeGhost)
            {
                if (_deathCoroutine != null) return;
                if (!initialSnapshot && !_deathPresentationCompleted)
                {
                    _isDead = true;
                    _pendingGhostTransition = true;
                    return;
                }
                _isDead = true;
                _isGhost = true;
                ShowGhostPlaceholder();
                _pendingGhostTransition = false;
            }
            else
            {
                _pendingGhostTransition = false;
                _isGhost = false;
                _ghostAlpha = 1f;
                SetCharacterRenderersVisible(true);
                if (_ghostPlaceholder != null) _ghostPlaceholder.SetActive(false);
            }
        }

        void ApplyGhostAppearanceAfterDeath()
        {
            _pendingGhostTransition = false;
            _isGhost = true;
            ShowGhostPlaceholder();
        }

        void EnsureGhostPlaceholder()
        {
            if (_visualRoot == null || _ghostPlaceholder != null) return;
            var existing = _visualRoot.Find("GhostPlaceholder");
            _ghostPlaceholder = existing != null ? existing.gameObject : new GameObject("GhostPlaceholder");
            if (existing == null)
                _ghostPlaceholder.transform.SetParent(_visualRoot, false);

            _ghostPlaceholder.transform.localPosition = new Vector3(0f, 0.72f, -0.08f);
            _ghostPlaceholder.transform.localRotation = Quaternion.identity;
            _ghostPlaceholder.transform.localScale = new Vector3(1.15f, 1.55f, 1f);
            _ghostPlaceholderRenderer = _ghostPlaceholder.GetComponent<SpriteRenderer>();
            if (_ghostPlaceholderRenderer == null)
                _ghostPlaceholderRenderer = _ghostPlaceholder.AddComponent<SpriteRenderer>();
            _ghostPlaceholderRenderer.sprite = GetGhostPlaceholderSprite();
            _ghostPlaceholderRenderer.color = new Color(0.55f, 0.88f, 1f, 0.72f);
            _ghostPlaceholderRenderer.sortingOrder = 85;
            _ghostPlaceholder.SetActive(false);
        }

        void ShowGhostPlaceholder()
        {
            EnsureGhostPlaceholder();
            _ghostAlpha = 0f;
            SetCharacterRenderersVisible(false);
            if (_ghostPlaceholder != null)
            {
                _ghostPlaceholder.SetActive(true);
                _ghostPlaceholderRenderer.enabled = true;
                _ghostPlaceholderRenderer.color = new Color(0.55f, 0.88f, 1f, 0.72f);
            }
        }

        void SetCharacterRenderersVisible(bool visible)
        {
            if (_spriteRenderers == null) return;
            foreach (var renderer in _spriteRenderers)
            {
                if (renderer == null || renderer == _ghostPlaceholderRenderer
                    || renderer == _freezeRenderer || renderer == _itemRenderer)
                    continue;
                renderer.enabled = visible;
            }
        }

        static Sprite GetGhostPlaceholderSprite()
        {
            if (_ghostPlaceholderSprite != null) return _ghostPlaceholderSprite;
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false)
            {
                name = "GhostPlaceholderTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            texture.SetPixels32(pixels);
            texture.Apply();
            _ghostPlaceholderSprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 8f);
            _ghostPlaceholderSprite.name = "GhostPlaceholderSprite";
            return _ghostPlaceholderSprite;
        }

        void PlayBreakParticles(bool heavy = false)
        {
            if (_iceBreakParticle != null)
            {
                _iceBreakParticle.gameObject.SetActive(true);
                _iceBreakParticle.Play();
            }
            if (heavy && _finalBreakParticle != null)
            {
                _finalBreakParticle.gameObject.SetActive(true);
                _finalBreakParticle.Play();
            }
        }

        void StopBreakParticles()
        {
            if (_iceBreakParticle != null)
            {
                _iceBreakParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _iceBreakParticle.gameObject.SetActive(false);
            }
            if (_finalBreakParticle != null)
            {
                _finalBreakParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _finalBreakParticle.gameObject.SetActive(false);
            }
        }

        public void SetItemSprite(Sprite sprite)
        {
            if (_itemRenderer == null) return;
            _itemRenderer.sprite = sprite;
        }

        public void PrepareDefenseSprite(Sprite sprite)
        {
            SetItemSprite(sprite);
            if (_itemTransform != null)
                _itemTransform.gameObject.SetActive(sprite != null);
        }

        public void ClearItemSprite()
        {
            if (_itemRenderer != null)
                _itemRenderer.sprite = null;
            if (_itemTransform != null)
                _itemTransform.gameObject.SetActive(false);
        }

        public void PlayCombatAnimation(string triggerName)
        {
            if (_animator == null)
            {
                Debug.LogWarning($"[PlayerVisual] PlayCombatAnimation('{triggerName}') — _animator is NULL");
                return;
            }
            if (_animEndCoroutine != null)
            {
                StopCoroutine(_animEndCoroutine);
                _animEndCoroutine = null;
            }

            string resolved = triggerName;
            if (!HasParameter(triggerName) && TriggerFallback.TryGetValue(triggerName, out var fb))
            {
                Debug.Log($"[PlayerVisual] PlayCombatAnimation: '{triggerName}' NOT in animator → fallback '{fb}'");
                resolved = fb;
            }
            else
            {
                Debug.Log($"[PlayerVisual] PlayCombatAnimation: '{triggerName}' found in animator → direct trigger");
            }

            _animator.SetTrigger(resolved);
        }

        bool HasParameter(string paramName)
        {
            if (_animator == null) return false;
            foreach (var p in _animator.parameters)
                if (p.name == paramName) return true;
            return false;
        }

        public void ReturnToIdle()
        {
            Debug.Log("[PlayerVisual] ReturnToIdle");
            if (_animEndCoroutine != null)
            {
                StopCoroutine(_animEndCoroutine);
                _animEndCoroutine = null;
            }
            if (_animator != null)
            {
                foreach (var p in _animator.parameters)
                    if (p.type == AnimatorControllerParameterType.Trigger)
                        _animator.ResetTrigger(p.nameHash);
                _animator.SetTrigger("end");
            }
            ClearItemSprite();
        }

        public Animator GetAnimator() => _animator;

        public Transform GetVisualRoot() => _visualRoot;

        public Vector3 GetVisualPosition() =>
            _visualRoot != null ? _visualRoot.position : transform.position;

        void InitCosmeticController(Transform root)
        {
            _cosmeticController = new CosmeticVisualController();

            var head = root.Find("head") ?? root.Find("Head");
            var body = root.Find("body") ?? root.Find("Body");
            var lowerBody = root.Find("lowerbody") ?? root.Find("LowerBody");

            if (head != null) _cosmeticController.SetPartRoot(CosmeticPart.Head, head);
            if (body != null)
            {
                _cosmeticController.SetPartRoot(CosmeticPart.Top, body);
                _cosmeticController.SetPartRoot(CosmeticPart.Back, body);
            }
            if (lowerBody != null) _cosmeticController.SetPartRoot(CosmeticPart.Bottom, lowerBody);

            var tail = root.Find("tail") ?? root.Find("Tail");
            if (tail == null && lowerBody != null)
            {
                var tailGO = new GameObject("tail");
                tailGO.transform.SetParent(lowerBody, false);
                tailGO.transform.localPosition = new Vector3(-0.3f, -0.1f, 0f);
                tail = tailGO.transform;
            }
            if (tail != null) _cosmeticController.SetPartRoot(CosmeticPart.Tail, tail);
        }

        public void ApplyRemoteCosmetic(string json)
        {
            if (_cosmeticController == null)
            {
                _pendingCosmeticDto = json;
                return;
            }

            var service = CosmeticProfileService.Instance;
            if (service != null && service.Registry != null)
            {
                _cosmeticController.ApplyFromDto(json, service.Registry);
                RefreshRendererCache();
            }
        }

        void RefreshRendererCache()
        {
            if (_visualRoot == null) return;
            _spriteRenderers = _visualRoot.GetComponentsInChildren<SpriteRenderer>(true);
            _cachedMaterials = new Material[_spriteRenderers.Length];
            for (int i = 0; i < _spriteRenderers.Length; i++)
                _cachedMaterials[i] = _spriteRenderers[i].material;
        }
    }
}
