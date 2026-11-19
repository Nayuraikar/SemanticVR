using System;
using UnityEngine;

namespace SemanticVR.ColourProcessing
{
    /// <summary>A serialisable 3x3 matrix acting on column RGB vectors: out = M * rgb.</summary>
    [Serializable]
    public struct Matrix3x3
    {
        public float m00, m01, m02, m10, m11, m12, m20, m21, m22;

        public Matrix3x3(float a, float b, float c, float d, float e, float f, float g, float h, float i)
        { m00 = a; m01 = b; m02 = c; m10 = d; m11 = e; m12 = f; m20 = g; m21 = h; m22 = i; }

        /// <summary>The identity matrix.</summary>
        public static Matrix3x3 Identity => new Matrix3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
        /// <summary>The zero matrix.</summary>
        public static Matrix3x3 Zero => default;

        public Vector3 Row0 => new Vector3(m00, m01, m02);
        public Vector3 Row1 => new Vector3(m10, m11, m12);
        public Vector3 Row2 => new Vector3(m20, m21, m22);

        /// <summary>Matrix product a * b (b is applied first).</summary>
        public static Matrix3x3 operator *(Matrix3x3 a, Matrix3x3 b) => new Matrix3x3(
            a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20, a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21, a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22,
            a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20, a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21, a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22,
            a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20, a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21, a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22);

        public static Matrix3x3 operator +(Matrix3x3 a, Matrix3x3 b) => new Matrix3x3(a.m00 + b.m00, a.m01 + b.m01, a.m02 + b.m02, a.m10 + b.m10, a.m11 + b.m11, a.m12 + b.m12, a.m20 + b.m20, a.m21 + b.m21, a.m22 + b.m22);
        public static Matrix3x3 operator -(Matrix3x3 a, Matrix3x3 b) => new Matrix3x3(a.m00 - b.m00, a.m01 - b.m01, a.m02 - b.m02, a.m10 - b.m10, a.m11 - b.m11, a.m12 - b.m12, a.m20 - b.m20, a.m21 - b.m21, a.m22 - b.m22);
        public static Matrix3x3 operator *(float s, Matrix3x3 a) => new Matrix3x3(s * a.m00, s * a.m01, s * a.m02, s * a.m10, s * a.m11, s * a.m12, s * a.m20, s * a.m21, s * a.m22);

        /// <summary>Component-wise linear interpolation, t is not clamped.</summary>
        public static Matrix3x3 Lerp(Matrix3x3 a, Matrix3x3 b, float t) => a + t * (b - a);

        /// <summary>Applies the matrix to an RGB vector.</summary>
        public Vector3 Apply(Vector3 v) => new Vector3(Vector3.Dot(Row0, v), Vector3.Dot(Row1, v), Vector3.Dot(Row2, v));

        public float Determinant => m00 * (m11 * m22 - m12 * m21) - m01 * (m10 * m22 - m12 * m20) + m02 * (m10 * m21 - m11 * m20);

        /// <summary>True when every entry is a finite number.</summary>
        public bool IsFinite
        {
            get
            {
                foreach (var f in new[] { m00, m01, m02, m10, m11, m12, m20, m21, m22 }) if (float.IsNaN(f) || float.IsInfinity(f)) return false;
                return true;
            }
        }

        public float MaxAbs => Mathf.Max(Mathf.Abs(m00), Mathf.Abs(m01), Mathf.Abs(m02), Mathf.Abs(m10), Mathf.Abs(m11), Mathf.Abs(m12), Mathf.Abs(m20), Mathf.Abs(m21), Mathf.Abs(m22));

        /// <summary>Row-major element access (index 0..8).</summary>
        public float this[int i]
        {
            get { switch (i) { case 0: return m00; case 1: return m01; case 2: return m02; case 3: return m10; case 4: return m11; case 5: return m12; case 6: return m20; case 7: return m21; default: return m22; } }
            set { switch (i) { case 0: m00 = value; break; case 1: m01 = value; break; case 2: m02 = value; break; case 3: m10 = value; break; case 4: m11 = value; break; case 5: m12 = value; break; case 6: m20 = value; break; case 7: m21 = value; break; default: m22 = value; break; } }
        }

        public override string ToString() => $"[{m00:0.###} {m01:0.###} {m02:0.###}; {m10:0.###} {m11:0.###} {m12:0.###}; {m20:0.###} {m21:0.###} {m22:0.###}]";
    }

    /// <summary>Validation of colour matrices before they reach the shader.</summary>
    public static class MatrixValidator
    {
        /// <summary>Largest allowed absolute entry.</summary>
        public const float MaxEntry = 4f;
        /// <summary>Smallest allowed |determinant| for matrices that must be invertible (corrections).</summary>
        public const float MinDeterminant = 0.05f;

        /// <summary>Returns true if valid; otherwise false with a reason in <paramref name="error"/>.</summary>
        public static bool Validate(Matrix3x3 m, bool requireInvertible, out string error)
        {
            error = null;
            if (!m.IsFinite) { error = "matrix contains NaN/Infinity"; return false; }
            if (m.MaxAbs > MaxEntry) { error = $"matrix entry exceeds {MaxEntry}"; return false; }
            if (requireInvertible && Mathf.Abs(m.Determinant) < MinDeterminant) { error = $"matrix is (nearly) singular, |det| = {Mathf.Abs(m.Determinant):0.####}"; return false; }
            return true;
        }
    }
}
