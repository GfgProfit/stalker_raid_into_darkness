using UnityEngine;

namespace RaidIntoDarkness.Weapons.Optics
{
    internal sealed class ScopeCaptureExclusion
    {
        private readonly Renderer[] _renderers;
        private readonly bool[] _previousRenderingOff;

        private bool _hidden;

        public ScopeCaptureExclusion(GameObject root)
        {
            _renderers = root != null ? root.GetComponentsInChildren<Renderer>(true) : System.Array.Empty<Renderer>();
            _previousRenderingOff = new bool[_renderers.Length];
        }

        public bool Contains(Renderer renderer)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == renderer)
                {
                    return true;
                }
            }

            return false;
        }

        public void Hide()
        {
            if (_hidden)
            {
                return;
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                {
                    _previousRenderingOff[i] = _renderers[i].forceRenderingOff;
                    _renderers[i].forceRenderingOff = true;
                }
            }

            _hidden = true;
        }

        public void Restore()
        {
            if (!_hidden)
            {
                return;
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                {
                    _renderers[i].forceRenderingOff = _previousRenderingOff[i];
                }
            }

            _hidden = false;
        }
    }
}
