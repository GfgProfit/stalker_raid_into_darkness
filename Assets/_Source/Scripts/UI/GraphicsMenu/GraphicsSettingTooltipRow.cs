using UnityEngine;
using UnityEngine.EventSystems;

namespace RaidIntoDarkness.GraphicsOptions
{
    [DisallowMultipleComponent]
    public sealed class GraphicsSettingTooltipRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private GraphicsSettingsTooltip _tooltip;
        [SerializeField, TextArea(2, 4)] private string _description;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_tooltip != null)
            {
                _tooltip.Show(this, _description);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            HideTooltip();
        }

        private void OnDisable()
        {
            HideTooltip();
        }

        private void HideTooltip()
        {
            if (_tooltip != null)
            {
                _tooltip.Hide(this);
            }
        }
    }
}
