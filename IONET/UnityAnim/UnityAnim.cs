using System;
using System.Collections.Generic;
using System.Text;

namespace IONET
{
    public class UnityAnim
    {
        class AnimationClip
        {
            public int m_ObjectHideFlags;
            public string m_Name = "Animation";
            public int serializedVersion = 6;
            public int m_Legacy = 0;
            public int m_Compressed = 0;
            public int m_UseHighQualityCurve = 1;
            public CurveList[] m_RotationCurves;
            public CurveList[] m_CompressedRotationCurves;
            public CurveList[] m_EulerCurves;
            public CurveList[] m_PositionCurves;
            public CurveList[] m_ScaleCurves;
            public CurveList[] m_FloatCurves;
            public CurveList[] m_PPtrCurves;
            public float m_SampleRate = 60;
            public int m_WrapMode = 0;
            public Bounds m_Bounds = new Bounds();
        }

        class Bounds
        {
            public Vector3 m_Center = new Vector3();
            public Vector3 m_Extent = new Vector3();
        }

        class CurveList
        {
            public int serializedVersion = 2;
            public Curve[] m_Curve;
        }

        class Curve
        {
            public int serializedVersion = 3;
            public float time;
            public Vector3 value;
            public Vector3 inSlope;
            public Vector3 outSlope;
            public int tangentMode;
            public int weightedMode;
            public Vector3 inWeight;
            public Vector3 outWeight;
        }

        class Vector3
        {
            public float x;
            public float y;
            public float z;
        }
    }
}
