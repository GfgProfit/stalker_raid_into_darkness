using XRay.Importer.Core.Level;
using XRay.Importer.Sources.Shaders;

namespace XRay.Importer.Editor.Levels
{
    public enum XRayLevelSurfaceKind
    {
        Opaque,
        AlphaTest,
        Terrain,
        Emissive,
        Transparent,
        Additive,
        Decal,
        Water,
        Glow,
        Skipped
    }

    public enum XRayLevelHemiMode
    {
        Constant = 0,
        Lightmap = 1,
        Vertex = 2,
        AlbedoAlpha = 3
    }

    public sealed class XRayLevelSurface
    {
        public XRayLevelSurfaceKind Kind { get; set; }
        public XRayLevelHemiMode HemiMode { get; set; }
        public float AlphaCutoff { get; set; } = 200.0f / 255.0f;
        public bool DoubleSided { get; set; }
        public bool CastShadows { get; set; } = true;
        public bool Flora { get; set; }
        public bool IsFallback { get; set; }
        public bool IsApproximation { get; set; }
        public string RuleName { get; set; }
        public string BlenderClass { get; set; }
        public string Note { get; set; }
        public bool UsesMonolith => !IsFallback && Kind != XRayLevelSurfaceKind.Skipped && Kind != XRayLevelSurfaceKind.Decal;
        public bool IsDecal => !IsFallback && Kind == XRayLevelSurfaceKind.Decal;

        public XRaySurfaceDescription ToFallbackDescription()
        {
            var description = new XRaySurfaceDescription { RuleName = RuleName, DoubleSided = DoubleSided, AlphaCutoff = AlphaCutoff, IsApproximation = true, Note = Note };
            switch (Kind)
            {
                case XRayLevelSurfaceKind.Additive:
                case XRayLevelSurfaceKind.Glow:
                    description.Kind = XRaySurfaceKind.Additive;
                    break;
                case XRayLevelSurfaceKind.AlphaTest:
                    description.Kind = XRaySurfaceKind.AlphaTest;
                    break;
                case XRayLevelSurfaceKind.Opaque:
                case XRayLevelSurfaceKind.Terrain:
                    description.Kind = XRaySurfaceKind.Opaque;
                    break;
                default:
                    description.Kind = XRaySurfaceKind.Transparent;
                    break;
            }

            return description;
        }
    }

    public static class XRayLevelSurfaceRules
    {
        public const float R2_AREF = 200.0f / 255.0f;

        public static XRayLevelSurface Classify(XRayLevelShader shader, XRayBlender blender, bool isTree)
        {
            string name = shader.EngineShader.Trim().Replace('/', '\\').ToLowerInvariant();
            string blenderClass = blender != null ? blender.ClassId : null;
            bool twoLightmaps = shader.Lightmaps.Count >= 2;
            XRayLevelHemiMode staticHemi = twoLightmaps ? XRayLevelHemiMode.Lightmap : XRayLevelHemiMode.Vertex;
            var surface = new XRayLevelSurface { BlenderClass = blenderClass ?? "unknown", HemiMode = isTree ? XRayLevelHemiMode.Vertex : staticHemi };
            bool noShadow = name.Contains("noshadow");
            surface.CastShadows = !noShadow;
            if (name == "effects\\wallmarkblend")
            {
                surface.Kind = XRayLevelSurfaceKind.Decal;
                surface.RuleName = "wallmark";
                surface.CastShadows = false;
                surface.Note = "Static wallmark (effects_wallmarkblend.s: wmark.vs + simple.ps, srcalpha/invsrcalpha into the albedo G-buffer RGB, Z-test on, Z-write off, wmark_shift) imported as HDRP/MonolithDecal: DBuffer albedo blend applied by MonolithLit receivers before lighting.";
                return surface;
            }

            if (name.StartsWith("effects\\wallmark"))
            {
                string note = name == "effects\\wallmarkset" ? "Wallmark SET pass (no blend, Z-write on) is not reproduced; explicit HDRP/Lit transparent fallback without shadows." : "Unsupported wallmark pass; effects\\wallmark and effects\\wallmarkmult use destcolor/srccolor blending, which is not reproduced by the HDRP/Lit transparent fallback without shadows.";
                return Fallback(surface, XRayLevelSurfaceKind.Decal, "wallmark", note, false);
            }

            if (name.StartsWith("effects\\water") || name.Contains("water"))
            {
                return Fallback(surface, XRayLevelSurfaceKind.Water, "water", "X-Ray water shader (reflection, refraction, flow) is not reproduced; HDRP/Lit transparent fallback.", false);
            }

            if (name.StartsWith("effects\\glow"))
            {
                return Fallback(surface, XRayLevelSurfaceKind.Glow, "glow", "Glow sprite shader; HDRP/Lit additive fallback.", false);
            }

            if (name.Contains("lightplanes"))
            {
                surface.DoubleSided = true;
                return Fallback(surface, XRayLevelSurfaceKind.Additive, "light_planes", "Light planes approximated as HDRP/Lit additive double-sided transparency.", false);
            }

            if (name.StartsWith("details\\"))
            {
                surface.Kind = XRayLevelSurfaceKind.Skipped;
                surface.RuleName = "details_lod";
                surface.Note = "Detail LOD shader is not used by level geometry.";
                return surface;
            }

            if (blenderClass == "BmmDold" || blenderClass == "BmmD" || name.StartsWith("levels\\"))
            {
                surface.Kind = XRayLevelSurfaceKind.Terrain;
                surface.HemiMode = XRayLevelHemiMode.AlbedoAlpha;
                surface.RuleName = "terrain";
                surface.IsApproximation = true;
                surface.Note = "Terrain (BmmD): hemisphere from base texture alpha and material 0.95 as in deffer_impl_flat; 4-detail splat by s_mask with detail bumps (.wzyx normal/gloss, intensity 0.6) and base x2 modulation; rain puddles, ripples and wet gloss are not reproduced.";
                return surface;
            }

            if (name.Contains("selflight"))
            {
                surface.Kind = XRayLevelSurfaceKind.Emissive;
                surface.RuleName = "self_light";
                surface.IsApproximation = true;
                surface.Note = "Self-illumination approximated with the diffuse texture as HDRP emission (6 EV100).";
                return surface;
            }

            bool alphaBlend = blender != null && blender.GetBool("Base Texture/Alpha-blend", false);
            if (blenderClass == "LM_AREF" || blenderClass == "V_AREF" || (blender == null && (name.Contains("aref") || name.Contains("trans"))))
            {
                if (alphaBlend || (blender == null && name.Contains("trans")))
                {
                    return Fallback(surface, XRayLevelSurfaceKind.Transparent, "alpha_blend", "Alpha-blended static surface (Alpha-blend=1); HDRP/Lit transparent fallback, no Monolith BRDF.", false);
                }

                surface.Kind = XRayLevelSurfaceKind.AlphaTest;
                surface.AlphaCutoff = R2_AREF;
                surface.RuleName = "alpha_test";
                return surface;
            }

            if (blenderClass == "D_TREE" || name.StartsWith("flora\\"))
            {
                if (alphaBlend || name.Contains("leaf"))
                {
                    surface.Kind = XRayLevelSurfaceKind.AlphaTest;
                    surface.AlphaCutoff = R2_AREF;
                    surface.DoubleSided = true;
                    surface.RuleName = "tree_leaves";
                    surface.Flora = true;
                    surface.IsApproximation = true;
                    surface.Note = "Tree leaves: alpha test 200/255, double-sided without normal flip; with a THM bump (no detail, no parallax) TS deffer_base_aref_bump-hq.ps writes the flora normal normalize(3*(bump.wz-0.5+0.02).x, 1, .y) and MAT_FLORA; tree wind animation is not reproduced.";
                    return surface;
                }

                surface.Kind = XRayLevelSurfaceKind.Opaque;
                surface.RuleName = "tree_trunk";
                surface.Note = "Tree trunk wind animation is not reproduced.";
                return surface;
            }

            if (blenderClass == "LM" || blenderClass == "V" || name == "default" || name.Contains("def_vertex") || name.Contains("def_lmap"))
            {
                surface.Kind = XRayLevelSurfaceKind.Opaque;
                surface.RuleName = "opaque";
                return surface;
            }

            if (blenderClass == "S_SET")
            {
                return Fallback(surface, XRayLevelSurfaceKind.Transparent, "effect_set", $"Effect shader '{shader.EngineShader}' (S_SET) is not reproduced; HDRP/Lit transparent fallback.", false);
            }

            surface.Kind = XRayLevelSurfaceKind.Opaque;
            surface.RuleName = "unknown";
            surface.IsApproximation = true;
            surface.Note = $"Unknown engine shader '{shader.EngineShader}' (class {surface.BlenderClass}); imported as opaque MonolithLit.";
            return surface;
        }

        private static XRayLevelSurface Fallback(XRayLevelSurface surface, XRayLevelSurfaceKind kind, string rule, string note, bool castShadows)
        {
            surface.Kind = kind;
            surface.RuleName = rule;
            surface.Note = note;
            surface.IsFallback = true;
            surface.IsApproximation = true;
            surface.CastShadows = castShadows;
            return surface;
        }
    }
}
