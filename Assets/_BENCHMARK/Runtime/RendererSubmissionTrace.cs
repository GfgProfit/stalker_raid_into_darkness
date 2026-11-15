using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Rvm.Benchmark
{
    public static class RendererSubmissionTrace
    {
        public static bool Enabled { get; set; }
        public static List<string> Entries { get; } = new List<string>();

        public static void Draw<T>(RenderParams parameters, Mesh mesh, int submesh, T[] matrices, int count, int start) where T : struct
        {
            if (!Enabled) return;
            var text = new StringBuilder();
            text.Append(mesh.GetEntityId().ToString()).Append('|').Append(parameters.material.GetEntityId().ToString()).Append('|').Append(submesh).Append('|').Append(count).Append('|').Append(start).Append('|').Append(parameters.camera.GetEntityId().ToString()).Append('|').Append(parameters.layer).Append('|').Append(parameters.motionVectorMode).Append('|').Append(parameters.shadowCastingMode).Append('|').Append(parameters.receiveShadows);
            Vector3 center = parameters.worldBounds.center;
            Vector3 size = parameters.worldBounds.size;
            foreach (float value in new[] { center.x, center.y, center.z, size.x, size.y, size.z }) Append(text, value);
            var current = typeof(T).GetField("objectToWorld", BindingFlags.Public | BindingFlags.Instance);
            var previous = typeof(T).GetField("prevObjectToWorld", BindingFlags.Public | BindingFlags.Instance);
            for (int i = start; i < start + count; i++)
            {
                object item = matrices[i];
                Matrix4x4 now = item is Matrix4x4 raw ? raw : (Matrix4x4)current.GetValue(item);
                Matrix4x4 before = previous == null ? now : (Matrix4x4)previous.GetValue(item);
                for (int component = 0; component < 16; component++)
                {
                    Append(text, now[component]);
                    Append(text, before[component]);
                }
            }
            Vector4[] data = parameters.matProps.GetVectorArray(XRay.Importer.Levels.XRayLevelInstancedRenderer.INSTANCE_DATA_PROPERTY);
            text.Append('|').Append(data == null ? -1 : data.Length);
            if (data != null)
            {
                foreach (Vector4 item in data)
                {
                    for (int component = 0; component < 4; component++) Append(text, item[component]);
                }
            }
            Entries.Add(text.ToString());
        }

        public static string[] Payload(Array instances)
        {
            var result = new string[instances.Length];
            for (int i = 0; i < instances.Length; i++)
            {
                object item = instances.GetValue(i);
                var text = new StringBuilder();
                foreach (string field in new[] { "objectToWorld", "prevObjectToWorld" })
                {
                    Matrix4x4 matrix = (Matrix4x4)item.GetType().GetField(field).GetValue(item);
                    for (int component = 0; component < 16; component++) Append(text, matrix[component]);
                }
                result[i] = text.ToString();
            }
            return result;
        }

        private static void Append(StringBuilder text, float value)
        {
            text.Append('|').Append(BitConverter.SingleToInt32Bits(value));
        }
    }
}


