/*******************************************************************************
* Author    :  Angus Johnson
* Copyright :  Angus Johnson 2010-2025
* License   :  https://www.boost.org/LICENSE_1_0.txt
*******************************************************************************
* Minimal clipping-core helpers adapted from Clipper2_2.0.1/Clipper.cs.
* Offset, rectangle clipping, Minkowski and triangulation modules are omitted.
*******************************************************************************/
using System;

namespace DCFApixels.WhimTex.ThirdParty.Clipper2Lib
{
    internal static class Clipper
    {
        public static Rect64 InvalidRect64 => new Rect64(false);
        public static double Area(Path64 path)
        {
            if (path.Count < 3) return 0;
            double a = 0;
            Point64 prev = path[path.Count - 1];
            foreach (Point64 pt in path) { a += (double)(prev.Y + pt.Y) * (prev.X - pt.X); prev = pt; }
            return a * .5;
        }
        public static double Area(PathD path)
        {
            if (path.Count < 3) return 0;
            double a = 0;
            PointD prev = path[path.Count - 1];
            foreach (PointD pt in path) { a += (prev.y + pt.y) * (prev.x - pt.x); prev = pt; }
            return a * .5;
        }
        public static Path64 ScalePath64(PathD path, double scale)
        {
            var result = new Path64(path.Count);
            foreach (PointD pt in path) result.Add(new Point64(pt, scale));
            return result;
        }
        public static Paths64 ScalePaths64(PathsD paths, double scale)
        {
            var result = new Paths64(paths.Count);
            foreach (PathD path in paths) result.Add(ScalePath64(path, scale));
            return result;
        }
        public static PathD ScalePathD(Path64 path, double scale)
        {
            var result = new PathD(path.Count);
            foreach (Point64 pt in path) result.Add(new PointD(pt, scale));
            return result;
        }
        public static double PerpendicDistFromLineSqrd(Point64 pt, Point64 line1, Point64 line2)
        {
            double a = (double)pt.X - line1.X, b = (double)pt.Y - line1.Y;
            double c = (double)line2.X - line1.X, d = (double)line2.Y - line1.Y;
            if (c == 0 && d == 0) return 0;
            double cross = a * d - c * b;
            return cross * cross / (c * c + d * d);
        }
    }

    // Local portable hash combiner: only bucket distribution, not geometry.
    // Avoids the upstream compatibility shim's random-number dependency.
    internal static class HashCode
    {
        public static int Combine<T1, T2>(T1 a, T2 b) =>
            unchecked((a == null ? 0 : a.GetHashCode()) * 397 ^ (b == null ? 0 : b.GetHashCode()));
    }
}
