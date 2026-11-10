namespace XRay.Importer
{
    public interface IXRayAnmSource
    {
        void Configure(XRayAnmAsset motion, bool relativeToFirstFrame);
    }
}
