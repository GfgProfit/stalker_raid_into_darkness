using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;
using XRay.Importer.Core.Anm;
using XRay.Importer.Core.IO;

namespace XRay.Importer.Editor
{
    [ScriptedImporter(IMPORTER_VERSION, "anm")]
    public sealed class XRayAnmImporter : ScriptedImporter
    {
        public const int IMPORTER_VERSION = 1;
        public const string MOTION_IDENTIFIER = "motion";
        public const string CLIP_IDENTIFIER = "clip";
        public const string PREVIEW_IDENTIFIER = "preview";

        [SerializeField] private bool _importAnimation = true;
        [SerializeField] private bool _createPreviewObject = true;
        [Tooltip("Uniform scale applied to positions. X-Ray units are meters, same as the OGF importer default.")]
        [SerializeField] private float _scale = 1.0f;
        [Tooltip("Store the original envelopes in the motion asset for exact runtime evaluation by XRayAnmPlayer.")]
        [SerializeField] private bool _preserveOriginalCurves = true;
        [SerializeField] private bool _generateAnimationClip = true;
        [Tooltip("Exact: keys and tangents reproduce TCB/Hermite/Bezier/Linear/Stepped spans analytically. Adaptive: sampled until the error tolerance is met. Fixed FPS: one key per ANM frame.")]
        [SerializeField] private XRayAnmSamplingMode _samplingMode = XRayAnmSamplingMode.Exact;
        [Tooltip("Engine: X-Ray engine envelope evaluation (OpenXRay interp.cpp). BlenderXRay: the interpretation of blender-xray, including its V4 channel order and Bezier 2D time units.")]
        [SerializeField] private XRayAnmSemantics _semantics = XRayAnmSemantics.Engine;
        [SerializeField] private bool _loop;
        [SerializeField] private bool _loopPose;
        [SerializeField] private bool _createCameraPreview;
        [Tooltip("Preview setting only; ANM files contain no field of view.")]
        [SerializeField] private float _previewFieldOfView = 67.5f;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            string fileName = Path.GetFileName(ctx.assetPath);
            XRayAnmMotion motion;
            try
            {
                motion = XRayAnmReader.ReadFile(File.ReadAllBytes(ctx.assetPath), fileName);
            }
            catch (XRayFormatException exception)
            {
                ctx.LogImportError($"{fileName}: {exception.Message}");
                SetPlaceholder(ctx, fileName);
                return;
            }
            catch (Exception exception)
            {
                ctx.LogImportError($"{fileName}: failed to read ANM: {exception}");
                SetPlaceholder(ctx, fileName);
                return;
            }

            if (string.IsNullOrEmpty(motion.Name))
            {
                motion.Name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            }

            var warnings = new List<string>(motion.Warnings);
            AnimationClip clip = null;
            if (_importAnimation && _generateAnimationClip)
            {
                clip = XRayAnmClipBuilder.Build(motion, _semantics, _scale, _samplingMode, _loop, _loopPose, warnings);
                ctx.AddObjectToAsset(CLIP_IDENTIFIER, clip);
            }

            motion.Warnings.Clear();
            motion.Warnings.AddRange(warnings);
            var asset = ScriptableObject.CreateInstance<XRayAnmAsset>();
            asset.name = motion.Name;
            asset.SetImportData(fileName, motion, _scale, _semantics, _importAnimation && _preserveOriginalCurves, clip);
            ctx.AddObjectToAsset(MOTION_IDENTIFIER, asset);
            foreach (string warning in warnings)
            {
                ctx.LogImportWarning($"{fileName}: {warning}");
            }

            if (_importAnimation && _createPreviewObject)
            {
                GameObject preview = XRayAnmPreviewBuilder.Build(asset, _createCameraPreview, _previewFieldOfView);
                ctx.AddObjectToAsset(PREVIEW_IDENTIFIER, preview);
                ctx.SetMainObject(preview);
            }
            else
            {
                ctx.SetMainObject(asset);
            }
        }

        private static void SetPlaceholder(AssetImportContext ctx, string fileName)
        {
            var asset = ScriptableObject.CreateInstance<XRayAnmAsset>();
            asset.name = fileName;
            ctx.AddObjectToAsset(MOTION_IDENTIFIER, asset);
            ctx.SetMainObject(asset);
        }
    }
}
