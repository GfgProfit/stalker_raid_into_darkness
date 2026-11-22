using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRay.Importer.Levels
{
    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict)]
    internal unsafe struct XRayPointShadowJob : IJob
    {
        internal const int MAX_PLANES = 128;

        private const int STACK_CAPACITY = 32;
        private const float PLANE_ROUNDING_MARGIN = 0.00001f;

        private static readonly ProfilerMarker EXECUTE_MARKER = new ProfilerMarker("XRay.PointJobs.Execute");
        private static readonly ProfilerMarker TRAVERSE_MARKER = new ProfilerMarker("XRay.PointJobs.Traverse");
        private static readonly ProfilerMarker REFINE_AND_PACK_MARKER = new ProfilerMarker("XRay.PointJobs.RefineAndPack");
        private static readonly ProfilerMarker PUBLISH_MARKER = new ProfilerMarker("XRay.PointJobs.Publish");

        [ReadOnly] internal NativeArray<Node> Nodes;
        [ReadOnly] internal NativeArray<int> Order;
        [ReadOnly] internal NativeArray<Source> Sources;
        [ReadOnly] internal NativeArray<Draw> Draws;
        [ReadOnly] internal NativeArray<Bounds> InstanceBounds;
        [ReadOnly] internal NativeArray<Plane> Planes;
        [ReadOnly] internal NativeArray<CullingSplit> Splits;
        internal NativeArray<ushort> SourceMasks;
        internal NativeArray<int> Visible;
        internal NativeArray<BatchDrawCommand> Planned;
        [WriteOnly] internal NativeArray<Result> Statistics;
        [WriteOnly] internal NativeArray<BatchCullingOutputDrawCommands> Output;
        internal BatchID BatchId;
        internal byte Layer;
        internal uint RenderingLayerMask;
        internal XRayPointShadowWind Wind;
        internal bool ProbeBurst;

        public void Execute()
        {
            using (EXECUTE_MARKER.Auto())
            {
                Output[0] = default;
                var result = new Result { Burst = ProbeBurst && X86.Sse2.IsSse2Supported ? 1 : 0 };
                PackedPlane* planes = stackalloc PackedPlane[MAX_PLANES];
                int splitCount = Splits.Length;
                ClipRange* ranges = stackalloc ClipRange[splitCount];
                using (TRAVERSE_MARKER.Auto())
                {
                    UnsafeUtility.MemClear(SourceMasks.GetUnsafePtr(), (long)SourceMasks.Length * sizeof(ushort));
                    PackPlanes(planes, ranges, splitCount);
                    result.Nodes = Traverse(planes, ranges, splitCount);
                }
                int visibleCount;
                using (REFINE_AND_PACK_MARKER.Auto())
                {
                    visibleCount = RefineAndPack(planes, ranges, splitCount, ref result);
                }
                using (PUBLISH_MARKER.Auto())
                {
                    result.Bytes = Publish(result.Commands, visibleCount);
                    Statistics[0] = result;
                }
            }
        }

        private void PackPlanes(PackedPlane* planes, ClipRange* ranges, int splitCount)
        {
            for (int split = 0; split < splitCount; split++)
            {
                CullingSplit item = Splits[split];
                int end = item.cullingPlaneOffset + item.cullingPlaneCount;
                ranges[split] = new ClipRange { Start = item.cullingPlaneOffset, End = end };
                for (int index = item.cullingPlaneOffset; index < end; index++)
                {
                    Plane plane = Planes[index];
                    Vector3 normal = plane.normal;
                    planes[index] = new PackedPlane { Normal = normal, AbsoluteNormal = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z)), Distance = plane.distance };
                }
            }
        }

        private int Traverse(PackedPlane* planes, ClipRange* ranges, int splitCount)
        {
            if (Nodes.Length == 0)
            {
                return 0;
            }
            Traversal* stack = stackalloc Traversal[STACK_CAPACITY];
            stack[0] = new Traversal { Index = 0, Pending = (ushort)((1 << splitCount) - 1) };
            int count = 1;
            int visited = 0;
            while (count != 0)
            {
                Traversal item = stack[--count];
                Node node = Nodes[item.Index];
                ushort pending = item.Pending;
                ushort inside = item.Inside;
                visited++;
                Bounds bounds = Wind.Expand(node.Bounds, node.SourceHeight);
                if (node.Invalid || !FiniteBounds(bounds))
                {
                    inside |= pending;
                    pending = 0;
                }
                else
                {
                    Vector3 center = bounds.center;
                    Vector3 extents = bounds.extents;
                    for (int split = 0; split < splitCount; split++)
                    {
                        ushort bit = (ushort)(1 << split);
                        if ((pending & bit) == 0)
                        {
                            continue;
                        }
                        int visibility = Classify(center, extents, planes, ranges[split]);
                        if (visibility == 0)
                        {
                            continue;
                        }
                        pending &= (ushort)~bit;
                        if (visibility > 0)
                        {
                            inside |= bit;
                        }
                    }
                }
                if (pending != 0 && node.Count > 1)
                {
                    stack[count++] = new Traversal { Index = node.Right, Pending = pending, Inside = inside };
                    stack[count++] = new Traversal { Index = node.Left, Pending = pending, Inside = inside };
                    continue;
                }
                ushort accepted = (ushort)(pending | inside);
                if (accepted == 0)
                {
                    continue;
                }
                int end = node.Start + node.Count;
                for (int offset = node.Start; offset < end; offset++)
                {
                    SourceMasks[Order[offset]] = accepted;
                }
            }
            return visited;
        }

        private int RefineAndPack(PackedPlane* planes, ClipRange* ranges, int splitCount, ref Result result)
        {
            int visibleCount = 0;
            for (int sourceIndex = 0; sourceIndex < Sources.Length; sourceIndex++)
            {
                ushort mask = SourceMasks[sourceIndex];
                if (mask == 0)
                {
                    continue;
                }
                Source source = Sources[sourceIndex];
                if (!source.CastShadows)
                {
                    SourceMasks[sourceIndex] = 0;
                    continue;
                }
                if (source.DrawCount == 0)
                {
                    continue;
                }

                int first = visibleCount;
                if (source.Count == 1)
                {
                    Visible[visibleCount++] = source.Start;
                }
                else
                {
                    int end = source.Start + source.Count;
                    for (int index = source.Start; index < end; index++)
                    {
                        Bounds bounds = InstanceBounds[index];
                        if (source.Wind)
                        {
                            bounds = Wind.Expand(bounds, source.Height);
                        }
                        Vector3 center = bounds.center;
                        Vector3 extents = bounds.extents;
                        for (int split = 0; split < splitCount; split++)
                        {
                            if ((mask & (1 << split)) != 0 && Intersects(center, extents, planes, ranges[split]))
                            {
                                Visible[visibleCount++] = index;
                                break;
                            }
                        }
                    }
                }
                int count = visibleCount - first;
                if (count == 0)
                {
                    continue;
                }
                result.Instances += count * source.DrawCount;
                int drawEnd = source.DrawStart + source.DrawCount;
                for (int drawIndex = source.DrawStart; drawIndex < drawEnd; drawIndex++)
                {
                    Draw draw = Draws[drawIndex];
                    if (result.Commands > 0)
                    {
                        int previousIndex = result.Commands - 1;
                        BatchDrawCommand previous = Planned[previousIndex];
                        if (previous.visibleOffset + previous.visibleCount == (uint)first && previous.meshID.value == draw.MeshId.value && previous.materialID.value == draw.MaterialId.value && previous.submeshIndex == draw.Submesh && previous.splitVisibilityMask == mask)
                        {
                            previous.visibleCount += (uint)count;
                            Planned[previousIndex] = previous;
                            continue;
                        }
                    }
                    Planned[result.Commands++] = new BatchDrawCommand { batchID = BatchId, materialID = draw.MaterialId, meshID = draw.MeshId, submeshIndex = draw.Submesh, visibleOffset = (uint)first, visibleCount = (uint)count, splitVisibilityMask = mask };
                }
            }
            return visibleCount;
        }

        private long Publish(int commandCount, int visibleCount)
        {
            if (commandCount == 0)
            {
                return 0;
            }
            long commandBytes = (long)UnsafeUtility.SizeOf<BatchDrawCommand>() * commandCount;
            long indexBytes = (long)sizeof(int) * visibleCount;
            int rangeBytes = UnsafeUtility.SizeOf<BatchDrawRange>();
            var commands = new BatchCullingOutputDrawCommands
            {
                drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(commandBytes, UnsafeUtility.AlignOf<BatchDrawCommand>(), Allocator.TempJob),
                visibleInstances = (int*)UnsafeUtility.Malloc(indexBytes, UnsafeUtility.AlignOf<int>(), Allocator.TempJob),
                drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(rangeBytes, UnsafeUtility.AlignOf<BatchDrawRange>(), Allocator.TempJob),
                drawCommandCount = commandCount,
                visibleInstanceCount = visibleCount,
                drawRangeCount = 1
            };
            UnsafeUtility.MemCpy(commands.drawCommands, Planned.GetUnsafeReadOnlyPtr(), commandBytes);
            UnsafeUtility.MemCpy(commands.visibleInstances, Visible.GetUnsafeReadOnlyPtr(), indexBytes);
            var filter = new BatchFilterSettings { renderingLayerMask = RenderingLayerMask, layer = Layer, receiveShadows = true, shadowCastingMode = ShadowCastingMode.ShadowsOnly, motionMode = MotionVectorGenerationMode.Camera, staticShadowCaster = false, allDepthSorted = false, sceneCullingMask = ulong.MaxValue };
            commands.drawRanges[0] = new BatchDrawRange { drawCommandsBegin = 0, drawCommandsCount = (uint)commandCount, drawCommandsType = BatchDrawCommandType.Direct, filterSettings = filter };
            Output[0] = commands;
            return commandBytes + indexBytes + rangeBytes;
        }

        private static int Classify(Vector3 center, Vector3 extents, PackedPlane* planes, ClipRange range)
        {
            bool inside = true;
            for (int index = range.Start; index < range.End; index++)
            {
                ref readonly PackedPlane plane = ref planes[index];
                float distance = Vector3.Dot(plane.Normal, center) + plane.Distance;
                float radius = plane.AbsoluteNormal.x * extents.x + plane.AbsoluteNormal.y * extents.y + plane.AbsoluteNormal.z * extents.z;
                if (!float.IsFinite(distance) || !float.IsFinite(radius)) return 0;
                float margin = PLANE_ROUNDING_MARGIN * (1f + Mathf.Abs(distance) + radius);
                if (distance < -radius - margin) return -1;
                if (distance < radius + margin) inside = false;
            }
            return inside ? 1 : 0;
        }

        private static bool Intersects(Vector3 center, Vector3 extents, PackedPlane* planes, ClipRange range)
        {
            for (int index = range.Start; index < range.End; index++)
            {
                ref readonly PackedPlane plane = ref planes[index];
                float radius = plane.AbsoluteNormal.x * extents.x + plane.AbsoluteNormal.y * extents.y + plane.AbsoluteNormal.z * extents.z;
                float distance = Vector3.Dot(plane.Normal, center) + plane.Distance + radius;
                if (!float.IsFinite(distance)) return true;
                if (distance < -0.00001f) return false;
            }
            return true;
        }

        private static bool FiniteBounds(Bounds bounds)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            return Finite(center) && Finite(extents) && extents.x >= 0f && extents.y >= 0f && extents.z >= 0f && Finite(center - extents) && Finite(center + extents);
        }

        private static bool Finite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        internal struct Node
        {
            internal Bounds Bounds;
            internal float SourceHeight;
            internal int Left;
            internal int Right;
            internal int Start;
            internal int Count;
            internal bool Invalid;
        }

        internal struct Source
        {
            internal int Start;
            internal int Count;
            internal int DrawStart;
            internal int DrawCount;
            internal float Height;
            internal bool CastShadows;
            internal bool Wind;
        }

        internal struct Draw
        {
            internal BatchMeshID MeshId;
            internal BatchMaterialID MaterialId;
            internal ushort Submesh;
        }

        internal struct Result
        {
            internal int Nodes;
            internal int Commands;
            internal int Instances;
            internal long Bytes;
            internal int Burst;
        }

        private struct PackedPlane
        {
            internal Vector3 Normal;
            internal Vector3 AbsoluteNormal;
            internal float Distance;
        }

        private struct ClipRange
        {
            internal int Start;
            internal int End;
        }

        private struct Traversal
        {
            internal int Index;
            internal ushort Pending;
            internal ushort Inside;
        }
    }
}
