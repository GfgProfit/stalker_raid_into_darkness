import argparse
import ctypes
import ctypes.wintypes as w
import datetime
import json
import subprocess
import time
from pathlib import Path

task = Path(__file__).resolve().parent.parent
archive = Path(r'E:\Projects\stalker_raid_into_darkness_Archive').resolve()
if not task.is_relative_to(archive):
    raise RuntimeError('Use the generated launchers under Archive, not the source helper inside Assets.')
user = ctypes.WinDLL('user32', use_last_error=True)
user.GetForegroundWindow.restype = w.HWND
user.GetWindowThreadProcessId.argtypes = [w.HWND, ctypes.POINTER(w.DWORD)]
user.IsWindowVisible.argtypes = [w.HWND]
user.GetClientRect.argtypes = [w.HWND, ctypes.POINTER(w.RECT)]
user.ShowWindow.argtypes = [w.HWND, ctypes.c_int]
user.BringWindowToTop.argtypes = [w.HWND]
user.SetForegroundWindow.argtypes = [w.HWND]
user.keybd_event.argtypes = [ctypes.c_ubyte, ctypes.c_ubyte, w.DWORD, ctypes.c_size_t]
callback = ctypes.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
user.EnumWindows.argtypes = [callback, w.LPARAM]

def save(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')

def windows(pid):
    output = []
    def visit(handle, unused):
        owner = w.DWORD()
        user.GetWindowThreadProcessId(handle, ctypes.byref(owner))
        if owner.value == pid:
            rect = w.RECT()
            user.GetClientRect(handle, ctypes.byref(rect))
            output.append(dict(handle=int(handle), visible=bool(user.IsWindowVisible(handle)), width=rect.right-rect.left, height=rect.bottom-rect.top, foreground=user.GetForegroundWindow() == handle))
        return True
    user.EnumWindows(callback(visit), 0)
    return output

def precheck():
    code = "Get-CimInstance Win32_Process | Where-Object { $_.Name -match 'Unity|ShaderCompiler|bee_backend|HWiNFO|L.?Connect|CleanRelease|RaidIntoDarkness|PresentMon|WPR|Nsight|PIXWin' } | Select-Object ProcessId,ParentProcessId,Name,ExecutablePath,CommandLine | ConvertTo-Json -Compress"
    value = subprocess.run(['powershell.exe', '-NoProfile', '-WindowStyle', 'Hidden', '-Command', code], capture_output=True, text=True, encoding='utf-8', check=True, creationflags=0x08000000)
    processes = json.loads(value.stdout.strip() or '[]')
    if isinstance(processes, dict):
        processes = [processes]
    forbidden = []
    for process in processes:
        name = process['Name'].lower()
        auth = '--internal-auth-broker-serve' in (process.get('CommandLine') or '')
        if name in ('unity.exe', 'unityshadercompiler.exe', 'bee_backend.exe', 'cleanrelease.exe', 'raidintodarkness.exe') and not auth:
            forbidden.append(process)
    if forbidden:
        raise RuntimeError('Editors, workers or another Player remain: ' + json.dumps(forbidden))
    for process in processes:
        process.pop('CommandLine', None)
    return dict(processes=processes, sensors='Unchanged; cooling services kept running', editorsAndWorkersClosed=True)

def run(route, label, mode, kind, player_folder=None, native_submission=None, punctual_shadow_mode=None, punctual_shadow_technical=False, punctual_shadow_capture=False):
    if native_submission is not None and (native_submission not in ('A', 'B') or kind == 'work'):
        raise ValueError('Native submission A/B requires an explicit clean route.')
    if punctual_shadow_mode is None and (punctual_shadow_technical or punctual_shadow_capture):
        raise ValueError('Punctual shadow diagnostics require an explicit A/B mode.')
    if punctual_shadow_mode is not None:
        if punctual_shadow_mode not in ('A', 'B') or mode != 'U' or native_submission not in (None, 'A') or kind == 'work':
            raise ValueError('Punctual shadow A/B requires an explicit clean route, grouping U and native submission A.')
        native_submission = 'A'
        punctual_shadow_technical = punctual_shadow_technical or kind in ('technical', 'capture') or punctual_shadow_capture
        punctual_shadow_capture = punctual_shadow_capture or kind == 'capture'
    output = task / 'Runs' / label
    if not output.resolve().is_relative_to(task):
        raise ValueError('Run label must stay inside the build output directory.')
    output.mkdir(parents=True, exist_ok=False)
    try:
        save(output / 'precheck.json', precheck())
    except Exception as exception:
        save(output / 'precheck-failure.json', dict(error=str(exception), playerWasLaunched=False))
        raise
    executable = (Path(player_folder) if player_folder else task / 'Player') / 'CleanRelease.exe'
    arguments = [str(executable), '-force-d3d12', '-screen-width', '1920', '-screen-height', '1080', '-screen-fullscreen', '1', '-window-mode', 'borderless', '-location-batching', mode, '-benchmark-route', route, '-logFile', str(output / 'Player.log'), '-crash-report-folder', str(output / 'Crashes')]
    if native_submission is not None:
        arguments += ['-instance-native-submission', native_submission]
    if punctual_shadow_mode is not None:
        arguments += ['-punctual-shadow-mode', punctual_shadow_mode]
        if punctual_shadow_technical:
            arguments += ['-punctual-shadow-technical']
        if punctual_shadow_capture:
            arguments += ['-punctual-shadow-capture']
    if kind == 'work':
        arguments += ['-location-preparation-work', '-preparation-output', str(output)]
    else:
        arguments += ['-clean-route-config-check' if kind == 'config' else '-clean-route', '-clean-route-output', str(output)]
        if punctual_shadow_mode is None and kind in ('capture', 'technical'):
            arguments += ['-batching-counters']
        if punctual_shadow_mode is None and kind == 'capture':
            arguments += ['-batching-capture']
    launch = dict(startedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(), arguments=arguments, mainSeries=kind == 'timing' and not punctual_shadow_technical, startupActivationOnly=True, systemAuditsDuringMeasurement=0, nativeSubmissionRequested=native_submission, punctualShadowMode=punctual_shadow_mode, punctualShadowTechnical=punctual_shadow_technical, punctualShadowCapture=punctual_shadow_capture)
    process = subprocess.Popen(arguments, cwd=executable.parent)
    launch['pid'] = process.pid
    deadline = time.monotonic() + 35
    while process.poll() is None and time.monotonic() < deadline:
        candidates = [value for value in windows(process.pid) if value['visible'] and value['width'] > 500]
        if candidates:
            handle = candidates[0]['handle']
            if not candidates[0]['foreground']:
                user.ShowWindow(handle, 9)
                user.BringWindowToTop(handle)
                user.keybd_event(0x12, 0, 0, 0)
                user.SetForegroundWindow(handle)
                user.keybd_event(0x12, 0, 2, 0)
            launch['startupWindow'] = windows(process.pid)
            break
        time.sleep(.1)
    save(output / 'launch.json', launch)
    try:
        result = process.wait(timeout=150)
    except subprocess.TimeoutExpired:
        process.terminate()
        process.wait(timeout=15)
        save(output / 'driver-failure.json', dict(reason='Player did not finish within the fixed timeout; retained as rejected attempt'))
        raise
    save(output / 'exit.json', dict(code=result, endedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat()))
    state_path = output / 'restoration.json'
    if state_path.exists():
        state = json.loads(state_path.read_text(encoding='utf-8-sig'))
        if not state.get('cameraRestored') or not state.get('weatherRestored') or punctual_shadow_mode is not None and not state.get('punctualShadowsRestored'):
            save(output / 'driver-failure.json', dict(reason='Session state was not restored; retained as rejected attempt'))
            result = 2
    if punctual_shadow_mode is not None:
        result_path = output / 'result.json'
        evidence = output / 'punctual-shadow-after.json'
        if not state_path.exists() or not result_path.exists() or not evidence.exists() or json.loads(result_path.read_text(encoding='utf-8-sig')).get('punctualShadowMode') != punctual_shadow_mode:
            save(output / 'driver-failure.json', dict(reason='Punctual shadow mode/evidence was not applied or restored; retained as rejected attempt'))
            result = 2
    print(json.dumps(dict(run=label, mode=mode, kind=kind, exitCode=result)), flush=True)
    return result

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('route')
    parser.add_argument('mode', choices=['U', 'G1'])
    parser.add_argument('--label')
    parser.add_argument('--kind', choices=['timing', 'config', 'capture', 'technical'], default='timing')
    parser.add_argument('--player-folder')
    parser.add_argument('--native-submission', choices=['A', 'B'])
    parser.add_argument('--punctual-shadow-mode', choices=['A', 'B'])
    parser.add_argument('--punctual-shadow-technical', action='store_true')
    parser.add_argument('--punctual-shadow-capture', action='store_true')
    args = parser.parse_args()
    suffix = '_Shadow' + args.punctual_shadow_mode if args.punctual_shadow_mode is not None else ''
    raise SystemExit(run(args.route, args.label or datetime.datetime.now().strftime('%Y%m%d_%H%M%S') + '_' + args.route + '_' + args.mode + suffix, args.mode, args.kind, args.player_folder, args.native_submission, args.punctual_shadow_mode, args.punctual_shadow_technical, args.punctual_shadow_capture))
