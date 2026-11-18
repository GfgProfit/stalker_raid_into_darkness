using System;
using System.Collections.Generic;
using System.Reflection;
using RaidIntoDarkness.Effects;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Rvm.CleanReleaseRoute07
{
    public static class PunctualShadowDistanceInventory
    {
        private const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static string Capture(Camera camera, PunctualShadowDistanceTrial trial = null)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            var report = new Report { frame = Time.frameCount, time = Time.timeAsDouble, camera = camera.name, cameraEntityId = camera.GetEntityId().ToString(), cameraPosition = camera.transform.position, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), unity = Application.unityVersion, trackingCheckAvailable = trial != null };
            var idleLights = new HashSet<Light>();
            FieldInfo idleField = typeof(AnomalyVisual).GetField("_idleIllumination", FLAGS);
            if (idleField == null) report.errors.Add("Anomaly idle ownership field is unavailable.");
            else
            {
                foreach (var visual in UnityEngine.Object.FindObjectsByType<AnomalyVisual>(FindObjectsInactive.Include))
                {
                    var light = idleField.GetValue(visual) as Light;
                    if (light != null) idleLights.Add(light);
                }
            }
            object pipeline = RenderPipelineManager.currentPipeline;
            Type databaseType = typeof(HDRenderPipeline).Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDLightRenderDatabase");
            object database = databaseType != null ? databaseType.GetProperty("instance", FLAGS)?.GetValue(null) : null;
            MethodInfo dataIndexMethod = databaseType?.GetMethod("GetEntityDataIndex", FLAGS);
            var byDataIndex = new Dictionary<int, LightRecord>();
            var byEntityId = new Dictionary<string, LightRecord>();
            var managedLights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
            report.initialManagedLightCount = managedLights.Length;
            foreach (var light in managedLights)
            {
                bool punctual = light.type == LightType.Point || light.type == LightType.Spot;
                bool directional = light.type == LightType.Directional;
                if (!punctual && !directional)
                {
                    report.otherLightComponents++;
                    continue;
                }
                var hd = light.GetComponent<HDAdditionalLightData>();
                var record = CaptureLight(light, hd, camera, trial, idleLights);
                record.initialManagedSnapshot = true;
                byEntityId.Add(record.entityId, record);
                if (record.potentialPunctualShadowSource && trial != null && !record.tracked) report.potentialUntrackedSources.Add(record.entityId);
                if (hd != null)
                {
                    try
                    {
                        object entity = Get(hd, "lightEntity");
                        record.hdEntityValid = (bool)Get(entity, "valid");
                        if (record.hdEntityValid && database != null && dataIndexMethod != null)
                        {
                            record.dataIndex = Convert.ToInt32(dataIndexMethod.Invoke(database, new[] { entity }));
                            if (record.dataIndex >= 0)
                            {
                                if (!byDataIndex.ContainsKey(record.dataIndex)) byDataIndex.Add(record.dataIndex, record);
                                else record.entityError = "More than one component maps to this HDRP data index.";
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        record.entityError = Message(exception);
                    }
                }
                if (directional) report.directionals.Add(record);
                else report.punctualLights.Add(record);
                if (record.enabled && record.active && record.hasHdrp && (!record.hdEntityValid || record.dataIndex < 0 || !string.IsNullOrEmpty(record.entityError))) report.activeSourceMappingWarnings.Add(record.entityId + ": " + (record.entityError ?? "Active component has no valid mapped HDRP entity."));
            }
            if (pipeline == null || database == null || dataIndexMethod == null) report.errors.Add("Active HDRP pipeline/light database is unavailable.");
            else
            {
                try
                {
                    HDShadowSettings settings = HDCamera.GetOrCreate(camera).volumeStack.GetComponent<HDShadowSettings>();
                    if (settings == null) throw new InvalidOperationException("The rendered camera has no HDShadowSettings.");
                    report.globalMaxShadowDistance = settings.maxShadowDistance.value;
                    report.globalShadowDistanceAvailable = true;
                }
                catch (Exception exception)
                {
                    report.globalShadowDistanceReason = Message(exception);
                }
                try
                {
                    object processed = Get(pipeline, "m_ProcessedLightsBuilder");
                    object gpu = Get(pipeline, "m_GpuLightsBuilder");
                    object keys = Get(processed, "sortKeys");
                    object entities = Get(processed, "processedEntities");
                    object gpuLights = Get(gpu, "lights");
                    report.sortedLightCount = Convert.ToInt32(Get(processed, "sortedLightCounts"));
                    report.sortedDirectionalCount = Convert.ToInt32(Get(processed, "sortedDirectionalLightCounts"));
                    int gpuCount = Count(gpuLights);
                    for (int index = 0; index < report.sortedLightCount; index++)
                    {
                        uint key = Convert.ToUInt32(Item(keys, index));
                        int visibleIndex = (int)(key & 0xffff);
                        object entity = Item(entities, visibleIndex);
                        int dataIndex = Convert.ToInt32(Get(entity, "dataIndex"));
                        string processedLightType = Get(entity, "lightType").ToString();
                        object flags = Get(entity, "shadowMapFlags");
                        bool offscreen = (key & (1u << 30)) != 0;
                        int gpuIndex = index - report.sortedDirectionalCount;
                        bool available = !offscreen && gpuIndex >= 0 && gpuIndex < gpuCount;
                        object lightData = available ? Item(gpuLights, gpuIndex) : null;
                        byDataIndex.TryGetValue(dataIndex, out var source);
                        string resolutionError = null;
                        bool processedPunctual = processedLightType == LightType.Point.ToString() || processedLightType == LightType.Spot.ToString();
                        bool processedDirectional = processedLightType == LightType.Directional.ToString();
                        if (source == null && (processedPunctual || processedDirectional))
                        {
                            try
                            {
                                if (dataIndex < 0) throw new InvalidOperationException("Processed light has no registered data index.");
                                var hd = ((DynamicArray<HDAdditionalLightData>)Get(database, "hdAdditionalLightData"))[dataIndex];
                                if (hd == null) throw new InvalidOperationException("Processed HDRP registry attachment is unavailable.");
                                var light = Get(hd, "legacyLight") as Light;
                                if (light == null) throw new InvalidOperationException("Processed HDRP registry Light is unavailable.");
                                if (light.type.ToString() != processedLightType) throw new InvalidOperationException("Processed type differs from the registered Light type.");
                                object registeredEntity = Get(hd, "lightEntity");
                                bool valid = (bool)Get(registeredEntity, "valid");
                                int registeredIndex = valid ? Convert.ToInt32(dataIndexMethod.Invoke(database, new[] { registeredEntity })) : -1;
                                if (registeredIndex != dataIndex) throw new InvalidOperationException("Processed registry attachment does not map back to the processed data index.");
                                string entityId = light.GetEntityId().ToString();
                                if (!byEntityId.TryGetValue(entityId, out source))
                                {
                                    source = CaptureLight(light, hd, camera, trial, idleLights);
                                    source.missingFromInitialManagedSnapshot = true;
                                    byEntityId.Add(entityId, source);
                                    report.processedRegistryDiscoveries++;
                                    if (processedDirectional) report.directionals.Add(source);
                                    else report.punctualLights.Add(source);
                                    if (source.potentialPunctualShadowSource && trial != null && !source.tracked) report.potentialUntrackedSources.Add(source.entityId);
                                }
                                source.registryResolved = true;
                                source.hdEntityValid = valid;
                                source.dataIndex = registeredIndex;
                                byDataIndex.Add(dataIndex, source);
                            }
                            catch (Exception exception)
                            {
                                source = null;
                                resolutionError = Message(exception);
                            }
                        }
                        var row = new ProcessedRecord { sortedIndex = index, visibleIndex = visibleIndex, dataIndex = dataIndex, sortKey = key, offscreen = offscreen, processedLightType = processedLightType, sourceMapped = source != null, sourceResolvedFromRegistry = source != null && source.registryResolved, sourceMissingFromInitialManagedSnapshot = source != null && source.missingFromInitialManagedSnapshot, sourceResolutionUnavailable = source == null, sourceResolutionError = resolutionError, sourceEntityId = source?.entityId, sourcePath = source?.path, sourceRole = source?.role, sourceType = source?.type, sourceDistance = source != null ? source.cameraDistance : -1f, sourceTracked = source != null && source.tracked, shadowMapFlags = Convert.ToInt32(flags), shadowMapFlagsName = flags.ToString(), gpuRecordAvailable = available, shadowIndex = available ? Convert.ToInt32(Get(lightData, "shadowIndex")) : -2, contactShadowMask = available ? Convert.ToInt32(Get(lightData, "contactShadowMask")) : 0, gpuLightColor = available ? (Vector3)Get(lightData, "color") : Vector3.zero };
                        if (source == null) report.unmappedProcessedRecords++;
                        if (source == null && processedPunctual) report.unresolvedProcessedPunctualRecords++;
                        if (source == null && processedDirectional) report.unresolvedProcessedDirectionalRecords++;
                        if (row.sourceResolvedFromRegistry) report.registryResolvedProcessedRecords++;
                        if (row.gpuRecordAvailable && row.shadowIndex >= 0) report.processedShadowBindings++;
                        report.processedLights.Add(row);
                    }
                    report.processedInventoryAvailable = true;
                }
                catch (Exception exception)
                {
                    report.errors.Add(Message(exception));
                }
                try
                {
                    object manager = Get(pipeline, "m_ShadowManager");
                    MethodInfo method = manager.GetType().GetMethod("GetShadowRequestCount", BindingFlags.Public | BindingFlags.Instance);
                    if (method == null) throw new MissingMethodException(manager.GetType().FullName, "GetShadowRequestCount");
                    report.shadowRequestCount = Convert.ToInt32(method.Invoke(manager, null));
                    report.shadowRequestCountAvailable = true;
                }
                catch (Exception exception)
                {
                    report.shadowRequestCountReason = Message(exception);
                }
            }
            report.apiSnapshotSucceeded = report.errors.Count == 0 && report.processedInventoryAvailable;
            report.sourceCoverageComplete = report.apiSnapshotSucceeded && report.unmappedProcessedRecords == 0 && report.activeSourceMappingWarnings.Count == 0;
            report.complete = report.apiSnapshotSucceeded && report.sourceCoverageComplete;
            return JsonUtility.ToJson(report, true);
        }

        private static LightRecord CaptureLight(Light light, HDAdditionalLightData hd, Camera camera, PunctualShadowDistanceTrial trial, HashSet<Light> idleLights)
        {
            var anomaly = light.GetComponentInParent<AnomalyVisual>(true);
            var record = new LightRecord { entityId = light.GetEntityId().ToString(), path = Hierarchy(light.transform), role = idleLights.Contains(light) ? "AnomalyIdle" : anomaly != null ? "AnomalyOther" : "Ordinary", type = light.type.ToString(), enabled = light.enabled, active = light.gameObject.activeInHierarchy, shadows = light.shadows.ToString(), range = light.range, intensity = light.intensity, color = light.color, lightUnit = light.lightUnit.ToString(), position = light.transform.position, cameraDistance = Vector3.Distance(camera.transform.position, light.transform.position), cullingMask = light.cullingMask, renderingLayerMask = light.renderingLayerMask, shadowNearPlane = light.shadowNearPlane, shadowStrength = light.shadowStrength, hasHdrp = hd != null, tracked = trial != null && trial.Tracks(light) };
            record.within75m = record.cameraDistance <= 75f;
            record.potentialPunctualShadowSource = (light.type == LightType.Point || light.type == LightType.Spot) && hd != null && light.shadows != LightShadows.None;
            if (hd != null)
            {
                record.shadowFadeDistance = hd.shadowFadeDistance;
                record.shadowDimmer = hd.shadowDimmer;
                record.volumetricShadowDimmer = hd.volumetricShadowDimmer;
                record.lightFadeDistance = hd.fadeDistance;
                record.lightDimmer = hd.lightDimmer;
                record.volumetricDimmer = hd.volumetricDimmer;
                record.shadowUpdateMode = hd.shadowUpdateMode.ToString();
            }
            return record;
        }

        private static object Get(object value, string name)
        {
            if (value == null) throw new InvalidOperationException("Missing HDRP object for member: " + name);
            FieldInfo field = value.GetType().GetField(name, FLAGS);
            if (field != null) return field.GetValue(value);
            PropertyInfo property = value.GetType().GetProperty(name, FLAGS);
            if (property == null) throw new MissingMemberException(value.GetType().FullName, name);
            return property.GetValue(value);
        }

        private static int Count(object value)
        {
            if (value is Array array) return array.Length;
            PropertyInfo property = value.GetType().GetProperty("Count", FLAGS) ?? value.GetType().GetProperty("Length", FLAGS);
            if (property == null) throw new MissingMemberException(value.GetType().FullName, "Count/Length");
            return Convert.ToInt32(property.GetValue(value));
        }

        private static object Item(object value, int index)
        {
            if (value is Array array) return array.GetValue(index);
            PropertyInfo property = value.GetType().GetProperty("Item", FLAGS);
            if (property == null) throw new MissingMemberException(value.GetType().FullName, "Item");
            return property.GetValue(value, new object[] { index });
        }

        private static string Hierarchy(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        private static string Message(Exception exception)
        {
            if (exception is TargetInvocationException invocation && invocation.InnerException != null) exception = invocation.InnerException;
            return exception.GetType().Name + ": " + exception.Message;
        }

        [Serializable]
        private sealed class Report
        {
            public bool complete;
            public string completeMeaning = "Both API snapshot and source identity coverage succeeded. Trial policy coverage and executed GPU work are separate and are not certified by this flag.";
            public bool apiSnapshotSucceeded;
            public bool sourceCoverageComplete;
            public string sourceCoverageMeaning = "Every processed sorted light row maps to a source identity and there are no active initial component mapping warnings. Registry discoveries are included; identity resolution does not mean the trial tracks or governs a source.";
            public bool detailedTechnicalObserver = true;
            public bool eligibleForFpsComparison;
            public int frame;
            public double time;
            public string camera;
            public string cameraEntityId;
            public Vector3 cameraPosition;
            public string graphicsApi;
            public string unity;
            public bool trackingCheckAvailable;
            public string potentialUntrackedMeaning = "Potential component sources only; cross-check trial snapshots for original zero fade/dimmer eligibility. Current component values alone do not prove rendered shadow work.";
            public int otherLightComponents;
            public int initialManagedLightCount;
            public int processedRegistryDiscoveries;
            public int registryResolvedProcessedRecords;
            public bool processedInventoryAvailable;
            public int sortedLightCount;
            public int sortedDirectionalCount;
            public int unmappedProcessedRecords;
            public int unresolvedProcessedPunctualRecords;
            public int unresolvedProcessedDirectionalRecords;
            public int processedShadowBindings;
            public bool globalShadowDistanceAvailable;
            public float globalMaxShadowDistance = -1f;
            public string globalShadowDistanceReason;
            public bool shadowRequestCountAvailable;
            public int shadowRequestCount = -1;
            public string shadowRequestCountReason;
            public string shadowRequestCountMeaning = "Installed public GetShadowRequestCount snapshot; total requests include different light types and do not measure executed GPU maps or draws.";
            public bool actualRenderedShadowMapsAvailable;
            public int actualRenderedShadowMaps = -1;
            public int actualDynamicShadowMapUpdates = -1;
            public int actualCachedShadowMapUpdates = -1;
            public string actualRenderedShadowMapsReason = "No validated public per-map executed render/cache-update statistics are exposed here. GPU bindings and request count are retained separately; unavailable is not zero.";
            public List<string> errors = new List<string>();
            public List<string> potentialUntrackedSources = new List<string>();
            public List<string> activeSourceMappingWarnings = new List<string>();
            public List<LightRecord> punctualLights = new List<LightRecord>();
            public List<LightRecord> directionals = new List<LightRecord>();
            public List<ProcessedRecord> processedLights = new List<ProcessedRecord>();
        }

        [Serializable]
        private sealed class LightRecord
        {
            public string entityId;
            public string path;
            public string role;
            public string type;
            public bool enabled;
            public bool active;
            public string shadows;
            public float range;
            public float intensity;
            public Color color;
            public string lightUnit;
            public Vector3 position;
            public float cameraDistance;
            public bool within75m;
            public int cullingMask;
            public int renderingLayerMask;
            public float shadowNearPlane;
            public float shadowStrength;
            public bool hasHdrp;
            public bool tracked;
            public bool initialManagedSnapshot;
            public bool registryResolved;
            public bool missingFromInitialManagedSnapshot;
            public bool potentialPunctualShadowSource;
            public float shadowFadeDistance;
            public float shadowDimmer;
            public float volumetricShadowDimmer;
            public float lightFadeDistance;
            public float lightDimmer;
            public float volumetricDimmer;
            public string shadowUpdateMode;
            public bool hdEntityValid;
            public int dataIndex = -1;
            public string entityError;
        }

        [Serializable]
        private sealed class ProcessedRecord
        {
            public int sortedIndex;
            public int visibleIndex;
            public int dataIndex;
            public uint sortKey;
            public bool offscreen;
            public string processedLightType;
            public bool sourceMapped;
            public bool sourceResolvedFromRegistry;
            public bool sourceMissingFromInitialManagedSnapshot;
            public bool sourceResolutionUnavailable;
            public string sourceResolutionError;
            public string sourceEntityId;
            public string sourcePath;
            public string sourceRole;
            public string sourceType;
            public float sourceDistance;
            public bool sourceTracked;
            public int shadowMapFlags;
            public string shadowMapFlagsName;
            public bool gpuRecordAvailable;
            public int shadowIndex;
            public int contactShadowMask;
            public Vector3 gpuLightColor;
        }
    }
}
