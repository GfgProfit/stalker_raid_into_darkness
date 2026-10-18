namespace XRay.Importer.Core.Anm
{
    public sealed class XRayAnmKey
    {
        public double Value { get; set; }
        public double Time { get; set; }
        public XRayAnmShape Shape { get; set; }
        public double Tension { get; set; }
        public double Continuity { get; set; }
        public double Bias { get; set; }
        public double Param0 { get; set; }
        public double Param1 { get; set; }
        public double Param2 { get; set; }
        public double Param3 { get; set; }

        public XRayAnmKey Clone()
        {
            return (XRayAnmKey)MemberwiseClone();
        }
    }
}
