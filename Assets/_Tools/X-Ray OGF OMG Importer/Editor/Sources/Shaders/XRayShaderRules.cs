using System;
using System.Collections.Generic;

namespace XRay.Importer.Sources.Shaders
{
    public enum XRaySurfaceKind
    {
        Opaque,
        AlphaTest,
        Transparent,
        Additive,
        Collimator,
        Emissive,
        Distortion
    }

    public sealed class XRaySurfaceDescription
    {
        public XRaySurfaceKind Kind { get; set; }
        public bool DoubleSided { get; set; }
        public float AlphaCutoff { get; set; } = 0.5f;
        public float EmissiveEv100 { get; set; }
        public bool IsApproximation { get; set; }
        public string RuleName { get; set; }
        public string Note { get; set; }

        public XRaySurfaceDescription Clone()
        {
            return (XRaySurfaceDescription)MemberwiseClone();
        }
    }

    public sealed class XRayShaderRule
    {
        public XRayShaderRule(string name, string[] tokens, bool exact, XRaySurfaceDescription surface)
        {
            Name = name;
            Tokens = tokens;
            Exact = exact;
            Surface = surface;
            Surface.RuleName = name;
        }

        public string Name { get; }
        public string[] Tokens { get; }
        public bool Exact { get; }
        public XRaySurfaceDescription Surface { get; }

        public bool Matches(string normalizedShader)
        {
            foreach (string token in Tokens)
            {
                if (Exact ? normalizedShader == token : normalizedShader.IndexOf(token, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class XRayShaderRules
    {
        private readonly List<XRayShaderRule> _rules = new List<XRayShaderRule>();

        public XRayShaderRules(XRaySurfaceDescription fallback)
        {
            Fallback = fallback;
        }

        public XRaySurfaceDescription Fallback { get; }
        public IReadOnlyList<XRayShaderRule> Rules => _rules;

        public static XRayShaderRules CreateDefault()
        {
            var rules = new XRayShaderRules(new XRaySurfaceDescription { Kind = XRaySurfaceKind.Opaque, RuleName = "unknown", IsApproximation = true, Note = "Unknown X-Ray shader; imported as opaque HDRP/Lit." });
            rules.Add(new XRayShaderRule("opaque", new[] { "models\\model", "models\\model_pn", "models\\weapons", "models\\objects", "models\\vehicles", "models\\neutrals", "models\\capture", "models\\oakleys", "models\\mirror", "models\\artefact", "models\\artefact2", "default" }, true, new XRaySurfaceDescription { Kind = XRaySurfaceKind.Opaque }));
            rules.Add(new XRayShaderRule("collimator", new[] { "collimator", "crosshair", "lfo_light_dot" }, false, new XRaySurfaceDescription { Kind = XRaySurfaceKind.Collimator, EmissiveEv100 = 9.0f, IsApproximation = true, Note = "Collimator reticle approximated as additive emissive transparency; the X-Ray reticle projection is not reproduced." }));
            rules.Add(new XRayShaderRule("light_planes", new[] { "lightplanes" }, false, new XRaySurfaceDescription { Kind = XRaySurfaceKind.Additive, DoubleSided = true, IsApproximation = true, Note = "Light planes approximated as additive double-sided transparency." }));
            rules.Add(new XRayShaderRule("distortion", new[] { "xdistort", "xanomaly", "xmonolith" }, false, new XRaySurfaceDescription { Kind = XRaySurfaceKind.Distortion, IsApproximation = true, Note = "Screen-space distortion shader approximated as alpha-blended transparency." }));
            rules.Add(new XRayShaderRule("transparent", new[] { "transparent", "glass", "window", "lens" }, false, new XRaySurfaceDescription { Kind = XRaySurfaceKind.Transparent }));
            rules.Add(new XRayShaderRule("alpha_test_double_sided", new[] { "pautina", "model_fur" }, false, new XRaySurfaceDescription { Kind = XRaySurfaceKind.AlphaTest, DoubleSided = true }));
            rules.Add(new XRayShaderRule("alpha_test", new[] { "aref" }, false, new XRaySurfaceDescription { Kind = XRaySurfaceKind.AlphaTest }));
            rules.Add(new XRayShaderRule("self_light", new[] { "selflight" }, false, new XRaySurfaceDescription { Kind = XRaySurfaceKind.Emissive, EmissiveEv100 = 6.0f, IsApproximation = true, Note = "Self-illuminated shader approximated with the diffuse texture as emission map." }));
            rules.Add(new XRayShaderRule("opaque_prefix", new[] { "models\\dtsm_" }, false, new XRaySurfaceDescription { Kind = XRaySurfaceKind.Opaque }));
            return rules;
        }

        public void Add(XRayShaderRule rule)
        {
            _rules.Add(rule);
        }

        public void Insert(int index, XRayShaderRule rule)
        {
            _rules.Insert(index, rule);
        }

        public XRaySurfaceDescription Resolve(string shader)
        {
            string normalized = (shader ?? string.Empty).Trim().Replace('/', '\\').ToLowerInvariant();
            foreach (XRayShaderRule rule in _rules)
            {
                if (rule.Exact && rule.Matches(normalized))
                {
                    return rule.Surface.Clone();
                }
            }

            foreach (XRayShaderRule rule in _rules)
            {
                if (!rule.Exact && rule.Matches(normalized))
                {
                    return rule.Surface.Clone();
                }
            }

            return Fallback.Clone();
        }
    }
}
