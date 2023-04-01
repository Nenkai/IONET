using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IONET.Core.Animation
{
    public class InterpolationHelper
    {
        public static float Lerp(float LHS, float RHS, float Weight)
        {
            return LHS * (1 - Weight) + RHS * Weight;
        }

        public static float CubicHermiteInterpolate(float frame, float FrameL, float frameR,
            float cf0, float cf1, float cf2, float cf3)
        {
            float t = (frame - FrameL) / (frameR - FrameL);
            return GetPointCubic(cf3, cf2, cf1, cf0, t);
        }

        private static float GetPointCubic(float cf0, float cf1, float cf2, float cf3, float t)
        {
            return (((cf0 * t + cf1) * t + cf2) * t + cf3);
        }

        public static float HermiteInterpolate(float frame, float frame1, float frame2,
          float p0, float p1,  float s0, float s1)
        {
            if (frame == frame1) return p0;
            if (frame == frame2) return p1;

            float t = (frame - frame1) / (frame2 - frame1);
            return GetPointHermite(p0, p1, s0, s1, t);
        }

        private static float GetPointHermite(float p0, float p1, float s0, float s1, float t)
        {
            float cf0 = (p0 * 2) + (p1 * -2) + (s0 * 1) + (s1 * 1);
            float cf1 = (p0 * -3) + (p1 * 3) + (s0 * -2) + (s1 * -1);
            float cf2 = (p0 * 0) + (p1 * 0) + (s0 * 1) + (s1 * 0);
            float cf3 = (p0 * 1) + (p1 * 0) + (s0 * 0) + (s1 * 0);
            return GetPointCubic(cf0, cf1, cf2, cf3, t);
        }

        public static float[] GetCubicSlopes(float time, float delta, float[] coef)
        {
            float outSlope = coef[1] / time;
            float param = coef[3] - (-2 * delta);
            float inSlope = param / time - outSlope;
            return new float[2] { inSlope, outSlope };
        }
    }
}
