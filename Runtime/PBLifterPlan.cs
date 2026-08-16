using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;

namespace PBLifter
{
    public static class PBLifterVersion
    {
        public const string Current = "0.1.1";
    }

    [AddComponentMenu("PB Lifter/PB Lifter Plan")]
    [DisallowMultipleComponent]
    public sealed class PBLifterPlan : MonoBehaviour, INDMFEditorOnly, ISerializationCallbackReceiver
    {
        public PBLifterOptions options = new PBLifterOptions();
        public List<PBLifterFieldTolerance> fieldTolerances = new List<PBLifterFieldTolerance>();
        public List<PBLifterPhysBoneExclusion> excludedPhysBones = new List<PBLifterPhysBoneExclusion>();

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            if (options == null) options = new PBLifterOptions();
            if (options.maxAffectedTransformsPerCandidate < 1) options.maxAffectedTransformsPerCandidate = 100;
            if (options.maxAffectedTransformsPerGroup < 2) options.maxAffectedTransformsPerGroup = 128;
            if ((options.highRiskRelaxations & HighRiskRelaxations.IgnoreAffectedBoneCountLimitLegacy) != 0)
            {
                options.highRiskRelaxations &= ~HighRiskRelaxations.IgnoreAffectedBoneCountLimitLegacy;
                options.maxAffectedTransformsPerGroup = 128;
            }
            if ((options.highRiskRelaxations & HighRiskRelaxations.IgnoreDisabledOrInactiveLegacy) == 0) return;
            options.highRiskRelaxations &= ~HighRiskRelaxations.IgnoreDisabledOrInactiveLegacy;
            options.highRiskRelaxations |= HighRiskRelaxations.IgnoreDisabledComponent |
                                         HighRiskRelaxations.IgnoreHierarchyInactive;
        }
    }

    [Serializable]
    public sealed class PBLifterOptions
    {
        public NumericAggregation aggregation = NumericAggregation.WeightedMean;
        public Weighting weighting = Weighting.AffectedTransformCount;
        public HighRiskRelaxations highRiskRelaxations = HighRiskRelaxations.None;
        public ClusteringMode clustering = ClusteringMode.CentroidBounded;
        public ToleranceInterpretation toleranceInterpretation = ToleranceInterpretation.Absolute;
        [Min(1)] public int maxAffectedTransformsPerCandidate = 100;
        [Min(2)] public int maxAffectedTransformsPerGroup = 128;
    }

    [Serializable]
    public sealed class PBLifterFieldTolerance
    {
        [HideInInspector] public string propertyPath;
        public bool allowDifference;
        [Min(0)] public float tolerance = 0.02f;
        public bool overrideToleranceInterpretation;
        public ToleranceInterpretation toleranceInterpretation = ToleranceInterpretation.Absolute;
    }

    [Serializable]
    public sealed class PBLifterPhysBoneExclusion
    {
        public Transform node;
        public PBLifterPhysBoneExclusionScope scope;
    }

    public enum NumericAggregation { ArithmeticMean, WeightedMean, Median }
    public enum Weighting { Equal, AffectedTransformCount }
    public enum ClusteringMode { CentroidBounded, CompleteLinkage }
    public enum ToleranceInterpretation { Absolute, Relative }
    public enum PBLifterPhysBoneExclusionScope { ThisNodeOnly, ThisNodeAndDescendants }

    [Flags]
    public enum HighRiskRelaxations
    {
        None = 0,
        IgnoreHumanoidBoneMapping = 1 << 0,
        IgnoreSelfActivationAnimation = 1 << 1,
        IgnoreAffectedBoneConstraints = 1 << 2,
        IgnoreAffectedBoneCountLimitLegacy = 1 << 3,
        IgnoreGrabbing = 1 << 4,
        IgnoreParameter = 1 << 5,
        IgnoreMultiChildMode = 1 << 6,
        IgnoreDisabledOrInactiveLegacy = 1 << 7,
        IgnoreDisabledComponent = 1 << 8,
        IgnoreHierarchyInactive = 1 << 9,
    }

    public static class PBLifterFieldLabels
    {
        private static readonly Dictionary<string, string[]> Labels = new Dictionary<string, string[]>
        {
            ["pull"] = new[] { "牵引力", "Pull" }, ["spring"] = new[] { "弹力", "Spring" }, ["stiffness"] = new[] { "刚度", "Stiffness" },
            ["gravity"] = new[] { "重力", "Gravity" }, ["gravityFalloff"] = new[] { "重力衰减", "Gravity Falloff" }, ["immobile"] = new[] { "不可动度", "Immobile" },
            ["boneOpacity"] = new[] { "骨骼透明度", "Bone Opacity" }, ["limitOpacity"] = new[] { "限制透明度", "Limit Opacity" },
            ["maxAngleX"] = new[] { "最大 X 角度", "Max Angle X" }, ["maxAngleZ"] = new[] { "最大 Z 角度", "Max Angle Z" },
            ["limitRotation"] = new[] { "旋转限制", "Limit Rotation" }, ["limitRotation.x"] = new[] { "旋转限制 X", "Limit Rotation X" },
            ["limitRotation.y"] = new[] { "旋转限制 Y", "Limit Rotation Y" }, ["limitRotation.z"] = new[] { "旋转限制 Z", "Limit Rotation Z" },
            ["radius"] = new[] { "碰撞半径", "Collision Radius" },
            ["stretchMotion"] = new[] { "拉伸运动", "Stretch Motion" }, ["maxStretch"] = new[] { "最大拉伸", "Max Stretch" }, ["maxSquish"] = new[] { "最大压缩", "Max Squish" },
            ["grabMovement"] = new[] { "抓取移动", "Grab Movement" }, ["endpointPosition"] = new[] { "末端位置", "Endpoint Position" },
            ["endpointPosition.x"] = new[] { "末端位置 X", "Endpoint Position X" }, ["endpointPosition.y"] = new[] { "末端位置 Y", "Endpoint Position Y" },
            ["endpointPosition.z"] = new[] { "末端位置 Z", "Endpoint Position Z" },
            ["pullCurve"] = new[] { "牵引力曲线", "Pull Curve" }, ["springCurve"] = new[] { "弹力曲线", "Spring Curve" }, ["stiffnessCurve"] = new[] { "刚度曲线", "Stiffness Curve" },
            ["gravityCurve"] = new[] { "重力曲线", "Gravity Curve" }, ["gravityFalloffCurve"] = new[] { "重力衰减曲线", "Gravity Falloff Curve" },
            ["immobileCurve"] = new[] { "不可动度曲线", "Immobile Curve" }, ["maxAngleXCurve"] = new[] { "最大 X 角度曲线", "Max Angle X Curve" },
            ["maxAngleZCurve"] = new[] { "最大 Z 角度曲线", "Max Angle Z Curve" }, ["limitRotationXCurve"] = new[] { "旋转限制 X 曲线", "Limit Rotation X Curve" },
            ["limitRotationYCurve"] = new[] { "旋转限制 Y 曲线", "Limit Rotation Y Curve" }, ["limitRotationZCurve"] = new[] { "旋转限制 Z 曲线", "Limit Rotation Z Curve" },
            ["radiusCurve"] = new[] { "碰撞半径曲线", "Collision Radius Curve" }, ["stretchMotionCurve"] = new[] { "拉伸运动曲线", "Stretch Motion Curve" },
            ["maxStretchCurve"] = new[] { "最大拉伸曲线", "Max Stretch Curve" }, ["maxSquishCurve"] = new[] { "最大压缩曲线", "Max Squish Curve" },
        };

        public static string Display(string propertyPath)
        {
            if (Labels.TryGetValue(propertyPath, out var labels))
                return PBLifterLocalization.Text($"{labels[0]}（{propertyPath}）", $"{labels[1]} ({propertyPath})");
            return propertyPath;
        }
    }
}
