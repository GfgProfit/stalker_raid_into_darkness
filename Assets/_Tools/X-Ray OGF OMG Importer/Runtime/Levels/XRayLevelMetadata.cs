using System;
using System.Collections.Generic;
using UnityEngine;

namespace XRay.Importer.Levels
{
    [Serializable]
    public sealed class XRayLevelSectorInfo
    {
        [SerializeField] private int _index;
        [SerializeField] private int _rootVisual;
        [SerializeField] private int[] _portals;
        [SerializeField] private Bounds _bounds;

        public XRayLevelSectorInfo(int index, int rootVisual, int[] portals, Bounds bounds)
        {
            _index = index;
            _rootVisual = rootVisual;
            _portals = portals;
            _bounds = bounds;
        }

        public int Index => _index;
        public int RootVisual => _rootVisual;
        public int[] Portals => _portals;
        public Bounds Bounds => _bounds;
    }

    [Serializable]
    public sealed class XRayLevelPortalInfo
    {
        [SerializeField] private int _sectorFront;
        [SerializeField] private int _sectorBack;
        [SerializeField] private Vector3[] _vertices;

        public XRayLevelPortalInfo(int sectorFront, int sectorBack, Vector3[] vertices)
        {
            _sectorFront = sectorFront;
            _sectorBack = sectorBack;
            _vertices = vertices;
        }

        public int SectorFront => _sectorFront;
        public int SectorBack => _sectorBack;
        public Vector3[] Vertices => _vertices;
    }

    public sealed class XRayLevelMetadata : MonoBehaviour
    {
        [SerializeField] private string _levelName;
        [SerializeField] private int _xrlcVersion;
        [SerializeField] private string _sourceFolder;
        [SerializeField] private string _importedUtc;
        [SerializeField] private Bounds _bounds;
        [SerializeField] private List<XRayLevelSectorInfo> _sectors = new List<XRayLevelSectorInfo>();
        [SerializeField] private List<XRayLevelPortalInfo> _portals = new List<XRayLevelPortalInfo>();
        [SerializeField] private List<string> _limitations = new List<string>();
        [SerializeField] private bool _drawPortals;

        public string LevelName => _levelName;
        public int XrlcVersion => _xrlcVersion;
        public string SourceFolder => _sourceFolder;
        public Bounds Bounds => _bounds;
        public IReadOnlyList<XRayLevelSectorInfo> Sectors => _sectors;
        public IReadOnlyList<XRayLevelPortalInfo> Portals => _portals;
        public IReadOnlyList<string> Limitations => _limitations;

        public void Initialize(string levelName, int xrlcVersion, string sourceFolder, string importedUtc, Bounds bounds, List<XRayLevelSectorInfo> sectors, List<XRayLevelPortalInfo> portals, List<string> limitations)
        {
            _levelName = levelName;
            _xrlcVersion = xrlcVersion;
            _sourceFolder = sourceFolder;
            _importedUtc = importedUtc;
            _bounds = bounds;
            _sectors = sectors;
            _portals = portals;
            _limitations = limitations;
        }

        private void OnDrawGizmosSelected()
        {
            if (!_drawPortals)
            {
                return;
            }

            Gizmos.color = new Color(1.0f, 0.6f, 0.1f, 0.8f);
            foreach (XRayLevelPortalInfo portal in _portals)
            {
                Vector3[] vertices = portal.Vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Gizmos.DrawLine(vertices[i], vertices[(i + 1) % vertices.Length]);
                }
            }
        }
    }
}
