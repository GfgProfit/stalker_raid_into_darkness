#include <windows.h>
#include <d3d12.h>
#include <dxgi.h>
#include <shlobj.h>
#include <atomic>
#include "IUnityInterface.h"
#include "IUnityGraphicsD3D12.h"
#if defined(__MINGW32__) && !defined(_MSC_VER)
#define _MSC_VER 1940
#endif
#include "pix3.h"

constexpr int CAPACITY = 16384;
constexpr int EVENT_ID = 6006;

struct Stamp
{
    long long Before;
    long long After;
    long long Stopwatch;
    int Sequence;
    int Frame;
    int Position;
    int Kind;
    unsigned int Process;
    unsigned int Thread;
    int Recorded;
    int Reserved;
};

static IUnityGraphicsD3D12v7* _graphics;
static Stamp _gpu[CAPACITY];
static std::atomic<int> _published[CAPACITY];
static int _route;
static int _cpuCount;
static int _gpuCount;
static std::atomic<int> _enabled;

extern "C" __declspec(dllexport) void UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* interfaces)
{
    _graphics = interfaces->Get<IUnityGraphicsD3D12v7>();
}

extern "C" __declspec(dllexport) void UNITY_INTERFACE_API UnityPluginUnload()
{
    _enabled.store(0);
    _graphics = nullptr;
}

static void UNITY_INTERFACE_API RenderMarker(int eventId, void* data)
{
    if (!_enabled.load() || eventId != EVENT_ID || !_graphics || !data) return;
    auto stamp = static_cast<Stamp*>(data);
    LARGE_INTEGER counter;
    QueryPerformanceCounter(&counter);
    stamp->Before = counter.QuadPart;
    stamp->Process = GetCurrentProcessId();
    stamp->Thread = GetCurrentThreadId();
    UnityGraphicsD3D12RecordingState state = {};
    if (_graphics->CommandRecordingState(&state) && state.commandList)
    {
        PIXSetMarker(state.commandList, PIX_COLOR(80, 190, 250), "RC06GPU route=%d seq=%d frame=%d pos=%d kind=%d", _route, stamp->Sequence, stamp->Frame, stamp->Position, stamp->Kind);
        stamp->Recorded = 1;
    }
    QueryPerformanceCounter(&counter);
    stamp->After = counter.QuadPart;
    _published[stamp->Sequence].store(1, std::memory_order_release);
}

extern "C" __declspec(dllexport) int RC06Initialize(int route)
{
    _route = route;
    _cpuCount = 0;
    _gpuCount = 0;
    if (_graphics)
    {
        UnityD3D12PluginEventConfig config = {};
        config.graphicsQueueAccess = kUnityD3D12GraphicsQueueAccess_DontCare;
        config.flags = 0;
        config.ensureActiveRenderTextureIsBound = false;
        _graphics->ConfigureEvent(EVENT_ID, &config);
    }
    _enabled.store(1);
    return (GetModuleHandleW(L"WinPixGpuCapturer.dll") ? 1 : 0) | (PIXIsAttachedForGpuCapture() ? 2 : 0) | (_graphics ? 4 : 0);
}

extern "C" __declspec(dllexport) void RC06Stop()
{
    _enabled.store(0);
}

extern "C" __declspec(dllexport) void* RC06RenderCallback()
{
    return reinterpret_cast<void*>(&RenderMarker);
}

extern "C" __declspec(dllexport) long long RC06Frequency()
{
    LARGE_INTEGER frequency;
    QueryPerformanceFrequency(&frequency);
    return frequency.QuadPart;
}

extern "C" __declspec(dllexport) int RC06Stamp(int frame, int position, int kind, long long stopwatch, Stamp* stamp)
{
    if (!_enabled.load() || _cpuCount >= CAPACITY) return 1;
    *stamp = {};
    stamp->Stopwatch = stopwatch;
    stamp->Sequence = _cpuCount++;
    stamp->Frame = frame;
    stamp->Position = position;
    stamp->Kind = kind;
    stamp->Process = GetCurrentProcessId();
    stamp->Thread = GetCurrentThreadId();
    LARGE_INTEGER counter;
    QueryPerformanceCounter(&counter);
    stamp->Before = counter.QuadPart;
    PIXSetMarker(PIX_COLOR(80, 190, 250), "RC06CPU route=%d seq=%d frame=%d pos=%d kind=%d sw=%lld qpc=%lld", _route, stamp->Sequence, frame, position, kind, stopwatch, stamp->Before);
    QueryPerformanceCounter(&counter);
    stamp->After = counter.QuadPart;
    stamp->Recorded = 1;
    return 0;
}

extern "C" __declspec(dllexport) void* RC06PrepareGpu(int frame, int position, int kind, long long stopwatch)
{
    if (!_enabled.load() || _gpuCount >= CAPACITY) return nullptr;
    auto stamp = &_gpu[_gpuCount];
    *stamp = {};
    stamp->Sequence = _gpuCount++;
    _published[stamp->Sequence].store(0);
    stamp->Frame = frame;
    stamp->Position = position;
    stamp->Kind = kind;
    stamp->Stopwatch = stopwatch;
    return stamp;
}

extern "C" __declspec(dllexport) int RC06ReadGpu(Stamp* output, int capacity)
{
    int count = _gpuCount < capacity ? _gpuCount : capacity;
    for (int i = 0; i < count; i++)
    {
        output[i] = {};
        if (_published[i].load(std::memory_order_acquire)) output[i] = _gpu[i];
    }
    return count;
}

extern "C" __declspec(dllexport) int RC06Capture(const wchar_t* filename)
{
    if (!_enabled.load() || !GetModuleHandleW(L"WinPixGpuCapturer.dll")) return E_UNEXPECTED;
    return PIXGpuCaptureNextFrames(filename, 1);
}
