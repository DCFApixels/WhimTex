using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public struct Double2 : IEquatable<Double2>
    {
        public double x, y;
        public Double2(double x, double y) { this.x = x; this.y = y; }
        public static implicit operator Double2(Vector2 v) => new Double2(v.x, v.y);
        public static implicit operator Vector2(Double2 v) => new Vector2((float)v.x, (float)v.y);
        public bool Equals(Double2 other) => x==other.x && y==other.y;
        public override bool Equals(object other) => other is Double2 value && Equals(value);
        public override int GetHashCode() => x.GetHashCode()*397 ^ y.GetHashCode();
        public static bool operator ==(Double2 a,Double2 b) => a.Equals(b);
        public static bool operator !=(Double2 a,Double2 b) => !a.Equals(b);
        public static Double2 operator +(Double2 a, Double2 b) => new Double2(a.x + b.x, a.y + b.y);
        public static Double2 operator -(Double2 a, Double2 b) => new Double2(a.x - b.x, a.y - b.y);
        public static Double2 operator *(Double2 a, double b) => new Double2(a.x * b, a.y * b);
    }

    [Serializable]
    public struct ProjectiveMatrix : IEquatable<ProjectiveMatrix>
    {
        public double m00, m01, m02, m10, m11, m12, m20, m21, m22;
        public bool Equals(ProjectiveMatrix b) => m00==b.m00 && m01==b.m01 && m02==b.m02 &&
            m10==b.m10 && m11==b.m11 && m12==b.m12 && m20==b.m20 && m21==b.m21 && m22==b.m22;
        public override bool Equals(object other) => other is ProjectiveMatrix value && Equals(value);
        public override int GetHashCode() => m00.GetHashCode() ^ m11.GetHashCode()*397 ^ m22.GetHashCode();
        public static ProjectiveMatrix Identity => new ProjectiveMatrix { m00 = 1, m11 = 1, m22 = 1 };
        public static ProjectiveMatrix Translate(double x, double y) { var m = Identity; m.m02 = x; m.m12 = y; return m; }
        public static ProjectiveMatrix Scale(double x, double y) { var m = Identity; m.m00 = x; m.m11 = y; return m; }
        public static ProjectiveMatrix Rotate(double degrees)
        {
            double r = degrees * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
            return new ProjectiveMatrix { m00 = c, m01 = -s, m10 = s, m11 = c, m22 = 1 };
        }
        public static ProjectiveMatrix operator *(ProjectiveMatrix a, ProjectiveMatrix b) => new ProjectiveMatrix {
            m00=a.m00*b.m00+a.m01*b.m10+a.m02*b.m20, m01=a.m00*b.m01+a.m01*b.m11+a.m02*b.m21, m02=a.m00*b.m02+a.m01*b.m12+a.m02*b.m22,
            m10=a.m10*b.m00+a.m11*b.m10+a.m12*b.m20, m11=a.m10*b.m01+a.m11*b.m11+a.m12*b.m21, m12=a.m10*b.m02+a.m11*b.m12+a.m12*b.m22,
            m20=a.m20*b.m00+a.m21*b.m10+a.m22*b.m20, m21=a.m20*b.m01+a.m21*b.m11+a.m22*b.m21, m22=a.m20*b.m02+a.m21*b.m12+a.m22*b.m22 };
        public bool TryPoint(Double2 p, out Double2 result)
        {
            double w = m20*p.x+m21*p.y+m22;
            result = default;
            if (!Finite(w) || Math.Abs(w) < 1e-12) return false;
            result = new Double2((m00*p.x+m01*p.y+m02)/w, (m10*p.x+m11*p.y+m12)/w);
            return Finite(result.x) && Finite(result.y);
        }
        public Double2 Point(Double2 p) => TryPoint(p, out var q) ? q : new Double2(double.NaN, double.NaN);
        public bool TryInverse(out ProjectiveMatrix inverse)
        {
            inverse = new ProjectiveMatrix {
                m00=m11*m22-m12*m21, m01=m02*m21-m01*m22, m02=m01*m12-m02*m11,
                m10=m12*m20-m10*m22, m11=m00*m22-m02*m20, m12=m02*m10-m00*m12,
                m20=m10*m21-m11*m20, m21=m01*m20-m00*m21, m22=m00*m11-m01*m10 };
            double det=m00*inverse.m00+m01*inverse.m10+m02*inverse.m20;
            if (!Finite(det) || Math.Abs(det)<1e-20) { inverse=default; return false; }
            inverse = inverse.Divide(det);
            return inverse.IsFinite;
        }
        public ProjectiveMatrix Divide(double n) => new ProjectiveMatrix {
            m00=m00/n,m01=m01/n,m02=m02/n,m10=m10/n,m11=m11/n,m12=m12/n,m20=m20/n,m21=m21/n,m22=m22/n };
        public bool IsFinite => Finite(m00)&&Finite(m01)&&Finite(m02)&&Finite(m10)&&Finite(m11)&&Finite(m12)&&Finite(m20)&&Finite(m21)&&Finite(m22);
        internal static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        public bool ValidUnitQuad()
        {
            if (!IsFinite || !TryInverse(out _)) return false;
            double a=m22,b=m20+m22,c=m20+m21+m22,d=m21+m22;
            double limit=Math.Max(Math.Max(Math.Abs(a),Math.Abs(b)),Math.Max(Math.Abs(c),Math.Abs(d))) * 1e-7;
            if (!(a>limit && b>limit && c>limit && d>limit) && !(a < -limit && b < -limit && c < -limit && d < -limit)) return false;
            return true;
        }
        public static bool TryQuad(Double2 a, Double2 b, Double2 c, Double2 d, out ProjectiveMatrix result)
        {
            double dx=b.x-c.x, ex=d.x-c.x, fx=a.x-b.x+c.x-d.x;
            double dy=b.y-c.y, ey=d.y-c.y, fy=a.y-b.y+c.y-d.y;
            double det=dx*ey-ex*dy;
            result=default;
            if (Math.Abs(det)<1e-14) return false;
            double g=(fx*ey-ex*fy)/det, h=(dx*fy-fx*dy)/det;
            result=new ProjectiveMatrix { m00=b.x-a.x+g*b.x,m01=d.x-a.x+h*d.x,m02=a.x,
                m10=b.y-a.y+g*b.y,m11=d.y-a.y+h*d.y,m12=a.y,m20=g,m21=h,m22=1 };
            return result.ValidUnitQuad();
        }
        internal void SetShader(Material material, string prefix)
        {
            material.SetVector(prefix+"0",new Vector4((float)m00,(float)m01,(float)m02,0));
            material.SetVector(prefix+"1",new Vector4((float)m10,(float)m11,(float)m12,0));
            material.SetVector(prefix+"2",new Vector4((float)m20,(float)m21,(float)m22,0));
        }
    }
}
