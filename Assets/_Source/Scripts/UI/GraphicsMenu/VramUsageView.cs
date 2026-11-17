using TMPro;
using UnityEngine;
using UnityEngine.Profiling;

namespace RaidIntoDarkness.GraphicsOptions
{
    [DisallowMultipleComponent]
    public sealed class VramUsageView : MonoBehaviour
    {
        private const double SAMPLE_INTERVAL = 1.0;
        private const double BYTES_PER_GIB = 1073741824.0;

        [SerializeField] private TMP_Text _device;
        [SerializeField] private TMP_Text _adapterUsage;
        [SerializeField] private TMP_Text _processUsage;
        [SerializeField] private RectTransform _fill;

        private WindowsVideoMemoryProbe _probe;
        private double _nextSample;

        private void OnEnable()
        {
            _probe = WindowsVideoMemoryProbe.Open();
            _device.text = "VRAM  ·  " + SystemInfo.graphicsDeviceName;
            Refresh();
        }

        private void OnDisable()
        {
            _probe?.Dispose();
            _probe = null;
        }

        private void Update()
        {
            if (Time.realtimeSinceStartupAsDouble >= _nextSample)
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            _nextSample = Time.realtimeSinceStartupAsDouble + SAMPLE_INTERVAL;
            ulong? used = null;
            ulong? process = null;
            ulong? budget = null;
            _probe?.Read(out used, out process, out budget);
            ulong capacity = _probe?.DedicatedCapacity ?? (ulong)Mathf.Max(0, SystemInfo.graphicsMemorySize) * 1048576UL;
            if (_probe != null && capacity == 0)
            {
                _adapterUsage.text = $"Общая память GPU  {AsGib(_probe.SharedCapacity)}";
            }
            else
            {
                _adapterUsage.text = $"Занято {AsGib(used)} / {AsGib(capacity > 0 ? capacity : null)}";
            }
            bool hasBar = used.HasValue && capacity > 0;
            float ratio = hasBar ? Mathf.Clamp01((float)((double)used.Value / capacity)) : 0.0f;
            if (!Mathf.Approximately(_fill.anchorMax.x, ratio))
            {
                _fill.anchorMax = new Vector2(ratio, 1.0f);
            }
            string prefix = Application.isEditor ? "Редактор сейчас" : "Игра сейчас";
            if (process.HasValue)
            {
                _processUsage.text = $"{prefix} около {AsGib(process)}  ·  бюджет {AsGib(budget)}";
                return;
            }
            long allocated = Profiler.GetAllocatedMemoryForGraphicsDriver();
            _processUsage.text = allocated > 0 ? $"{prefix} около {AsGib((ulong)allocated)}  ·  оценка Unity" : $"{prefix} —";
        }

        private static string AsGib(ulong? bytes)
        {
            return bytes.HasValue ? $"{bytes.Value / BYTES_PER_GIB:0.0} ГиБ" : "—";
        }
    }
}
