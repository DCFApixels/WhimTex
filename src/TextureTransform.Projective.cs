using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum TransformStorage { TRS, Projective }
    public partial struct TextureTransform
    {
        public ProjectiveMatrix ToMatrix(double width, double height)
        {
            if (storage == TransformStorage.Projective) return matrix;
            double angle=rotation*Math.PI/180, c=Math.Cos(angle),s=Math.Sin(angle);
            double a=c*scale.x,b=-s*scale.y*height/width,d=s*scale.x*width/height,e=c*scale.y;
            return new ProjectiveMatrix { m00=a,m01=b,m02=pivot.x+position.x/width-a*pivot.x-b*pivot.y,
                m10=d,m11=e,m12=pivot.y+position.y/height-d*pivot.x-e*pivot.y,m22=1 };
        }
        public bool TrySetMatrix(ProjectiveMatrix value)
        {
            if (!value.IsFinite || value.m22 == 0) return false;
            value=value.Divide(value.m22);
            if (!value.ValidUnitQuad() || !value.TryPoint(pivot,out _)) return false;
            matrix = value;
            storage = TransformStorage.Projective;
            return true;
        }
        public Vector2 Map(Vector2 uv, Vector2 size) => ToMatrix(size.x,size.y).Point(uv);
        public Vector2 Unmap(Vector2 uv, Vector2 size) => ToMatrix(size.x,size.y).TryInverse(out var inverse)
            ? (Vector2)inverse.Point(uv) : new Vector2(float.NaN,float.NaN);
        internal bool TrySetPivot(Double2 value)
        {
            if (!ProjectiveMatrix.Finite(value.x) || !ProjectiveMatrix.Finite(value.y)) return false;
            if (storage==TransformStorage.Projective && !matrix.TryPoint(value,out _)) return false;
            pivot=value; return true;
        }

        internal void GetDisplay(Vector2 size, out Double2 offset, out Double2 scaling, out double angle)
        {
            offset=position; scaling=scale; angle=rotation;
            if (storage == TransformStorage.TRS) return;
            var center=matrix.Point(pivot);
            offset=new Double2((center.x-pivot.x)*size.x,(center.y-pivot.y)*size.y);
            double w=matrix.m20*pivot.x+matrix.m21*pivot.y+matrix.m22;
            double a=(matrix.m00-center.x*matrix.m20)/w;
            double b=(matrix.m10-center.y*matrix.m20)/w*size.y/size.x;
            double c=(matrix.m01-center.x*matrix.m21)/w*size.x/size.y;
            double d=(matrix.m11-center.y*matrix.m21)/w;
            double sx=Math.Sqrt(a*a+b*b);
            scaling=new Double2(sx,(a*d-b*c)/sx);
            angle=Math.Atan2(b,a)*180/Math.PI;
        }
        internal void EditPosition(Double2 value, Vector2 size)
        {
            if (!ProjectiveMatrix.Finite(value.x) || !ProjectiveMatrix.Finite(value.y)) return;
            if (storage==TransformStorage.TRS) { position=value; return; }
            GetDisplay(size,out var old,out _,out _);
            TrySetMatrix(ProjectiveMatrix.Translate((value.x-old.x)/size.x,(value.y-old.y)/size.y)*matrix);
        }
        internal void EditRotation(double value, Vector2 size)
        {
            if (!ProjectiveMatrix.Finite(value)) return;
            if (storage==TransformStorage.TRS) { rotation=value; return; }
            GetDisplay(size,out _,out _,out var old);
            AroundPivot(ProjectiveMatrix.Rotate(value-old),size);
        }
        internal void EditScale(Double2 value, Vector2 size)
        {
            if (!ProjectiveMatrix.Finite(value.x) || !ProjectiveMatrix.Finite(value.y) || Math.Abs(value.x)<1e-7 || Math.Abs(value.y)<1e-7) return;
            if (storage==TransformStorage.TRS) { scale=value; return; }
            GetDisplay(size,out _,out var old,out var angle);
            AroundPivot(ProjectiveMatrix.Rotate(angle)*ProjectiveMatrix.Scale(value.x/old.x,value.y/old.y)*ProjectiveMatrix.Rotate(-angle),size);
        }
        internal void AroundPivot(ProjectiveMatrix pixels, Vector2 size)
        {
            var p=ToMatrix(size.x,size.y).Point(pivot);
            TrySetMatrix(ProjectiveMatrix.Translate(p.x,p.y)*ProjectiveMatrix.Scale(1d/size.x,1d/size.y)*pixels*
                ProjectiveMatrix.Scale(size.x,size.y)*ProjectiveMatrix.Translate(-p.x,-p.y)*ToMatrix(size.x,size.y));
        }
    }
}
