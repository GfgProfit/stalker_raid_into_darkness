namespace Rvm.Benchmark.Editor
{
    public static class HdrpShadowPackagePatch
    {
        public static void RequireApplied()
        {
            XRay.Importer.Editor.Rendering.HdrpShadowBindingPatch.RequireApplied();
        }
    }
}
