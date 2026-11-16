# Optional PIX diagnostic bridge

This Windows x64 bridge belongs exclusively to `_BENCHMARK`. `PixDiagnosticSession` loads it only after explicit `-clean-route -benchmark-pix-diagnostic` admission. There is no native load from static initialization, ordinary startup or game code. Plugin importers disable preload. The bridge never loads the GPU capturer: launch the Player through PIX before D3D12 device creation.

`RC06Capture` calls the official `PIXGpuCaptureNextFrames(path, 1)` present-to-present API. `PIXGetCaptureState` is not used as proof of attachment or successful recording. Unity's Development-only `Rendering.PIX` wrapper is not used.

CPU markers contain route key, sequence, Unity frame, position, original Stopwatch ticks and absolute Windows QPC. Each native call is bracketed with additional Stopwatch reads. GPU markers use Unity 6000.6.7f1's `IUnityGraphicsD3D12v7.CommandRecordingState` inside an `IssuePluginEventAndData` callback, configured without flushing, worker synchronization or render-target changes. They identify camera command recording, not Present or an automatically corresponding CPU/GPU execution frame. No pass names are invented.

Fixed buffers hold at most 16,384 CPU and GPU records each. Overflow invalidates the diagnostic run. Markers cover the route, including warmup and teardown. CSV files are written only after the route; unpublished trailing GPU callbacks remain unrecorded. GPU snapshots use release/acquire publication. Stop is idempotent and removes rendering callbacks before writing evidence.

## Rebuild

Download the official `WinPixEventRuntime` **1.0.240308001** package from `https://api.nuget.org/v3-flatcontainer/winpixeventruntime/1.0.240308001/winpixeventruntime.1.0.240308001.nupkg` and extract it under Archive. It is not registered as a Unity or NuGet project dependency. Its runtime DLL is MIT licensed; retain the upstream license with the module.

The build used portable Zig **0.17.1**, SHA-256 `b03f477a1943673e02aca0f570e8030dffd536ce1f45cbc4d3326ec31159562e`, from `https://ziglang.org/download/0.17.1/zig-x86_64-windows-0.17.1.zip`. Compiler, SDK and all intermediate output remain under Archive. The MinGW/Clang path includes Windows headers before enabling the SDK's MSVC-version compatibility check; the official PIX header implementation and runtime binary are unchanged. Native/managed structs use sequential 8-byte alignment.

```powershell
& Assets/_BENCHMARK/CleanReleaseRoute07/Native/Build.ps1 -ZigCompiler '<Archive>/Tools/zig/zig.exe' -PixSdk '<Archive>/Tools/WinPixEventRuntime' -ArtifactOutput '<Archive>/NativeBuild'
```

The script installs only the two required plugin DLLs inside this module. Unity Plugin API headers come from the installed Production Editor. Rebuild and verify the saved marker content after changing the bridge or SDK; DLL presence alone is insufficient.
