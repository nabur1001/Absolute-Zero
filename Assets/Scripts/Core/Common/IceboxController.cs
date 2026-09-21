using System.Collections;
using AbsoluteZero.Core.Audio;
using UnityEngine;

namespace AbsoluteZero.Core.Common
{
    public class IceboxController : MonoBehaviour
    {
        public static IceboxController Instance { get; private set; }

        [SerializeField] GameObject _iceboxPrefab;

        GameObject _iceboxInstance;
        Animator _iceboxAnimator;
        ParticleSystem _iceboxParticle;
        Transform _boxSpawnPoint;
        bool _isAnimating;

        public bool IsAnimating => _isAnimating;

        static readonly int IsOpenHash = Animator.StringToHash("isOpen");
        static readonly WaitForSeconds _waitBoxOpen = new(0.5f);
        static readonly WaitForSeconds _waitItemDelay = new(0.08f);
        static readonly WaitForSeconds _waitBeforeClose = new(0.3f);
        static readonly WaitForSeconds _waitArcFinish = new(ARC_DURATION);

        const float ARC_DURATION = 0.5f;
        const float ARC_HEIGHT = 1.5f;

        void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        void Start()
        {
            var sp = GameObject.Find("BoxSpawnPoint");
            if (sp != null) _boxSpawnPoint = sp.transform;

            Debug.Log($"[Icebox] Start: BoxSpawnPoint={(_boxSpawnPoint != null)}, prefab={(_iceboxPrefab != null)}");

            if (_boxSpawnPoint != null)
                HideSpawnMarkers(_boxSpawnPoint);

            if (_iceboxPrefab != null && _boxSpawnPoint != null)
            {
                if (_iceboxInstance != null)
                {
                    Debug.LogWarning("[Icebox] _iceboxInstance already exists — skipping duplicate Instantiate");
                }
                else
                {
                    _iceboxInstance = Instantiate(_iceboxPrefab, _boxSpawnPoint.position, Quaternion.identity);
                    _iceboxAnimator = _iceboxInstance.GetComponent<Animator>();
                    _iceboxParticle = _iceboxInstance.GetComponentInChildren<ParticleSystem>(true);
                    SetupParticle();
                    Debug.Log($"[Icebox] Instantiated at {_boxSpawnPoint.position}, animator={(_iceboxAnimator != null)}, particle={(_iceboxParticle != null)}");
                }
            }
            else
            {
                Debug.LogWarning($"[Icebox] NOT instantiated — prefab={(_iceboxPrefab != null)}, spawnPoint={(_boxSpawnPoint != null)}");
            }
        }

        static void HideSpawnMarkers(Transform parent)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (!child.name.StartsWith("_SpawnMarker")) continue;

                var mr = child.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.enabled = false;
                    Debug.Log($"[Icebox] Disabled MeshRenderer on '{child.name}' under '{parent.name}'");
                }
            }
        }

        void SetupParticle()
        {
            if (_iceboxParticle == null) return;

            var psr = _iceboxParticle.GetComponent<ParticleSystemRenderer>();
            if (psr == null) return;

            if (psr.sharedMaterial == null)
            {
                var mat = Resources.Load<Material>("ParticlesUnlit");
                if (mat == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                    if (shader != null)
                    {
                        mat = new Material(shader);
                        mat.SetTexture("_BaseMap", Texture2D.whiteTexture);
                        mat.SetColor("_BaseColor", new Color(1f, 1f, 0.8f, 0.7f));
                    }
                }
                if (mat != null)
                    psr.material = mat;
                Debug.Log($"[Icebox] Assigned particle material: {psr.sharedMaterial?.name ?? "FAILED"}");
            }

            psr.sortingLayerName = "Default";
            psr.sortingOrder = 100;
            psr.material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            psr.material.SetInt("_ZWrite", 0);

            _iceboxParticle.gameObject.SetActive(false);
            _iceboxParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Repositions the local presentation anchor and an already spawned box.
        /// Multi uses this after Start so scene initialization order cannot leave
        /// the box at the legacy 1v1 location.
        /// </summary>
        public bool TrySetPresentationPosition(Vector3 worldPosition)
        {
            if (_boxSpawnPoint == null)
            {
                var spawnPoint = GameObject.Find("BoxSpawnPoint");
                if (spawnPoint == null) return false;
                _boxSpawnPoint = spawnPoint.transform;
            }

            _boxSpawnPoint.position = worldPosition;
            if (_iceboxInstance != null)
                _iceboxInstance.transform.position = worldPosition;
            return true;
        }

        public void PlayDistribution(Transform[] itemTransforms)
        {
            if (_boxSpawnPoint == null || itemTransforms == null || itemTransforms.Length == 0)
            {
                Debug.LogWarning($"[Icebox] PlayDistribution SKIPPED — spawnPoint={(_boxSpawnPoint != null)}, items={itemTransforms?.Length ?? 0}");
                return;
            }
            Debug.Log($"[Icebox] PlayDistribution: {itemTransforms.Length} items, wasAnimating={_isAnimating}");
            if (_isAnimating) StopAllCoroutines();
            StartCoroutine(DistributionRoutine(itemTransforms));
        }

        IEnumerator DistributionRoutine(Transform[] items)
        {
            _isAnimating = true;
            Vector3 boxPos = _boxSpawnPoint.position + Vector3.up * 0.5f;
            Debug.Log($"[Icebox] DistributionRoutine START: {items.Length} items, boxPos={boxPos}");

            var targets = new Vector3[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null) continue;
                targets[i] = items[i].position;
                items[i].position = boxPos;
                items[i].localScale = Vector3.zero;
            }

            if (_iceboxAnimator != null)
                _iceboxAnimator.SetBool(IsOpenHash, true);

            if (_iceboxParticle != null)
            {
                _iceboxParticle.gameObject.SetActive(true);
                _iceboxParticle.Play(true);
            }

            GameAudioManager.Instance?.PlayBoxOpen();

            yield return _waitBoxOpen;

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null) continue;
                StartCoroutine(ArcMoveTo(items[i], boxPos, targets[i]));
                yield return _waitItemDelay;
            }

            yield return _waitArcFinish;
            yield return _waitBeforeClose;

            if (_iceboxParticle != null)
            {
                _iceboxParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _iceboxParticle.gameObject.SetActive(false);
            }

            if (_iceboxAnimator != null)
                _iceboxAnimator.SetBool(IsOpenHash, false);

            _isAnimating = false;
            Debug.Log("[Icebox] DistributionRoutine DONE — box closed");
        }

        IEnumerator ArcMoveTo(Transform item, Vector3 from, Vector3 to)
        {
            float elapsed = 0f;
            while (elapsed < ARC_DURATION)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / ARC_DURATION);
                float smooth = t * t * (3f - 2f * t);

                Vector3 pos = Vector3.Lerp(from, to, smooth);
                pos.y += ARC_HEIGHT * 4f * t * (1f - t);

                if (item == null) yield break;
                item.position = pos;
                item.localScale = Vector3.Lerp(Vector3.zero, Vector3.one, Mathf.Min(t * 3f, 1f));
                yield return null;
            }

            if (item == null) yield break;
            item.position = to;
            item.localScale = Vector3.one;
        }
    }
}
