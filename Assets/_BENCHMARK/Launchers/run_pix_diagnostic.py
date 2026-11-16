import argparse
import ctypes
import datetime
import importlib.util
import json
import subprocess
import time
from pathlib import Path

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('route')
    parser.add_argument('--pix', required=True)
    parser.add_argument('--label', required=True)
    parser.add_argument('--player-folder')
    parser.add_argument('--capture-position', type=int, choices=range(1, 5))
    parser.add_argument('--timing', action='store_true')
    args = parser.parse_args()
    if args.timing and args.capture_position:
        raise ValueError('Use separate live timing and structural captures.')
    spec = importlib.util.spec_from_file_location('route_driver', Path(__file__).with_name('run_benchmark.py'))
    driver = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(driver)
    output = driver.task / 'Runs' / args.label
    if not output.resolve().is_relative_to(driver.task):
        raise ValueError('Run output must remain in its Archive task directory.')
    output.mkdir(parents=True, exist_ok=False)
    if args.capture_position:
        (output / 'Captures').mkdir()
    driver.save(output / 'precheck.json', driver.precheck())
    executable = (Path(args.player_folder) if args.player_folder else driver.task / 'Player') / 'CleanRelease.exe'
    pix = Path(args.pix).resolve(strict=True)
    app_args = ['-force-d3d12', '-screen-width', '1920', '-screen-height', '1080', '-screen-fullscreen', '1', '-window-mode', 'borderless', '-location-batching', 'U', '-benchmark-route', args.route, '-clean-route', '-benchmark-pix-diagnostic', '-clean-route-output', str(output), '-logFile', str(output / 'Player.log'), '-crash-report-folder', str(output / 'Crashes')]
    if args.capture_position:
        app_args += ['-benchmark-pix-capture-position', str(args.capture_position)]
    command = '"' + str(pix) + '" --log-file="' + str(output / 'pixtool.log') + '" launch "' + str(executable) + '" --command-line="' + subprocess.list2cmdline(app_args) + '"'
    if args.timing:
        command += ' --timing take-new-timing-capture "' + str(output / 'timing.wpix') + '" --until-exit --sample-rate=1000 --no-gpu-memory'
    else:
        command += ' programmatic-capture --until-exit save-all-captures "' + str(output / 'Captures') + '"'
    launch = dict(arguments=command, mainSeries=False, instrumented=True, eligibleForFpsComparison=False, measurementClass='pix-diagnostic', startupActivationOnly=True, startedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat())
    log = (output / 'capture-console.txt').open('w', encoding='utf-8')
    process = subprocess.Popen(command, cwd=pix.parent, stdout=log, stderr=subprocess.STDOUT, creationflags=0x08000000)
    pid = None
    deadline = time.monotonic() + 45
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.restype = ctypes.c_void_p
    kernel.QueryFullProcessImageNameW.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_wchar_p, ctypes.POINTER(ctypes.c_uint)]
    kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    def locate():
        found = []
        def visit(handle, unused):
            owner = ctypes.c_ulong()
            driver.user.GetWindowThreadProcessId(handle, ctypes.byref(owner))
            native = kernel.OpenProcess(0x1000, False, owner.value)
            if native:
                path = ctypes.create_unicode_buffer(32768)
                size = ctypes.c_uint(32768)
                if kernel.QueryFullProcessImageNameW(native, 0, path, ctypes.byref(size)) and Path(path.value) == executable:
                    found.append(owner.value)
                kernel.CloseHandle(native)
            return True
        driver.user.EnumWindows(driver.callback(visit), 0)
        return found[0] if found else None
    while process.poll() is None and time.monotonic() < deadline:
        pid = locate()
        candidates = [v for v in driver.windows(pid) if v['visible'] and v['width'] > 500] if pid else []
        if candidates:
            handle = candidates[0]['handle']
            if not candidates[0]['foreground']:
                driver.user.ShowWindow(handle, 9)
                driver.user.BringWindowToTop(handle)
                driver.user.keybd_event(0x12, 0, 0, 0)
                driver.user.SetForegroundWindow(handle)
                driver.user.keybd_event(0x12, 0, 2, 0)
            launch['startupWindow'] = driver.windows(pid)
            break
        time.sleep(.1)
    launch['pid'] = pid
    driver.save(output / 'launch.json', launch)
    try:
        result = process.wait(timeout=180)
    except subprocess.TimeoutExpired:
        process.terminate()
        process.wait(timeout=15)
        driver.save(output / 'driver-failure.json', dict(reason='Diagnostic launch timed out; inspect owned Player before another run.', pid=pid))
        raise
    finally:
        log.close()
    driver.save(output / 'exit.json', dict(toolCode=result, endedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat()))
    state = json.loads((output / 'result.json').read_text(encoding='utf-8-sig')) if (output / 'result.json').exists() else {}
    valid = state.get('status') == 'diagnostic-completed' and state.get('instrumented') and state.get('eligibleForFpsComparison') is False
    captures = list(output.rglob('*.wpix'))
    capture_saved = not (args.capture_position or args.timing) or bool(captures)
    driver.save(output / 'capture-export.json', dict(files=[str(x) for x in captures], structuralRequested=bool(args.capture_position), timingRequested=args.timing, fileSaved=capture_saved, suitabilityVerified=False))
    print(json.dumps(dict(run=args.label, pid=pid, toolExit=result, diagnosticCompleted=valid, captureFiles=len(captures))))
    return 0 if valid and result == 0 and capture_saved else 2

if __name__ == '__main__':
    raise SystemExit(main())
