using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.UI
{
    public sealed class SpawnMenuCategoryRow : MonoBehaviour
    {
        private const float INDENT = 17f;

        [SerializeField] private Button _toggle;
        [SerializeField] private Button _select;
        [SerializeField] private TMP_Text _arrow;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private Image _selected;

        private SpawnMenuView _owner;
        private SpawnMenuCategory _category;

        private void Awake()
        {
            _toggle.onClick.AddListener(ToggleCategory);
            _select.onClick.AddListener(SelectCategory);
        }

        internal void Bind(SpawnMenuView owner, SpawnMenuCategory category, bool selected)
        {
            _owner = owner;
            _category = category;
            float indent = INDENT * category.Depth;
            var toggle = (RectTransform)_toggle.transform;
            var select = (RectTransform)_select.transform;
            toggle.anchoredPosition = new Vector2(indent, toggle.anchoredPosition.y);
            select.offsetMin = new Vector2(toggle.rect.width + indent, select.offsetMin.y);
            _title.text = category.Title;
            _toggle.gameObject.SetActive(category.Children.Count > 0);
            _arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, category.Expanded ? -90f : 0f);
            _selected.enabled = selected;
        }

        internal void SetSelected(SpawnMenuCategory selected) => _selected.enabled = _category == selected;

        private void SelectCategory() => _owner.SelectCategory(_category);

        private void ToggleCategory() => _owner.ToggleCategory(_category);
    }
}
