import json
import os
import sys

import addon_utils
import bpy


SUBFRAMES = (0.25, 0.5, 0.75)


def matrix_to_list(matrix):
    return [matrix[r][c] for r in range(4) for c in range(4)]


def clear_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)


def export(path, output_folder):
    clear_scene()
    try:
        result = bpy.ops.xray_import.anm(directory=os.path.dirname(path), files=[{'name': os.path.basename(path)}], camera_animation=True)
    except RuntimeError as error:
        result = {'FAILED_AFTER_IMPORT: ' + str(error).splitlines()[-1]}
    motion = next(obj for obj in bpy.data.objects if obj.parent is None)
    camera = next((obj for obj in bpy.data.objects if obj.parent == motion), None)
    action = motion.animation_data.action
    fcurves = sorted(action.fcurves, key=lambda fc: (fc.data_path, fc.array_index))
    first = min(fc.keyframe_points[0].co.x for fc in fcurves if len(fc.keyframe_points))
    last = max(fc.keyframe_points[-1].co.x for fc in fcurves if len(fc.keyframe_points))
    scene = bpy.context.scene
    samples = []
    frame = int(round(first))
    while frame <= int(round(last)):
        for subframe in (0.0,) + (SUBFRAMES if frame < int(round(last)) else ()):
            scene.frame_set(frame, subframe=subframe)
            value = frame + subframe
            samples.append({
                'frame': value,
                'fcurves': [fc.evaluate(value) for fc in fcurves],
                'fcurve_paths': [fc.data_path + '[' + str(fc.array_index) + ']' for fc in fcurves],
                'basis': matrix_to_list(motion.matrix_basis),
                'world': matrix_to_list(motion.matrix_world),
                'camera_world': matrix_to_list(camera.matrix_world) if camera else None,
            })
        frame += 1
    data = {
        'file': os.path.basename(path),
        'blender_version': bpy.app.version_string,
        'addon_version': '.'.join(str(v) for v in addon_utils.module_bl_info(sys.modules['io_scene_xray'])['version']),
        'operator_result': list(result),
        'fps': action.xray.fps,
        'scene_frame_start': scene.frame_start,
        'scene_frame_end': scene.frame_end,
        'rotation_mode': motion.rotation_mode,
        'keys': {fc.data_path + '[' + str(fc.array_index) + ']': [[k.co.x, k.co.y, k.interpolation] for k in fc.keyframe_points] for fc in fcurves},
        'extrapolation': {fc.data_path + '[' + str(fc.array_index) + ']': fc.extrapolation for fc in fcurves},
        'samples': samples,
    }
    target = os.path.join(output_folder, os.path.basename(path) + '.json')
    with open(target, 'w', encoding='utf-8') as stream:
        json.dump(data, stream)
    print('reference written:', target, len(samples), 'samples')


def main():
    argv = sys.argv[sys.argv.index('--') + 1:]
    output_folder = argv[0]
    os.makedirs(output_folder, exist_ok=True)
    addon_utils.enable('io_scene_xray', default_set=True)
    for path in argv[1:]:
        try:
            export(path, output_folder)
        except Exception as exception:
            print('reference failed:', path, repr(exception))


main()
