using UnityEngine;

namespace SoftMasking
{
    // 对以 Vector4 表示的 Rect 进行各种操作（xMin、yMin、xMax、yMax）。
    public static class MathLib
    {
        public static Vector4 ToVector(Rect r) => new Vector4(r.xMin, r.yMin, r.xMax, r.yMax);
        public static Vector4 Div(Vector4 v, Vector2 s) => new Vector4(v.x / s.x, v.y / s.y, v.z / s.x, v.w / s.y);
        public static Vector2 Div(Vector2 v, Vector2 s) => new Vector2(v.x / s.x, v.y / s.y);
        public static Vector4 Mul(Vector4 v, Vector2 s) => new Vector4(v.x * s.x, v.y * s.y, v.z * s.x, v.w * s.y);
        public static Vector2 Size(Vector4 r) => new Vector2(r.z - r.x, r.w - r.y);

        public static Vector4 ApplyBorder(Vector4 v, Vector4 b) =>
            new Vector4(v.x + b.x, v.y + b.y, v.z - b.z, v.w - b.w);

        static Vector2 Min(Vector4 r) => new Vector2(r.x, r.y);
        static Vector2 Max(Vector4 r) => new Vector2(r.z, r.w);

        public static Vector2 Remap(Vector2 c, Vector4 from, Vector4 to)
        {
            var fromSize = Max(from) - Min(from);
            var toSize = Max(to) - Min(to);
            return Vector2.Scale(Div((c - Min(from)), fromSize), toSize) + Min(to);
        }

        public static bool Inside(Vector2 v, Vector4 r) =>
            v.x >= r.x && v.y >= r.y && v.x <= r.z && v.y <= r.w;
    }
}