using AbsoluteZero.Core.Player;
using Unity.Netcode;
using UnityEngine;

namespace AbsoluteZero.Core.Common
{
    /// <summary>
    /// 독립 선풍기 오브젝트의 날개 회전 컨트롤러.
    /// 해당 플레이어의 선풍기가 켜져 있을 때(PlayerState.IsFanActive)만 날개가 돌고,
    /// 준비(Ready) 완료로 선풍기가 꺼지면 부드럽게 감속해 멈춘다. (머리 날림 애니메이션과 동일 신호)
    /// </summary>
    public class FanBladeSpinner : MonoBehaviour
    {
        [Header("=== Refs ===")]
        [Tooltip("회전시킬 날개 Transform (fan_blades 스프라이트)")]
        [SerializeField] private Transform blades;

        [Header("=== Spin ===")]
        [Tooltip("최대 회전 속도 (deg/s)")]
        [SerializeField] private float spinSpeed = 900f;
        [Tooltip("가속률 (deg/s^2)")]
        [SerializeField] private float accel = 1800f;
        [Tooltip("감속 시간 (초)")]
        [SerializeField] private float decelDuration = 0.2f;
        [Tooltip("시계방향(-1) / 반시계(+1)")]
        [SerializeField] private float direction = -1f;

        [Header("=== Player Binding ===")]
        [Tooltip("-1 = 로컬 플레이어, -2 = 상대 플레이어, 0/1 = 특정 clientId")]
        [SerializeField] private int playerIndex = -1;
        [Tooltip("플레이어 상태를 아직 못 찾았을 때 기본으로 돌릴지")]
        [SerializeField] private bool spinWhenNoState = true;

        private float _current;
        private float _angle;                                    // 현재 스핀 각 (0~360 wrap — 무한 누적/오버플로우 방지)
        private Quaternion _baseRotation = Quaternion.identity;  // 초기 기울기(Y틸트) 보존용
        private bool _baseCaptured;
        private PlayerState _cached;

        private void Awake()
        {
            // 인스펙터 미할당 시 자식 "Blades" 자동 탐색 (프리팹 자동 조립 대비)
            if (blades == null)
            {
                var t = transform.Find("Blades");
                if (t != null) blades = t;
            }
            CaptureBase();
        }

        private void CaptureBase()
        {
            if (blades != null && !_baseCaptured)
            {
                _baseRotation = blades.localRotation;   // Y 기울기 등 초기 자세
                _angle = 0f;
                _baseCaptured = true;
            }
        }

        private void Update()
        {
            if (blades == null) return;

            if (ShouldSpin())
            {
                _current = Mathf.MoveTowards(_current, spinSpeed, accel * Time.deltaTime);
            }
            else if (_current > 0.5f)
            {
                float t = 1f - Mathf.Pow(0.001f, Time.deltaTime / Mathf.Max(decelDuration, 0.01f));
                _current = Mathf.Lerp(_current, 0f, t);
            }
            else
            {
                _current = 0f;
            }
            if (!Mathf.Approximately(_current, 0f))
            {
                // 각도를 직접 관리해 360으로 wrap → 트랜스폼에 무한 누적 안 됨
                _angle = Mathf.Repeat(_angle + direction * _current * Time.deltaTime, 360f);
                blades.localRotation = _baseRotation * Quaternion.Euler(0f, 0f, _angle);
            }
        }

        private bool ShouldSpin()
        {
            var ps = GetPlayerState();
            if (ps == null) return spinWhenNoState;
            return ps.IsFanActive.Value;
        }

        /// <summary>바인딩 대상 설정 (런타임 생성 시 SpawnStayItemFans에서 호출)</summary>
        public void Bind(Transform bladesTf, int index)
        {
            blades = bladesTf;
            playerIndex = index;
            _cached = null;
        }

        private PlayerState GetPlayerState()
        {
            if (_cached != null) return _cached;

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return null;   // 세션 시작 전/후엔 SpawnManager 접근 금지
            var sm = nm.SpawnManager;
            if (sm == null || sm.SpawnedObjects == null) return null;

            foreach (var kvp in sm.SpawnedObjects)
            {
                var netObj = kvp.Value;
                if (netObj == null || !netObj.IsPlayerObject) continue;

                bool match;
                if (playerIndex == -1) match = netObj.OwnerClientId == nm.LocalClientId;
                else if (playerIndex == -2) match = netObj.OwnerClientId != nm.LocalClientId;
                else
                {
                    var ps = netObj.GetComponent<PlayerState>();
                    match = ps != null && ps.PlayerIndex == playerIndex;
                }

                if (match)
                {
                    _cached = netObj.GetComponent<PlayerState>();
                    return _cached;
                }
            }
            return null;
        }
    }
}
