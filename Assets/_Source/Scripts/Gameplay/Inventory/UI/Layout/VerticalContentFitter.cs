using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class VerticalContentFitter : MonoBehaviour
    {
        private const float EPSILON = 0.001f;

        [SerializeField, Tooltip("Расстояние между соседними активными дочерними объектами по вертикали в единицах Canvas. Дочерние объекты укладываются от верхнего края этого объекта вниз в порядке иерархии без отступа сверху. Ширина и высота этого объекта не меняются; у дочерних объектов меняется только положение по Y. Дочерние объекты не должны растягиваться по вертикали от якорей.")] private float _spacing;

        private RectTransform _rectTransform;
        private DrivenRectTransformTracker _tracker;

        public float Spacing => _spacing;

        public void SetSpacing(float spacing)
        {
            _spacing = spacing;
            Refresh();
        }

        public void Refresh()
        {
            if (_rectTransform == null) _rectTransform = (RectTransform)transform;
            float spacing = float.IsFinite(_spacing) ? _spacing : 0f;
            _tracker.Clear();
            int childCount = _rectTransform.childCount;
            float top = _rectTransform.rect.yMax;
            float offset = 0f;
            for (int i = 0; i < childCount; i++)
            {
                if (!(_rectTransform.GetChild(i) is RectTransform child) || !child.gameObject.activeSelf) continue;
                _tracker.Add(this, child, DrivenTransformProperties.AnchoredPositionY);
                Rect rect = child.rect;
                float scale = child.localScale.y;
                float y = top - offset - Mathf.Max(rect.yMin * scale, rect.yMax * scale);
                Vector3 position = child.localPosition;
                if (Mathf.Abs(position.y - y) > EPSILON) child.localPosition = new Vector3(position.x, y, position.z);
                offset += GetHeight(child) + spacing;
            }
        }

        private void OnEnable()
        {
            _rectTransform = (RectTransform)transform;
            Refresh();
        }

        private void OnDisable()
        {
            _tracker.Clear();
        }

        private void LateUpdate()
        {
            Refresh();
        }

        private void OnTransformChildrenChanged()
        {
            if (isActiveAndEnabled) Refresh();
        }

        private static float GetHeight(RectTransform child)
        {
            return child.rect.height * Mathf.Abs(child.localScale.y);
        }
    }
}
