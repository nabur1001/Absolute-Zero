using AbsoluteZero.Core.Player;
using AbsoluteZero.UI.Game.Bridge;
using AbsoluteZero.UI.Game.Build;
using Unity.Netcode;
using UnityEngine;

namespace AbsoluteZero.UI.Game.Presenters
{
    public sealed class OpponentBarPresenter : MonoBehaviour
    {
        IGameDataBridge _bridge;
        Canvas _oppBarCanvas;

        bool _attached;
        bool _initialized;

        static readonly Vector3 LOCAL_OFFSET = new(0f, 2.2f, 0f);

        public void Initialize(IGameDataBridge bridge, GameHudRefs refs)
        {
            _bridge = bridge;
            _oppBarCanvas = refs.OppBarCanvas;
            _initialized = true;
        }

        void Update()
        {
            if (!_initialized || _oppBarCanvas == null) return;

            if (!_attached)
                TryAttach();

            var cam = Camera.main;
            if (cam == null) return;

            _oppBarCanvas.worldCamera = cam;
            _oppBarCanvas.transform.rotation = cam.transform.rotation;
        }

        void TryAttach()
        {
            Transform target = null;

            byte localSeat = _bridge.LocalSeatIndex;
            var players = FindObjectsByType<PlayerState>(FindObjectsSortMode.None);
            foreach (var ps in players)
            {
                if (ps.PlayerIndex != localSeat)
                {
                    var visual = ps.GetComponent<AZPlayerVisual>();
                    if (visual != null)
                    {
                        var vRoot = visual.GetVisualRoot();
                        if (vRoot != null)
                            target = vRoot;
                    }
                    break;
                }
            }

            if (target == null)
            {
                var enemyGO = GameObject.Find("EnemyPlayer");
                if (enemyGO != null)
                    target = enemyGO.transform;
            }

            if (target == null) return;

            _oppBarCanvas.transform.SetParent(target, false);
            _oppBarCanvas.transform.localPosition = LOCAL_OFFSET;
            _attached = true;
        }
    }
}
