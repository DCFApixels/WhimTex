using System;

namespace DCFApixels.WhimTex
{
    internal struct GradientVector
    {
        internal double x, y, z;
        internal GradientVector(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        internal double this[int i] => i == 0 ? x : i == 1 ? y : z;
        public static GradientVector operator +(GradientVector a, GradientVector b) => new GradientVector(a.x + b.x, a.y + b.y, a.z + b.z);
        public static GradientVector operator -(GradientVector a, GradientVector b) => new GradientVector(a.x - b.x, a.y - b.y, a.z - b.z);
        public static GradientVector operator *(GradientVector a, double b) => new GradientVector(a.x * b, a.y * b, a.z * b);
    }

    internal static class GradientColorMath
    {
        private static readonly double[,] Lms = {
            { .4122214708, .5363325363, .0514459929 },
            { .2119034982, .6806995451, .1073969566 },
            { .0883024619, .2817188376, .6299787005 }
        };
        private static readonly double[,] Lab = {
            { .2104542553, .7936177850, -.0040720468 },
            { 1.9779984951, -2.4285922050, .4505937099 },
            { .0259040371, .7827717662, -.8086757660 }
        };
        private static readonly double[,] InverseLms = Inverse(Lms), InverseLab = Inverse(Lab);
        internal static double Decode(double v) => Math.Abs(v) <= .04045 ? v / 12.92 : Math.Sign(v) * Math.Pow((Math.Abs(v) + .055) / 1.055, 2.4);
        internal static double Encode(double v) => Math.Abs(v) <= .0031308 ? 12.92 * v : Math.Sign(v) * (1.055 * Math.Pow(Math.Abs(v), 1 / 2.4) - .055);
        internal static GradientVector Decode(GradientVector v) => new GradientVector(Decode(v.x), Decode(v.y), Decode(v.z));
        internal static GradientVector Encode(GradientVector v) => new GradientVector(Encode(v.x), Encode(v.y), Encode(v.z));
        internal static GradientVector ToLab(GradientVector rgb)
        {
            var v = Multiply(Lms, Decode(rgb));
            return Multiply(Lab, new GradientVector(CubeRoot(v.x), CubeRoot(v.y), CubeRoot(v.z)));
        }
        internal static GradientVector FromLab(GradientVector lab)
        {
            var v = Multiply(InverseLab, lab);
            return Encode(Multiply(InverseLms, new GradientVector(v.x * v.x * v.x, v.y * v.y * v.y, v.z * v.z * v.z)));
        }
        internal static double HueChroma(GradientVector lab) => Math.Sqrt(lab.y * lab.y + lab.z * lab.z);
        internal static GradientVector ResolveHue(GradientVector lab, double chroma)
        {
            // Circular mean direction follows the minor arc. Recover the mean chroma only
            // when directions agree; a quintic fade keeps cancellation continuous in OKLab.
            double magnitude = HueChroma(lab);
            double agreement = magnitude / Math.Max(chroma, 1e-20);
            double t = Math.Max(0, Math.Min(1, (agreement - .25) * 4));
            double blend = t * t * t * (t * (t * 6 - 15) + 10);
            double scale = 1 + blend * Math.Max(0, chroma - magnitude) / Math.Max(magnitude, 1e-20);
            return new GradientVector(lab.x, lab.y * scale, lab.z * scale);
        }
        internal static GradientVector ToLch(GradientVector lab)
        {
            double h = Math.Atan2(lab.z, lab.y) * 180 / Math.PI;
            return new GradientVector(lab.x, Math.Sqrt(lab.y * lab.y + lab.z * lab.z), h < 0 ? h + 360 : h);
        }
        internal static GradientVector HueLab(GradientVector lch)
        {
            double angle = (lch.z % 360) * Math.PI / 180;
            return new GradientVector(lch.x, lch.y * Math.Cos(angle), lch.y * Math.Sin(angle));
        }
        internal static double HueDelta(double a, double b, WhimTexGradientHuePath path)
        {
            double d = b - a;
            switch (path)
            {
                case WhimTexGradientHuePath.Shortest: if (d > 180) d -= 360; else if (d < -180) d += 360; break;
                case WhimTexGradientHuePath.Longest: if (d > 0 && d < 180) d -= 360; else if (d <= 0 && d > -180) d += 360; break;
                case WhimTexGradientHuePath.Increasing: if (d < 0) d += 360; break;
                case WhimTexGradientHuePath.Decreasing: if (d > 0) d -= 360; break;
            }
            return d;
        }
        private static double CubeRoot(double v) => Math.Sign(v) * Math.Pow(Math.Abs(v), 1.0 / 3);
        private static GradientVector Multiply(double[,] m, GradientVector v) => new GradientVector(
            m[0, 0] * v.x + m[0, 1] * v.y + m[0, 2] * v.z,
            m[1, 0] * v.x + m[1, 1] * v.y + m[1, 2] * v.z,
            m[2, 0] * v.x + m[2, 1] * v.y + m[2, 2] * v.z);
        private static double[,] Inverse(double[,] m)
        {
            double a = m[0, 0], b = m[0, 1], c = m[0, 2], d = m[1, 0], e = m[1, 1], f = m[1, 2], g = m[2, 0], h = m[2, 1], i = m[2, 2];
            double determinant = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
            return new double[,] {
                { (e*i-f*h)/determinant, (c*h-b*i)/determinant, (b*f-c*e)/determinant },
                { (f*g-d*i)/determinant, (a*i-c*g)/determinant, (c*d-a*f)/determinant },
                { (d*h-e*g)/determinant, (b*g-a*h)/determinant, (a*e-b*d)/determinant }
            };
        }
    }
}
