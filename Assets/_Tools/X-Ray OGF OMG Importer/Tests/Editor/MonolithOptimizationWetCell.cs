using System.Runtime.InteropServices;
using UnityEngine;

namespace XRay.Importer.Tests
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct MonolithOptimizationWetCell
    {
        private readonly Vector4 _moisture;
        private readonly Vector2 _heights;

        public MonolithOptimizationWetCell(Vector4 moisture)
        {
            _moisture = moisture;
            _heights = Vector2.zero;
        }
    }
}
