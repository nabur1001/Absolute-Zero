using System.Collections;
using System.Collections.Generic;
using AbsoluteZero.Core.Audio;
using AbsoluteZero.Core.Combat;
using AbsoluteZero.Core.Common;
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
            if (!TryBindEnemyVisual())
                _bindCoroutine = StartCoroutine(RetryBindEnemyVisual());
        }

        bool TryBindEnemyVisual()
        {
            var enemyGO = GameObject.Find("EnemyPlayer");
            if (enemyGO == null) return false;

            _visualRoot = enemyGO.transform;
            _animator = enemyGO.GetComponent<Animator>();
            if (_animator == null)
                _animator = enemyGO.GetComponentInChildren<Animator>();

            Debug.Log($"[PlayerVisual] EnemyPlayer bound: animator={(_animator != null)}, controller={(_animator?.runtimeAnimatorController != null ? _animator.runtimeAnimatorController.name : "NONE")}");

            _spriteRenderers = enemyGO.GetComponentsInChildren<SpriteRenderer>(true);
            _cachedMaterials = new Material[_spriteRenderers.Length];
            for (int i = 0; i < _spriteRenderers.Length; i++)
                _cachedMaterials[i] = _spriteRenderers[i].material;

            Debug.Log($"[PlayerVisual] EnemyPlayer: {_spriteRenderers.Length} sprite renderers found");

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

            var iceBreakT = _visualRoot.Find("IceBreakEffect");
            if (iceBreakT != null)
                _iceBreakParticle = iceBreakT.GetComponent<ParticleSystem>();

            var finalBreakT = _visualRoot.Find("FinalBreakEffect");
            if (finalBreakT != null)
                _finalBreakParticle = finalBreakT.GetComponent<ParticleSystem>();

            Debug.Log($"[PlayerVisual] Particles: iceBreak={(_iceBreakParticle != null)}, finalBreak={(_finalBreakParticle != null)}");
            return true;
        }

        IEnumerator RetryBindEnemyVisual()
        {
            float elapsed = 0f;
            const float timeout = 3f;
            while (elapsed < timeout)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
                if (TryBindEnemyVisual())
                {
                    _bindCoroutine = null;
                    yield break;
                }
            }
            Debug.LogError($"[PlayerVisual] EnemyPlayer NOT FOUND after {timeout}s — visual binding failed");
            _bindCoroutine = null;
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

        public void PlayDamageFlash()
        {
            Debug.Log("[PlayerVisual] PlayDamageFlash");
            if (_flashCoroutine != null)
                StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(DamageFlashRoutine());
        }

        IEnumerator DamageFlashRoutine()
        {
            if (_animator != null)
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

            if (_animator != null)
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

            for (int i = 0; i < _spriteRenderers.Length; i++)
            {
                if (_spriteRenderers[i] != null)
                    _spriteRenderers[i].color = tint;
            }

            if (_fanRenderer != null && _playerState.IsFanUpgraded.Value)
                _fanRenderer.color = new Color(0.4f, 0.6f, 1f);

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
            if (_isDead) return;
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
            _deathCoroutine = StartCoroutine(DeathRoutine());
        }

        public Coroutine PlayDeathSequenceAndWait(bool endsMatch)
        {
            PlayDeathSequence(endsMatch);
            return _deathCoroutine;
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
                _deathCoroutine = null;
                yield break;
            }

            if (_freezeRenderer != null)
                _freezeRenderer.gameObject.SetActive(false);

            if (_animator != null)
                _animator.Play("Idle_Tree", 0, 0f);

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
    }
}
