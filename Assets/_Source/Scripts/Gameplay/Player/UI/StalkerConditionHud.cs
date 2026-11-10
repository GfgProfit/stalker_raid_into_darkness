using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class StalkerConditionHud : MonoBehaviour
    {
        [SerializeField] private StalkerActorCondition _condition;
        [SerializeField] private Image _healthFill;
        [SerializeField] private Image _psyHealthFill;
        [SerializeField] private Image _powerFill;
        [SerializeField] private RectTransform _healthLimit;
        [SerializeField] private RectTransform _psyHealthLimit;
        [SerializeField] private RectTransform _powerLimit;

        public void Bind(StalkerActorCondition condition, Image healthFill, Image psyHealthFill, Image powerFill, RectTransform healthLimit, RectTransform psyHealthLimit, RectTransform powerLimit)
        {
            _condition = condition;
            _healthFill = healthFill;
            _psyHealthFill = psyHealthFill;
            _powerFill = powerFill;
            _healthLimit = healthLimit;
            _psyHealthLimit = psyHealthLimit;
            _powerLimit = powerLimit;
            Refresh();
        }

        public void Refresh()
        {
            if (_condition == null)
            {
                return;
            }

            Apply(_healthFill, _healthLimit, _condition.Health, _condition.MaxHealth, _condition.HealthCapacity);
            Apply(_psyHealthFill, _psyHealthLimit, _condition.PsyHealth, _condition.MaxPsyHealth, _condition.PsyHealthCapacity);
            Apply(_powerFill, _powerLimit, _condition.Power, _condition.MaxPower, _condition.PowerCapacity);
        }

        private void LateUpdate()
        {
            Refresh();
        }

        private static void Apply(Image fill, RectTransform limit, float value, float maximum, float capacity)
        {
            if (fill == null)
            {
                return;
            }

            fill.fillAmount = Normalize(value, capacity);
            if (limit == null)
            {
                return;
            }

            RectTransform fillRect = fill.rectTransform;
            Rect rect = fillRect.rect;
            Vector3 point = fillRect.TransformPoint(new Vector3(Mathf.Lerp(rect.xMin, rect.xMax, Normalize(maximum, capacity)), rect.center.y, 0.0f));
            Vector3 localPoint = limit.parent != null ? limit.parent.InverseTransformPoint(point) : point;
            Vector3 position = limit.localPosition;
            if (position.x != localPoint.x)
            {
                position.x = localPoint.x;
                limit.localPosition = position;
            }
        }

        private static float Normalize(float value, float capacity)
        {
            return float.IsFinite(value) && float.IsFinite(capacity) && capacity > 0.0f ? Mathf.Clamp01(value / capacity) : 0.0f;
        }
    }
}
