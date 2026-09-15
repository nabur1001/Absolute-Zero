using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Collections;
using UnityEngine;

namespace AbsoluteZero.Core.Cosmetic
{
    public class CosmeticProfileService : MonoBehaviour
    {
        const string NicknameKey = "player_nickname";
        static readonly Regex NicknameRegex = new(@"^[가-힣a-zA-Z0-9]+$", RegexOptions.Compiled);

        [SerializeField] CosmeticRegistrySO _registry;

        public static CosmeticProfileService Instance { get; private set; }

        string _nickname;
        CosmeticEquipState _equipState;

        public string Nickname => _nickname;
        public CosmeticEquipState EquipState => _equipState;
        public CosmeticRegistrySO Registry => _registry;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _nickname = PlayerPrefs.GetString(NicknameKey, "");

            _equipState = new CosmeticEquipState();
            if (_registry != null)
                _equipState.Load(_registry);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool TryValidateNickname(string raw, out string validated)
        {
            validated = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string trimmed = raw.Trim().Normalize(NormalizationForm.FormC);
            if (!NicknameRegex.IsMatch(trimmed)) return false;

            int textLen = new StringInfo(trimmed).LengthInTextElements;
            if (textLen < 2 || textLen > 8) return false;

            validated = trimmed;
            return true;
        }

        public void SetNickname(string validated)
        {
            _nickname = validated;
            PlayerPrefs.SetString(NicknameKey, validated);
            PlayerPrefs.Save();
        }

        public string GetCompactDto()
        {
            var dto = _equipState.ToDto();
            string json = JsonUtility.ToJson(dto);

            if (Encoding.UTF8.GetByteCount(json) > 125)
            {
                Debug.LogWarning("[CosmeticProfileService] Compact DTO exceeds 125 UTF-8 bytes — returning empty");
                return "";
            }

            return json;
        }

        // 9-step server-side validation
        public bool TryValidateAndCanonicalizeDto(string raw, out FixedString128Bytes canonical)
        {
            canonical = default;

            // 1. null/empty
            if (string.IsNullOrEmpty(raw)) return false;

            // 2. byte length pre-check
            if (Encoding.UTF8.GetByteCount(raw) > 125) return false;

            // 3. parse
            CosmeticDto dto;
            try
            {
                dto = JsonUtility.FromJson<CosmeticDto>(raw);
            }
            catch
            {
                return false;
            }

            // 4. dto null
            if (dto == null) return false;

            // 5. version
            if (dto.v != 1) return false;

            // 6. validate each part Id against registry
            if (_registry == null) return false;
            if (!ValidatePartId(dto.head, CosmeticPart.Head)) return false;
            if (!ValidatePartId(dto.top, CosmeticPart.Top)) return false;
            if (!ValidatePartId(dto.back, CosmeticPart.Back)) return false;
            if (!ValidatePartId(dto.bottom, CosmeticPart.Bottom)) return false;
            if (!ValidatePartId(dto.tail, CosmeticPart.Tail)) return false;

            // 7. canonical re-serialize
            string canonicalJson = JsonUtility.ToJson(dto);

            // 8. byte recheck
            if (Encoding.UTF8.GetByteCount(canonicalJson) > 125) return false;

            // 9. assign
            canonical = new FixedString128Bytes(canonicalJson);
            return true;
        }

        bool ValidatePartId(string id, CosmeticPart expectedPart)
        {
            if (string.IsNullOrEmpty(id)) return true;
            var item = _registry.GetById(id);
            if (item == null) return false;
            return item.Part == expectedPart;
        }
    }
}
