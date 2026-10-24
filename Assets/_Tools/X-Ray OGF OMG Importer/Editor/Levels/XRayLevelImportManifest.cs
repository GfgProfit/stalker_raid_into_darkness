using UnityEngine;

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayLevelImportManifest : ScriptableObject
    {
        [SerializeField] private XRayLevelImportRecord _record = new XRayLevelImportRecord();

        public XRayLevelImportRecord Record
        {
            get => _record;
            set => _record = value;
        }
    }
}
