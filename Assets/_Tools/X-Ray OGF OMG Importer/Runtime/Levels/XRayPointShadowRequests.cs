using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRay.Importer.Levels
{
    internal sealed class XRayPointShadowRequests : IDisposable
    {
        private const int MAX_SLOTS = 32;

        private readonly List<Slot> _slots = new List<Slot>(MAX_SLOTS);

        private NativeArray<XRayPointShadowJob.Node> _nodes;
        private NativeArray<int> _order;
        private NativeArray<Bounds> _bounds;
        private NativeArray<XRayPointShadowJob.Draw> _draws;
        private NativeArray<XRayPointShadowJob.Source> _sources;
        private JobHandle _retiredSources;

        internal long MemoryBytes
        {
            get
            {
                long shared = (long)_nodes.Length * UnsafeUtility.SizeOf<XRayPointShadowJob.Node>() + (long)_order.Length * sizeof(int) + (long)_bounds.Length * UnsafeUtility.SizeOf<Bounds>() + (long)_draws.Length * UnsafeUtility.SizeOf<XRayPointShadowJob.Draw>() + (long)_sources.Length * UnsafeUtility.SizeOf<XRayPointShadowJob.Source>();
                long perSlot = (long)_sources.Length * sizeof(ushort) + (long)_bounds.Length * sizeof(int) + (long)_draws.Length * UnsafeUtility.SizeOf<BatchDrawCommand>() + UnsafeUtility.SizeOf<XRayPointShadowJob.Result>();
                return shared + _slots.Count * perSlot;
            }
        }

        internal XRayPointShadowRequests(XRayInstanceVisibilityIndex index, Bounds[] bounds, NativeArray<XRayPointShadowJob.Draw> draws, NativeArray<XRayPointShadowJob.Source> sources)
        {
            _nodes = new NativeArray<XRayPointShadowJob.Node>(index.PointShadowNodeCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _order = new NativeArray<int>(sources.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _bounds = new NativeArray<Bounds>(bounds, Allocator.Persistent);
            _draws = draws;
            _sources = sources;
            index.CopyPointShadowGeometry(_nodes, _order);
        }

        internal void ReplaceSources(NativeArray<XRayPointShadowJob.Source> sources)
        {
            JobHandle readers = default;
            foreach (Slot slot in _slots)
            {
                if (slot.Active) readers = JobHandle.CombineDependencies(readers, slot.Fence);
            }
            JobHandle retired = _sources.Dispose(readers);
            _retiredSources = JobHandle.CombineDependencies(_retiredSources, retired);
            _sources = sources;
        }

        internal bool TrySchedule(BatchCullingContext context, BatchCullingOutput output, BatchID batchId, byte layer, uint renderingLayerMask, XRayPointShadowWind wind, int epoch, bool probeBurst, out JobHandle fence)
        {
            Slot slot = null;
            foreach (Slot next in _slots)
            {
                if (next.Active) continue;
                slot = next;
                break;
            }
            if (slot == null && _slots.Count < MAX_SLOTS)
            {
                slot = new Slot(_sources.Length, _bounds.Length, _draws.Length);
                _slots.Add(slot);
            }
            if (slot == null)
            {
                fence = default;
                return false;
            }
            fence = new XRayPointShadowJob
            {
                Nodes = _nodes,
                Order = _order,
                InstanceBounds = _bounds,
                Sources = _sources,
                Draws = _draws,
                Planes = context.cullingPlanes,
                Splits = context.cullingSplits,
                Output = output.drawCommands,
                SourceMasks = slot.Masks,
                Visible = slot.Visible,
                Planned = slot.Commands,
                Statistics = slot.Statistics,
                BatchId = batchId,
                Layer = layer,
                RenderingLayerMask = renderingLayerMask,
                Wind = wind,
                ProbeBurst = probeBurst
            }.Schedule();
            slot.Fence = fence;
            slot.Epoch = epoch;
            slot.Active = true;
            return true;
        }

        internal void Collect(int epoch, out XRayPointShadowJob.Result total, out int jobs, out int burst)
        {
            total = default;
            jobs = 0;
            burst = 0;
            foreach (Slot slot in _slots)
            {
                if (!slot.Active || !slot.Fence.IsCompleted) continue;
                slot.Fence.Complete();
                slot.Active = false;
                var result = slot.Statistics[0];
                jobs++;
                burst += result.Burst;
                if (slot.Epoch != epoch) continue;
                total.Nodes += result.Nodes;
                total.Commands += result.Commands;
                total.Instances += result.Instances;
                total.Bytes += result.Bytes;
            }
            if (_retiredSources.IsCompleted)
            {
                _retiredSources.Complete();
                _retiredSources = default;
            }
        }

        public void Dispose()
        {
            foreach (Slot slot in _slots)
            {
                if (slot.Active) slot.Fence.Complete();
                slot.Dispose();
            }
            _slots.Clear();
            _retiredSources.Complete();
            _nodes.Dispose();
            _order.Dispose();
            _bounds.Dispose();
            _draws.Dispose();
            _sources.Dispose();
        }

        private sealed class Slot : IDisposable
        {
            internal NativeArray<ushort> Masks { get; private set; }
            internal NativeArray<int> Visible { get; private set; }
            internal NativeArray<BatchDrawCommand> Commands { get; private set; }
            internal NativeArray<XRayPointShadowJob.Result> Statistics { get; private set; }
            internal JobHandle Fence { get; set; }
            internal int Epoch { get; set; }
            internal bool Active { get; set; }

            internal Slot(int sources, int instances, int draws)
            {
                Masks = new NativeArray<ushort>(sources, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                Visible = new NativeArray<int>(instances, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                Commands = new NativeArray<BatchDrawCommand>(draws, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                Statistics = new NativeArray<XRayPointShadowJob.Result>(1, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }

            public void Dispose()
            {
                Masks.Dispose();
                Visible.Dispose();
                Commands.Dispose();
                Statistics.Dispose();
            }
        }
    }
}
