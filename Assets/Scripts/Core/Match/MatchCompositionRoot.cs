using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Player.Identity;
using UnityEngine;

namespace AbsoluteZero.Core.Match
{
    public class MatchCompositionRoot : MonoBehaviour
    {
        public static MatchCompositionRoot Instance { get; private set; }

        PlayerRegistry _registry;
        public IReadOnlyPlayerRegistry Registry => _registry;
        public PlayerRegistry WritableRegistry => _registry;

        ItemManager _itemManager;
        MatchManager _matchManager;
        public ItemManager ItemManager => _itemManager;
        public MatchManager MatchManager => _matchManager;

        void Awake()
        {
            if (Instance != null)
            {
                Debug.LogError("[MatchCompositionRoot] Duplicate Root detected — destroying this instance");
                Destroy(this);
                return;
            }

            Instance = this;
            _registry = new PlayerRegistry();
            ValidateSceneReferences();
            Debug.Log("[MatchCompositionRoot] Awake — Registry created, scene references validated");
        }

        void ValidateSceneReferences()
        {
            _itemManager = FindAnyObjectByType<ItemManager>();
            _matchManager = FindAnyObjectByType<MatchManager>();

            if (_itemManager == null)
                Debug.LogWarning("[MatchCompositionRoot] ItemManager not found in scene");
            if (_matchManager == null)
                Debug.LogWarning("[MatchCompositionRoot] MatchManager not found in scene");
        }

        void OnDestroy()
        {
            if (Instance != this) return;

            _registry?.Clear();
            Instance = null;
            Debug.Log("[MatchCompositionRoot] Destroyed — Registry cleared");
        }
    }
}
