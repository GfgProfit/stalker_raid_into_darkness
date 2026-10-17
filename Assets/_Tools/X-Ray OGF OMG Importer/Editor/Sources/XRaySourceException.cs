using System;

namespace XRay.Importer.Sources
{
    public sealed class XRaySourceException : Exception
    {
        public XRaySourceException(string message) : base(message)
        {
        }

        public XRaySourceException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
