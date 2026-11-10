namespace XRay.Importer.Levels
{
    public interface IXRayAnomalyActivitySource
    {
        bool CanDeactivate { get; }

        void SetSourceActive(bool active);
    }
}
