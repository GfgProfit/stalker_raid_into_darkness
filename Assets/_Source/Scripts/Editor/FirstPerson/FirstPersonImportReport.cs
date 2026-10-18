using System;
using System.Collections.Generic;

namespace RaidIntoDarkness.FirstPerson.Editor
{
    [Serializable]
    public sealed class FirstPersonOutfitRow
    {
        public string section;
        public string displayName;
        public string hudSection;
        public string handsVirtualPath;
        public string handsAsset;
        public string handsSource;
        public string legsSection;
        public string bodyVirtualPath;
        public string bodyAsset;
        public string bodySource;
        public bool isExoskeleton;
        public string repairType;
        public string resolution;
    }

    [Serializable]
    public sealed class FirstPersonModelRow
    {
        public string kind;
        public string virtualPath;
        public string assetPath;
        public string source;
        public string overriddenSources;
        public string copyStatus;
        public int usedByOutfits;
        public bool imported;
        public int bones;
        public int skinnedRenderers;
        public int meshRenderers;
        public int vertices;
        public int triangles;
        public int bindposeMismatches;
        public int missingBones;
        public int materials;
        public int generatedMaterials;
        public int fallbackMaterials;
        public int materialsWithoutBaseColor;
        public int texturesResolved;
        public int texturesMissing;
        public bool embeddedMotionsPresent;
        public bool embeddedMotionsImported;
        public string motionReferences;
        public string problems;
    }

    [Serializable]
    public sealed class FirstPersonCompatibilityRow
    {
        public string body;
        public int bodyBones;
        public int animatedBonesMissingInBody;
        public int bodyBonesNotAnimated;
        public int curvePathsMissing;
        public string missingBoneNames;
        public string extraBoneNames;
    }

    [Serializable]
    public sealed class FirstPersonSampleRow
    {
        public string body;
        public string motion;
        public float time;
        public int drivenTransforms;
        public int nonFiniteTransforms;
        public float maxRotationFromRestDegrees;
        public float maxPositionFromRest;
        public float maxRotationDifferenceToReferenceDegrees;
    }

    [Serializable]
    public sealed class FirstPersonMotionRow
    {
        public string table;
        public int flags;
        public string motion;
        public string clipSource;
        public bool found;
    }

    [Serializable]
    public sealed class FirstPersonImportReportData
    {
        public string createdUtc;
        public string profile;
        public int activeMods;
        public int indexedFiles;
        public int ltxSections;
        public int dltxFiles;
        public int rusStrings;
        public bool useHdModels;
        public string swmScriptSource;
        public int outfitRows;
        public int singlePlayerOutfits;
        public int multiplayerExcluded;
        public string multiplayerSections;
        public int exoskeletonOutfits;
        public int uniqueHands;
        public int uniqueBodies;
        public int handsImported;
        public int bodiesImported;
        public int animationsImported;
        public int clipsInOmf;
        public int duplicateMotionsAcrossOmf;
        public int tableEntries;
        public int tableEntriesMissing;
        public int importErrors;
        public int importWarnings;
        public float cameraHeight;
        public string cameraHeightOrigin;
        public float legsOffset;
        public List<string> resolutionCounts = new List<string>();
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();
        public List<FirstPersonOutfitRow> outfits = new List<FirstPersonOutfitRow>();
        public List<FirstPersonModelRow> models = new List<FirstPersonModelRow>();
        public List<FirstPersonMotionRow> motions = new List<FirstPersonMotionRow>();
        public List<FirstPersonCompatibilityRow> compatibility = new List<FirstPersonCompatibilityRow>();
        public List<FirstPersonSampleRow> samples = new List<FirstPersonSampleRow>();
    }
}
