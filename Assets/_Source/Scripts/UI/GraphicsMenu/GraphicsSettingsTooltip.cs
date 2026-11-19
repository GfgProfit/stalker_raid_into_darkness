using TMPro;
using UnityEngine;

namespace RaidIntoDarkness.GraphicsOptions
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class GraphicsSettingsTooltip : MonoBehaviour
    {
        private TMP_Text _text;
        private GraphicsSettingTooltipRow _activeRow;

        private void Awake()
        {
            _text = GetComponent<TMP_Text>();
            Clear();
        }

        private void OnDisable()
        {
            Clear();
        }

        internal void Show(GraphicsSettingTooltipRow row, string description)
        {
            if (!isActiveAndEnabled || _text == null || _activeRow == row)
            {
                return;
            }

            _activeRow = row;
            _text.text = description;
        }

        internal void Hide(GraphicsSettingTooltipRow row)
        {
            if (_activeRow == row)
            {
                Clear();
            }
        }

        private void Clear()
        {
            _activeRow = null;
            if (_text != null)
            {
                _text.text = string.Empty;
            }
        }
    }
}
