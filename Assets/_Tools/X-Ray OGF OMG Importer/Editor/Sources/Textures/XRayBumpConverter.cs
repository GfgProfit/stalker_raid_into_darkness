using System;

namespace XRay.Importer.Sources.Textures
{
    public static class XRayBumpConverter
    {
        public static byte[] ToTangentNormalMap(byte[] bump, int width, int height, byte[] correction, int correctionWidth, int correctionHeight, bool flipGreen)
        {
            int pixels = width * height;
            if (bump == null || bump.Length < pixels * 4)
            {
                throw new ArgumentException("Bump pixel data does not match the texture size.", nameof(bump));
            }

            bool hasCorrection = correction != null && correctionWidth > 0 && correctionHeight > 0 && correction.Length >= correctionWidth * correctionHeight * 4;
            var sample = new double[3];
            var output = new byte[pixels * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int offset = (y * width + x) * 4;
                    double nx = bump[offset + 3] / 255.0 - 0.5;
                    double ny = bump[offset + 2] / 255.0 - 0.5;
                    double nz = bump[offset + 1] / 255.0 - 0.5;
                    if (hasCorrection)
                    {
                        SampleBilinear(correction, correctionWidth, correctionHeight, (x + 0.5) / width, (y + 0.5) / height, sample);
                        nx += sample[0] - 0.5;
                        ny += sample[1] - 0.5;
                        nz += sample[2] - 0.5;
                    }

                    double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (length < 1e-6)
                    {
                        nx = 0.0;
                        ny = 0.0;
                        nz = 1.0;
                    }
                    else
                    {
                        nx /= length;
                        ny /= length;
                        nz /= length;
                    }

                    if (flipGreen)
                    {
                        ny = -ny;
                    }

                    output[offset] = ToByte(nx);
                    output[offset + 1] = ToByte(ny);
                    output[offset + 2] = ToByte(nz);
                    output[offset + 3] = 255;
                }
            }

            return output;
        }

        private static void SampleBilinear(byte[] rgba, int width, int height, double u, double v, double[] result)
        {
            double fx = u * width - 0.5;
            double fy = v * height - 0.5;
            int x0 = (int)Math.Floor(fx);
            int y0 = (int)Math.Floor(fy);
            double tx = fx - x0;
            double ty = fy - y0;
            for (int channel = 0; channel < 3; channel++)
            {
                double top = Lerp(Fetch(rgba, width, height, x0, y0, channel), Fetch(rgba, width, height, x0 + 1, y0, channel), tx);
                double bottom = Lerp(Fetch(rgba, width, height, x0, y0 + 1, channel), Fetch(rgba, width, height, x0 + 1, y0 + 1, channel), tx);
                result[channel] = Lerp(top, bottom, ty) / 255.0;
            }
        }

        private static double Fetch(byte[] rgba, int width, int height, int x, int y, int channel)
        {
            int wrappedX = ((x % width) + width) % width;
            int wrappedY = ((y % height) + height) % height;
            return rgba[(wrappedY * width + wrappedX) * 4 + channel];
        }

        private static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }

        private static byte ToByte(double value)
        {
            return (byte)Math.Round(Math.Max(0.0, Math.Min(1.0, value * 0.5 + 0.5)) * 255.0);
        }
    }
}
