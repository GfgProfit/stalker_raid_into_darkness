using System;

namespace XRay.Importer.Core.IO
{
    public sealed class XRayFormatException : Exception
    {
        public XRayFormatException(string message) : base(message)
        {
        }

        public XRayFormatException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
