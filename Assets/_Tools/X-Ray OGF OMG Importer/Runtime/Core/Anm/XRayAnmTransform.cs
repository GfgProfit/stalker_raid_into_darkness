namespace XRay.Importer.Core.Anm
{
    public static class XRayAnmTransform
    {
        public static XQuaternion ToRotation(XVector3 rotationHpb)
        {
            return XQuaternion.FromXRayBindEuler(ToEulerXyz(rotationHpb));
        }

        public static XVector3 ToEulerXyz(XVector3 rotationHpb)
        {
            return new XVector3(rotationHpb.Y, rotationHpb.X, rotationHpb.Z);
        }

        public static XMatrix4 EngineHpbMatrix(double h, double p, double b)
        {
            double sh = System.Math.Sin(h);
            double ch = System.Math.Cos(h);
            double sp = System.Math.Sin(p);
            double cp = System.Math.Cos(p);
            double sb = System.Math.Sin(b);
            double cb = System.Math.Cos(b);
            double cc = ch * cb;
            double cs = ch * sb;
            double sc = sh * cb;
            double ss = sh * sb;
            double[] i = { cc - sp * ss, -cp * sb, sp * cs + sc };
            double[] j = { sp * sc + cs, cp * cb, ss - sp * cc };
            double[] k = { -cp * sh, sp, cp * ch };
            return XMatrix4.FromRows(i[0], j[0], k[0], 0, i[1], j[1], k[1], 0, i[2], j[2], k[2], 0, 0, 0, 0, 1);
        }
    }
}
