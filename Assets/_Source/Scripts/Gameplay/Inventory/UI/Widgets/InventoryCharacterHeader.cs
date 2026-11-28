using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.UI
{
    [DisallowMultipleComponent]
    public sealed class InventoryCharacterHeader : MonoBehaviour
    {
        [SerializeField] private Image _portrait;
        [SerializeField] private Image _rankIcon;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _communityText;
        [SerializeField] private TMP_Text _moneyText;

        public string CharacterName => _nameText != null ? _nameText.text : string.Empty;
        public string Community => _communityText != null ? _communityText.text : string.Empty;
        public long Money { get; private set; }

        public void Configure(Image portrait, Image rankIcon, TMP_Text nameText, TMP_Text communityText, TMP_Text moneyText)
        {
            _portrait = portrait;
            _rankIcon = rankIcon;
            _nameText = nameText;
            _communityText = communityText;
            _moneyText = moneyText;
        }

        public void SetCharacter(string characterName, string community, Sprite portrait, Sprite rank)
        {
            if (_nameText != null)
            {
                _nameText.text = characterName ?? string.Empty;
            }

            if (_communityText != null)
            {
                _communityText.text = community ?? string.Empty;
            }

            SetSprite(_portrait, portrait);
            SetSprite(_rankIcon, rank);
        }

        public void SetMoney(long money) => SetMoney(money, money.ToString(CultureInfo.InvariantCulture));

        public void SetMoney(long money, string text)
        {
            Money = money;

            if (_moneyText != null)
            {
                _moneyText.text = text;
            }
        }

        private static void SetSprite(Image image, Sprite sprite)
        {
            if (image == null)
            {
                return;
            }

            if (sprite != null)
            {
                image.sprite = sprite;
            }

            image.enabled = image.sprite != null;
        }
    }
}
