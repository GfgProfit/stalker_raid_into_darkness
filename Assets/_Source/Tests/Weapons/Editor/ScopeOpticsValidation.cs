using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace RaidIntoDarkness.Weapons.Tests
{
    public static class ScopeOpticsValidation
    {
        [Serializable]
        private sealed class ValidationCase
        {
            [SerializeField] private string _fixture;
            [SerializeField] private string _test;
            [SerializeField] private bool _passed;
            [SerializeField] private string _error;

            public ValidationCase(string fixture, string test, string error)
            {
                _fixture = fixture;
                _test = test;
                _passed = error == null;
                _error = error;
            }
        }

        [Serializable]
        private sealed class ValidationReport
        {
            [SerializeField] private int _passed;
            [SerializeField] private int _failed;
            [SerializeField] private List<ValidationCase> _cases = new List<ValidationCase>();
            [SerializeField] private List<string> _warnings = new List<string>();

            public void Add(string fixture, string test, string error)
            {
                _cases.Add(new ValidationCase(fixture, test, error));
                if (error == null)
                {
                    _passed++;
                }
                else
                {
                    _failed++;
                }
            }

            public void AddWarning(string warning)
            {
                _warnings.Add(warning);
            }
        }

        public static string Run()
        {
            if (Application.isPlaying)
            {
                throw new InvalidOperationException("ScopeOpticsValidation requires Edit Mode.");
            }

            var report = new ValidationReport();
            Type[] fixtures = { typeof(ScopeOpticsMathTests), typeof(ScopeOpticsStateTests), typeof(ScopeOpticsRendererTests), typeof(ScopeLensGeometryTests), typeof(ScopeMarchBindingTests), typeof(ScopeProProjectionTests) };
            foreach (Type fixture in fixtures)
            {
                foreach (MethodInfo method in fixture.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    object[] cases = method.GetCustomAttributes(typeof(TestCaseAttribute), false);
                    if (cases.Length > 0)
                    {
                        for (int index = 0; index < cases.Length; index++)
                        {
                            RunCase(report, fixture, method, ((TestCaseAttribute)cases[index]).Arguments, method.Name + "[" + index + "]");
                        }
                    }
                    else if (method.IsDefined(typeof(TestAttribute), false))
                    {
                        RunCase(report, fixture, method, null, method.Name);
                    }
                }
            }

            return JsonUtility.ToJson(report);
        }

        public static string CompileShader()
        {
            return CompileShaderAsset("Assets/_Source/Shaders/Optics/ScopeLens.shader");
        }

        public static string CompileScopeProShader()
        {
            return CompileShaderAsset("Assets/_Source/Shaders/Optics/ScopeProLens.shader");
        }

        private static string CompileShaderAsset(string assetPath)
        {
            if (Application.isPlaying)
            {
                throw new InvalidOperationException("ScopeOpticsValidation requires Edit Mode.");
            }

            var report = new ValidationReport();
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
            if (shader == null)
            {
                report.Add("ScopeLens", "LoadShader", assetPath + " is unavailable.");
                return JsonUtility.ToJson(report);
            }

            ShaderData.Subshader subshader = ShaderUtil.GetShaderData(shader).GetSubshader(0);
            for (int index = 0; index < subshader.PassCount; index++)
            {
                ShaderData.Pass pass = subshader.GetPass(index);
                foreach (ShaderCompilerPlatform platform in new[] { ShaderCompilerPlatform.D3D, ShaderCompilerPlatform.D3D12 })
                {
                    foreach (ShaderType stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                    {
                        ShaderData.VariantCompileInfo result = pass.CompileVariant(stage, Array.Empty<string>(), platform, BuildTarget.StandaloneWindows64);
                        string context = pass.Name + ":" + platform + ":" + stage;
                        string error = result.Success ? null : "Shader compiler returned an unsuccessful result.";
                        foreach (ShaderMessage message in result.Messages)
                        {
                            string detail = message.severity + ": " + message.message + " (" + message.file + ":" + message.line + ")";
                            if (message.severity.ToString() == "Error")
                            {
                                error = (error == null ? string.Empty : error + " | ") + detail;
                            }
                            else
                            {
                                report.AddWarning(context + ": " + detail);
                            }
                        }

                        report.Add("ScopeLens", context, error);
                    }
                }
            }

            return JsonUtility.ToJson(report);
        }

        private static void RunCase(ValidationReport report, Type fixtureType, MethodInfo test, object[] arguments, string name)
        {
            object fixture = Activator.CreateInstance(fixtureType);
            MethodInfo setUp = fixtureType.GetMethod("SetUp", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo tearDown = fixtureType.GetMethod("TearDown", BindingFlags.Instance | BindingFlags.Public);
            string error = null;
            try
            {
                setUp?.Invoke(fixture, null);
                test.Invoke(fixture, arguments);
            }
            catch (Exception exception)
            {
                error = Describe(exception);
            }
            finally
            {
                try
                {
                    tearDown?.Invoke(fixture, null);
                }
                catch (Exception exception)
                {
                    error = (error == null ? string.Empty : error + " | ") + "TearDown: " + Describe(exception);
                }
            }

            report.Add(fixtureType.Name, name, error);
        }

        private static string Describe(Exception exception)
        {
            Exception cause = exception is TargetInvocationException invocation && invocation.InnerException != null ? invocation.InnerException : exception;
            return cause.GetType().Name + ": " + cause.Message;
        }
    }
}
