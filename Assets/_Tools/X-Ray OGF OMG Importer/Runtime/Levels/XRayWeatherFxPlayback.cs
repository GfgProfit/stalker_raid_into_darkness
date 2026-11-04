using UnityEngine;

namespace XRay.Importer.Levels
{
    public enum XRayWeatherFxPhase
    {
        None,
        Entry,
        Effect,
        Return
    }

    internal sealed class XRayWeatherFxPlayback
    {
        private readonly XRayWeatherProfile _ordinary;

        private XRayWeatherSnapshot _entryPrevious;
        private XRayWeatherSnapshot _entryNext;
        private XRayWeatherSnapshot _returnKey;
        private float _entryWeight;

        public XRayWeatherFxProfile Effect { get; }
        public double EntrySeconds { get; }
        public double ElapsedSeconds { get; set; }
        public bool AuthoredFlarePublished { get; set; }
        public double DurationSeconds => EffectEnd + EntrySeconds;
        public XRayWeatherFxPhase Phase => ElapsedSeconds < EntrySeconds ? XRayWeatherFxPhase.Entry : ElapsedSeconds < EffectEnd ? XRayWeatherFxPhase.Effect : XRayWeatherFxPhase.Return;

        private double EffectEnd => EntrySeconds + Effect.AuthoredDurationSeconds;

        public XRayWeatherFxPlayback(XRayWeatherFxProfile effect, XRayWeatherProfile ordinary, double entrySeconds, double worldSeconds, double elapsedSeconds)
        {
            Effect = effect;
            _ordinary = ordinary;
            EntrySeconds = entrySeconds;
            ElapsedSeconds = elapsedSeconds;
            Rebase(worldSeconds);
        }

        public void Rebase(double startWorldSeconds)
        {
            _ordinary.GetBracketingKeyframes(XRayWeatherProfile.NormalizeTime(startWorldSeconds), out _entryPrevious, out _entryNext, out _entryWeight);
            _ordinary.GetBracketingKeyframes(XRayWeatherProfile.NormalizeTime(startWorldSeconds + EffectEnd), out _, out _returnKey, out _);
        }

        public XRayWeatherSnapshot Evaluate(double worldSeconds, out float effectWeight)
        {
            XRayWeatherSnapshot frame;
            if (ElapsedSeconds < EntrySeconds)
            {
                frame = XRayWeatherSnapshot.Blend(_entryPrevious, _entryNext, Mathf.Lerp(_entryWeight, 1.0f, (float)(ElapsedSeconds / EntrySeconds)));
                effectWeight = 0.0f;
            }
            else if (ElapsedSeconds >= EffectEnd)
            {
                float weight = Mathf.Clamp01((float)((ElapsedSeconds - EffectEnd) / EntrySeconds));
                frame = XRayWeatherSnapshot.Blend(Effect.GetKeyframe(Effect.KeyframeCount - 1), _returnKey, weight);
                effectWeight = 1.0f - weight;
            }
            else
            {
                double time = ElapsedSeconds - EntrySeconds;
                int next = 1;
                while (next < Effect.KeyframeCount - 1 && Effect.GetKeyframe(next).GameSeconds < time) next++;
                XRayWeatherSnapshot first = next == 1 ? _entryNext : Effect.GetKeyframe(next - 1);
                XRayWeatherSnapshot second = Effect.GetKeyframe(next);
                double firstTime = next == 1 ? 0.0 : first.GameSeconds;
                float weight = (float)((time - firstTime) / (second.GameSeconds - firstTime));
                frame = XRayWeatherSnapshot.Blend(first, second, weight);
                effectWeight = next == 1 ? weight : 1.0f;
            }
            return frame.WithSampling(worldSeconds, _ordinary.SunDirection(worldSeconds), _ordinary.VisibilityScale);
        }
    }
}
