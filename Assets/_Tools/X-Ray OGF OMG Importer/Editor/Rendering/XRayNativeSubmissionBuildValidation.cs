using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor
{
    public sealed class XRayNativeSubmissionBuildValidation : IPreprocessBuildWithReport
    {
        public int callbackOrder => int.MinValue;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (EditorGraphicsSettings.batchRendererGroupShaderStrippingMode != BatchRendererGroupStrippingMode.KeepAll)
            {
                throw new BuildFailedException("X-Ray native instance submission requires Graphics Settings > BatchRendererGroup Variants > Keep All. This setting also applies to ordinary builds without the benchmark module.");
            }
        }
    }

    public sealed class XRayNativeSubmissionShaderStripping : IPreprocessShaders
    {
        private const string SHADER_NAME = "HDRP/MonolithLit";
        private const string DOTS_KEYWORD = "DOTS_INSTANCING_ON";

        private Shader _shader;
        private ShaderKeyword _dotsKeyword;
        private ShaderKeyword[] _localKeywords;
        private GlobalKeyword[] _globalKeywords;
        private bool[] _localAvailable;
        private bool[] _globalAvailable;

        public int callbackOrder => int.MaxValue;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (shader == null || shader.name != SHADER_NAME || data.Count == 0) return;
            EnsureKeywords(shader);
            var counts = new Dictionary<ShaderCompilerPlatform, VariantCounts>();
            int retained = 0;
            for (int index = 0; index < data.Count; index++)
            {
                ShaderCompilerData variant = data[index];
                VariantCounts platformCounts;
                counts.TryGetValue(variant.shaderCompilerPlatform, out platformCounts);
                platformCounts.Incoming++;
                bool dots = variant.shaderKeywordSet.IsEnabled(_dotsKeyword);
                if (dots) platformCounts.Dots++;
                if (dots && Unsupported(variant.shaderKeywordSet))
                {
                    platformCounts.Removed++;
                }
                else
                {
                    if (retained != index) data[retained] = variant;
                    retained++;
                    platformCounts.Retained++;
                }
                counts[variant.shaderCompilerPlatform] = platformCounts;
            }
            for (int index = data.Count - 1; index >= retained; index--) data.RemoveAt(index);
            foreach (KeyValuePair<ShaderCompilerPlatform, VariantCounts> pair in counts)
            {
                VariantCounts platformCounts = pair.Value;
                if (platformCounts.Removed > 0) Debug.Log($"X-Ray native shader variants: pass={snippet.passName}; stage={snippet.shaderType}; api={pair.Key}; incoming={platformCounts.Incoming}; dots={platformCounts.Dots}; removed={platformCounts.Removed}; retained={platformCounts.Retained}; retainedDots={platformCounts.Dots - platformCounts.Removed}.");
            }
        }

        private void EnsureKeywords(Shader shader)
        {
            if (_shader == shader) return;
            _shader = shader;
            _dotsKeyword = new ShaderKeyword(DOTS_KEYWORD);
            IReadOnlyList<string> names = XRayInstanceNativeRenderer.UnsupportedShaderKeywords;
            _localKeywords = new ShaderKeyword[names.Count];
            _globalKeywords = new GlobalKeyword[names.Count];
            _localAvailable = new bool[names.Count];
            _globalAvailable = new bool[names.Count];
            GlobalKeyword[] globals = Shader.globalKeywords;
            for (int index = 0; index < names.Count; index++)
            {
                _localKeywords[index] = new ShaderKeyword(shader, names[index]);
                _localAvailable[index] = _localKeywords[index].IsValid(shader);
                LocalKeyword keyword = shader.keywordSpace.FindKeyword(names[index]);
                if (!keyword.isValid || !keyword.isOverridable) continue;
                for (int globalIndex = 0; globalIndex < globals.Length; globalIndex++)
                {
                    if (globals[globalIndex].name != names[index]) continue;
                    _globalKeywords[index] = globals[globalIndex];
                    _globalAvailable[index] = true;
                    break;
                }
            }
        }

        private bool Unsupported(ShaderKeywordSet keywords)
        {
            for (int index = 0; index < _localKeywords.Length; index++)
            {
                if (_localAvailable[index] && keywords.IsEnabled(_localKeywords[index]) || _globalAvailable[index] && keywords.IsEnabled(_globalKeywords[index])) return true;
            }
            return false;
        }

        private struct VariantCounts
        {
            public int Incoming { get; set; }
            public int Dots { get; set; }
            public int Removed { get; set; }
            public int Retained { get; set; }
        }
    }
}
