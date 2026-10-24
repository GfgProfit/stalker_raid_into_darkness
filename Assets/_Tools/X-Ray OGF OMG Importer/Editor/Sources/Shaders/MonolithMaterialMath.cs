using System;

namespace XRay.Importer.Sources.Shaders
{
    public static class MonolithMaterialMath
    {
        public static double DecodeMaterial(uint materialId, double weight)
        {
            double raw = materialId + (materialId < 4 ? weight : 0.0);
            double normalized = raw < 5.0 ? (raw + 0.5) / 4.0 : raw;
            uint quantized = (uint)(normalized / 1.333333333 * 31.0);
            return (quantized & 31u) / 31.0 * 1.333333333;
        }

        public static double Metalness(double gloss, double material)
        {
            double threshold = Math.Pow(0.125, Math.Pow(2.0, Math.Max(0.0, material * 4.0 - 0.5) / 4.0));
            double softness = threshold * 0.9;
            return material > 0.751 ? Saturate((gloss - (threshold - softness)) / (2.0 * softness)) : 0.0;
        }

        public static double Roughness(double gloss, double material, double dynamicGloss)
        {
            double boostedGloss = Math.Pow(gloss, 1.0 - Metalness(gloss, material) * 0.25);
            double rough = Math.Pow(1.0 + (0.45 - 1.0) * boostedGloss, 0.5 / Math.Max(0.001, 1.0 - dynamicGloss));
            return Saturate(rough * rough);
        }

        public static double Specular(double encodedAlbedo, double luminance, double gloss, double material)
        {
            double metalness = Metalness(gloss, material);
            double blend = 0.5 + (0.5 - luminance) * 0.45;
            double boosted = blend <= 0.5 ? encodedAlbedo - (1.0 - 2.0 * blend) * encodedAlbedo * (1.0 - encodedAlbedo) : encodedAlbedo + (2.0 * blend - 1.0) * (Math.Sqrt(encodedAlbedo) - encodedAlbedo);
            double boost = Math.Pow(Math.Pow(2.0, 1.15 * ((Saturate(material * 1.425) * 2.0 - 1.0) + (Math.Sqrt(gloss) * 2.0 - 1.0))), 1.15);
            return Saturate(0.04 * boost * (1.0 - metalness) + Math.Pow(boosted, 2.2) * metalness);
        }

        public static double FloraGloss(double packedGloss, double glossScale)
        {
            return Saturate(packedGloss * packedGloss * glossScale);
        }

        public static double QuantizeGloss(double gloss)
        {
            return Math.Round(gloss * 255.0) / 255.0;
        }

        public static void FloraNormal(double packedA, double packedB, out double x, out double y, out double z)
        {
            Normalize((packedA - 0.5 + 0.02) * 3.0, 1.0, (packedB - 0.5 + 0.02) * 3.0, out x, out y, out z);
        }

        public static void LodNormal(double packedX, double packedY, double packedZ, double[] viewRight, double[] viewUp, double[] viewBack, out double x, out double y, out double z)
        {
            double viewX = packedX * 2.0 - 1.0;
            double viewY = packedY * 2.0 - 1.0;
            double viewZ = packedZ * 2.0 - 1.0;
            if (viewX * viewX + viewY * viewY + viewZ * viewZ <= 1e-8)
            {
                viewX = 0.0;
                viewY = 1.0;
                viewZ = 0.0;
            }

            Normalize(viewX, viewY, viewZ, out viewX, out viewY, out viewZ);
            Normalize(viewRight[0] * viewX + viewUp[0] * viewY - viewBack[0] * viewZ, viewRight[1] * viewX + viewUp[1] * viewY - viewBack[1] * viewZ, viewRight[2] * viewX + viewUp[2] * viewY - viewBack[2] * viewZ, out x, out y, out z);
        }

        private static void Normalize(double inX, double inY, double inZ, out double x, out double y, out double z)
        {
            double length = Math.Sqrt(inX * inX + inY * inY + inZ * inZ);
            x = inX / length;
            y = inY / length;
            z = inZ / length;
        }

        private static double Saturate(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }
    }
}
