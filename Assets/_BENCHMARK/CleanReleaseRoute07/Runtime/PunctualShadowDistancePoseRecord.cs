using System;

namespace Rvm.CleanReleaseRoute07
{
    [Serializable]
    internal struct PunctualShadowDistancePoseRecord
    {
        public int frame;
        public int tracked;
        public int eligible;
        public int added;
    }
}
