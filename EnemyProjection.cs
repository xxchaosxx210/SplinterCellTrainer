using System;
using System.Numerics;

namespace SCTrainer;

public static class EnemyProjection
{
    public static bool TryProject(Vector3 point, Vector3 camera, int pitch, int yaw, int roll,
        float horizontalFov, double width, double height, out double x, out double y)
    {
        x = y = 0;
        if (width <= 0 || height <= 0 || !float.IsFinite(horizontalFov) || horizontalFov <= 5 || horizontalFov >= 175) return false;
        const double unit = Math.PI * 2 / 65536;
        double p = pitch * unit, a = yaw * unit, r = roll * unit;
        var forward = new Vector3((float)(Math.Cos(p) * Math.Cos(a)), (float)(Math.Cos(p) * Math.Sin(a)), (float)Math.Sin(p));
        var right = new Vector3((float)(Math.Sin(r) * Math.Sin(p) * Math.Cos(a) - Math.Cos(r) * Math.Sin(a)),
            (float)(Math.Sin(r) * Math.Sin(p) * Math.Sin(a) + Math.Cos(r) * Math.Cos(a)), (float)(-Math.Sin(r) * Math.Cos(p)));
        var up = Vector3.Cross(forward, right);
        var delta = point - camera;
        double depth = Vector3.Dot(delta, forward);
        if (!double.IsFinite(depth) || depth < 1) return false;
        double scale = width / (2 * Math.Tan(horizontalFov * Math.PI / 360));
        x = width / 2 + Vector3.Dot(delta, right) * scale / depth;
        y = height / 2 - Vector3.Dot(delta, up) * scale / depth;
        return double.IsFinite(x) && double.IsFinite(y);
    }
}
