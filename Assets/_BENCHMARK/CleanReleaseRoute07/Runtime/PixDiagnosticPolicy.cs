#if UNITY_EDITOR || CLEAN_RELEASE_ROUTE07
using System;
using System.IO;

namespace Rvm.CleanReleaseRoute07
{
    public static class PixDiagnosticPolicy
    {
        public static bool Requested(string[] args)
        {
            return Array.IndexOf(args, "-benchmark-pix-diagnostic") >= 0 && (Array.IndexOf(args, "-clean-route") >= 0 || Array.IndexOf(args, "-clean-route-config-check") >= 0 || Array.IndexOf(args, "-clean-route-no-timing") >= 0);
        }

        public static bool IsPixModule(string path)
        {
            string name = Path.GetFileName(path);
            return string.Equals(name, "WinPixGpuCapturer.dll", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "WinPixTimingCapturer.dll", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsForbiddenModule(string path)
        {
            string name = Path.GetFileName(path);
            return name.IndexOf("RTSSHooks", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("FramePacingNative", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("renderdoc", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("bifrost", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Nvda.Graphics.Interception", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool ModulesAllowed(bool diagnostic, bool externalDiagnostics, string[] modules)
        {
            bool pix = false;
            if (modules != null)
            {
                for (int i = 0; i < modules.Length; i++)
                {
                    if (IsForbiddenModule(modules[i])) return false;
                    pix |= IsPixModule(modules[i]);
                }
            }
            return !externalDiagnostics && !pix || diagnostic && pix;
        }

        public static void Require(bool diagnostic, bool externalDiagnostics, string[] modules, bool configurationValid)
        {
            if (!configurationValid || !ModulesAllowed(diagnostic, externalDiagnostics, modules)) throw new InvalidOperationException("Release route conditions do not match the required " + (diagnostic ? "PIX diagnostic" : "clean") + " configuration.");
        }

        public static bool HasObserverArguments(string[] args)
        {
            return Array.IndexOf(args, "-batching-counters") >= 0 || Array.IndexOf(args, "-batching-capture") >= 0 || Array.IndexOf(args, "-preparation-counters") >= 0 || Array.IndexOf(args, "-preparation-capture") >= 0 || Array.IndexOf(args, "-particle-counters") >= 0;
        }
    }
}
#endif
