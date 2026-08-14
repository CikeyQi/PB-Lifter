#if PBLIFTER_VRCSDK3
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(PBLifter.Editor.PBLifterPlugin))]

namespace PBLifter.Editor
{
    [RunsOnAllPlatforms]
    internal sealed class PBLifterPlugin : Plugin<PBLifterPlugin>
    {
        public override string DisplayName => "PB Lifter";
        public override string QualifiedName => "io.github.cikeyqi.pb-lifter";

        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing)
                .Run("PB Lifter: Cluster and merge PhysBones", PBLifterPass.Run);
        }
    }

    internal static class PBLifterPass
    {
        private static string L(string chinese, string english) => PBLifterLocalization.Text(chinese, english);

        internal sealed class ScanReport
        {
            internal readonly List<List<VRCPhysBone>> Groups = new List<List<VRCPhysBone>>();
            internal readonly List<ScanDiagnostic> Diagnostics = new List<ScanDiagnostic>();
            internal int EligibleCount;
            internal int SourceCount;
            internal int MergedCount => Groups.Sum(group => group.Count);
            internal int ResultCount => SourceCount - MergedCount + Groups.Count;
            internal int Reduction => SourceCount - ResultCount;
        }

        internal sealed class ScanDiagnostic
        {
            internal VRCPhysBone PhysBone;
            internal bool Planned;
            internal string Reason;
        }

        private sealed class AvatarAnimationIndex
        {
            private readonly HashSet<string> _activePaths = new HashSet<string>();
            private readonly HashSet<string> _positionOrRotationPaths = new HashSet<string>();

            internal AvatarAnimationIndex(GameObject avatarRoot)
            {
                foreach (var clip in CollectClips(avatarRoot).Distinct())
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type == typeof(Transform) && IsPositionOrRotation(binding.propertyName))
                        _positionOrRotationPaths.Add(binding.path);
                    if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive") _activePaths.Add(binding.path);
                }
            }

            internal bool HasPositionOrRotationAnimation(Transform transform, GameObject avatarRoot) =>
                _positionOrRotationPaths.Contains(AnimationUtility.CalculateTransformPath(transform, avatarRoot.transform));

            internal bool HasActivationAnimation(Transform transform, GameObject avatarRoot) =>
                _activePaths.Contains(AnimationUtility.CalculateTransformPath(transform, avatarRoot.transform));

            internal bool HasActivationAnimationOnAncestors(Transform transform, GameObject avatarRoot)
            {
                for (var current = transform; current != null; current = current.parent)
                {
                    if (_activePaths.Contains(AnimationUtility.CalculateTransformPath(current, avatarRoot.transform))) return true;
                    if (current == avatarRoot.transform) break;
                }
                return false;
            }

            private static bool IsPositionOrRotation(string propertyName) => propertyName == "m_LocalRotation.x" ||
                propertyName == "m_LocalRotation.y" || propertyName == "m_LocalRotation.z" ||
                propertyName == "m_LocalRotation.w" || propertyName == "m_LocalPosition.x" ||
                propertyName == "m_LocalPosition.y" || propertyName == "m_LocalPosition.z" ||
                propertyName == "localRotation.x" || propertyName == "localRotation.y" ||
                propertyName == "localRotation.z" || propertyName == "localRotation.w" ||
                propertyName == "localPosition.x" || propertyName == "localPosition.y" ||
                propertyName == "localPosition.z";

            private static IEnumerable<AnimationClip> CollectClips(GameObject avatarRoot)
            {
                foreach (var animator in avatarRoot.GetComponentsInChildren<Animator>(true))
                foreach (var clip in ClipsOf(animator.runtimeAnimatorController)) yield return clip;
                foreach (var descriptor in avatarRoot.GetComponentsInChildren<VRCAvatarDescriptor>(true))
                {
                    if (descriptor.baseAnimationLayers != null)
                        foreach (var layer in descriptor.baseAnimationLayers)
                        foreach (var clip in ClipsOf(layer.animatorController)) yield return clip;
                    if (descriptor.specialAnimationLayers != null)
                        foreach (var layer in descriptor.specialAnimationLayers)
                        foreach (var clip in ClipsOf(layer.animatorController)) yield return clip;
                }
            }

            private static IEnumerable<AnimationClip> ClipsOf(RuntimeAnimatorController controller) =>
                controller == null ? Enumerable.Empty<AnimationClip>() : controller.animationClips.Where(clip => clip != null);
        }

        internal static void Run(BuildContext context)
        {
            var plan = context.AvatarRootObject.GetComponent<PBLifterPlan>();
            if (plan == null) return;
            var report = Analyze(context.AvatarRootObject, plan);
            var animationIndex = new AvatarAnimationIndex(context.AvatarRootObject);
            foreach (var group in report.Groups) MergeCluster(group, plan, context.AvatarRootObject, animationIndex);
            Object.DestroyImmediate(plan);
        }

        internal static ScanReport Analyze(GameObject avatarRoot, PBLifterPlan plan)
        {
            var report = new ScanReport();
            var all = avatarRoot.GetComponentsInChildren<VRCPhysBone>(true);
            report.SourceCount = all.Length;
            var animationIndex = new AvatarAnimationIndex(avatarRoot);
            var diagnostics = all.ToDictionary(pb => pb, pb => new ScanDiagnostic { PhysBone = pb });
            var uniqueTargets = new HashSet<VRCPhysBone>(all.GroupBy(EffectiveRoot)
                .Where(group => group.Count() == 1).Select(group => group.First()));
            var candidates = new List<VRCPhysBone>();
            foreach (var physBone in all)
            {
                if (!uniqueTargets.Contains(physBone))
                {
                    diagnostics[physBone].Reason = L("同一有效根节点上存在多个 PhysBone；为避免改变执行顺序，不合并该目标。", "Multiple PhysBones share this effective root. It is not merged to preserve execution order.");
                    continue;
                }
                var reason = EligibilityReason(physBone, avatarRoot, plan, animationIndex);
                if (reason == null) candidates.Add(physBone);
                else diagnostics[physBone].Reason = reason;
            }
            report.EligibleCount = candidates.Count;
            foreach (var siblings in candidates.GroupBy(pb => new
                     {
                         parent = EffectiveRoot(pb).parent,
                         toggleRoot = GroupingToggleRoot(pb, avatarRoot, plan, animationIndex),
                     }))
                report.Groups.AddRange(Cluster(siblings, plan));
            var planned = new HashSet<VRCPhysBone>(report.Groups.SelectMany(group => group));
            foreach (var physBone in candidates)
            {
                if (planned.Contains(physBone))
                {
                    diagnostics[physBone].Planned = true;
                    diagnostics[physBone].Reason = L("已纳入计划合并组。", "Included in a planned merge group.");
                }
                else diagnostics[physBone].Reason = ExplainNotMerged(physBone, candidates, plan, avatarRoot, animationIndex);
            }
            report.Diagnostics.AddRange(all.Select(pb => diagnostics[pb]));
            return report;
        }

        internal static IEnumerable<string> NumericPropertyPaths(VRCPhysBone physBone) =>
            CompatibleNumericPropertyPaths.Where(path => new SerializedObject(physBone).FindProperty(path) != null);

        internal static IEnumerable<string> TolerablePropertyPaths(VRCPhysBone physBone) =>
            CompatibleNumericPropertyPaths.Concat(CompatibleCurvePropertyPaths)
                .Where(path => new SerializedObject(physBone).FindProperty(path) != null).Distinct().OrderBy(path => path);

        internal static bool HasEffectiveTolerance(PBLifterPlan plan) => plan.fieldTolerances
            .Any(field => field != null && field.allowDifference && field.tolerance > 0f);

        internal static IEnumerable<string> DifferingNumericFields(List<VRCPhysBone> group, PBLifterPlan plan)
        {
            foreach (var path in NumericPaths(new SerializedObject(group[0])).Distinct())
            {
                var tolerance = ToleranceFor(plan, path);
                if (tolerance == 0) continue;
                var first = new SerializedObject(group[0]).FindProperty(path);
                if (first == null) continue;
                if (group.Skip(1).Select(pb => new SerializedObject(pb).FindProperty(path))
                    .Any(property => property != null && ToVector(property) != ToVector(first)))
                    yield return L($"{PBLifterFieldLabels.Display(path)} → {Format(Aggregate(group, path, plan))}（容差 {tolerance:0.####}）", $"{PBLifterFieldLabels.Display(path)} → {Format(Aggregate(group, path, plan))} (tolerance {tolerance:0.####})");
            }
        }

        private static string EligibilityReason(VRCPhysBone pb, GameObject avatarRoot, PBLifterPlan plan,
            AvatarAnimationIndex animationIndex)
        {
            if (!pb || !pb.enabled || !pb.gameObject.activeInHierarchy) return L("组件已禁用，或其 GameObject 在层级中未激活。", "The component is disabled or its GameObject is inactive in the hierarchy.");
            if (!EffectiveRoot(pb).IsChildOf(avatarRoot.transform)) return L("有效根节点位于 Avatar Root 之外。", "The effective root is outside Avatar Root.");
            if (TryGetExclusionReason(pb, plan, out var exclusionReason)) return exclusionReason;
            if (!HasRelaxation(plan, HighRiskRelaxations.IgnoreHumanoidBoneMapping) &&
                IsHumanoidMappedBone(EffectiveRoot(pb), avatarRoot)) return L("有效根节点被 Humanoid Animator 骨骼映射引用。", "The effective root is mapped by a Humanoid Animator.");
            var basicCandidateFailure = BasicCandidateFailureReason(pb, plan);
            if (basicCandidateFailure != null) return basicCandidateFailure;
            if (!HasRelaxation(plan, HighRiskRelaxations.IgnoreSelfActivationAnimation) &&
                FindToggleRoot(pb.gameObject, avatarRoot, animationIndex) == pb.gameObject)
                return L("组件自身由激活动画切换。", "The component itself is toggled by an activation animation.");
            if (!HasRelaxation(plan, HighRiskRelaxations.IgnoreAffectedBoneConstraints) &&
                AffectedTransforms(pb).Any(t => t.GetComponent<IConstraint>() != null || t.GetComponent<VRCConstraintBase>() != null))
                return L("受影响骨骼上存在约束组件。", "An affected bone has a constraint component.");
            if (!HasRelaxation(plan, HighRiskRelaxations.IgnoreAffectedBoneCountLimit) && CountAffected(pb) >= 100)
                return L("受影响骨骼数达到单组件上限 100。", "The affected bone count reaches the per-component limit of 100.");
            return null;
        }

        private static bool TryGetExclusionReason(VRCPhysBone physBone, PBLifterPlan plan, out string reason)
        {
            foreach (var exclusion in plan.excludedPhysBones ?? Enumerable.Empty<PBLifterPhysBoneExclusion>())
            {
                if (exclusion == null || exclusion.node == null) continue;
                var matches = exclusion.scope == PBLifterPhysBoneExclusionScope.ThisNodeOnly
                    ? physBone.transform == exclusion.node
                    : physBone.transform == exclusion.node || physBone.transform.IsChildOf(exclusion.node);
                if (!matches) continue;
                var scope = exclusion.scope == PBLifterPhysBoneExclusionScope.ThisNodeOnly ? L("仅当前节点", "this node only") : L("包含子节点", "including descendants");
                reason = L($"已被自定义排除：节点“{exclusion.node.name}”（{scope}）。", $"Custom exclusion: node \"{exclusion.node.name}\" ({scope}).");
                return true;
            }
            reason = null;
            return false;
        }

        private static bool HasRelaxation(PBLifterPlan plan, HighRiskRelaxations relaxation) =>
            (plan.options.highRiskRelaxations & relaxation) != 0;

        private static bool IsHumanoidMappedBone(Transform root, GameObject avatarRoot)
        {
            foreach (var animator in avatarRoot.GetComponentsInChildren<Animator>(true))
            {
                if (animator.avatar == null || !animator.avatar.isHuman) continue;
                for (var index = 0; index < (int)HumanBodyBones.LastBone; index++)
                    if (animator.GetBoneTransform((HumanBodyBones)index) == root) return true;
            }
            return false;
        }

        private static GameObject FindToggleRoot(GameObject gameObject, GameObject avatarRoot, AvatarAnimationIndex animationIndex)
        {
            return FindToggleRoot(gameObject.transform, avatarRoot, animationIndex);
        }

        private static GameObject FindToggleRoot(Transform start, GameObject avatarRoot, AvatarAnimationIndex animationIndex)
        {
            for (var current = start; current != null; current = current.parent)
            {
                if (animationIndex.HasActivationAnimation(current, avatarRoot)) return current.gameObject;
                if (current == avatarRoot.transform) break;
            }
            return null;
        }

        private static GameObject GroupingToggleRoot(VRCPhysBone physBone, GameObject avatarRoot, PBLifterPlan plan,
            AvatarAnimationIndex animationIndex)
        {
            var toggleRoot = FindToggleRoot(physBone.gameObject, avatarRoot, animationIndex);
            if (toggleRoot != physBone.gameObject || !HasRelaxation(plan, HighRiskRelaxations.IgnoreSelfActivationAnimation))
                return toggleRoot;
            return FindToggleRoot(physBone.transform.parent, avatarRoot, animationIndex);
        }

        private static string ExplainNotMerged(VRCPhysBone physBone, IEnumerable<VRCPhysBone> candidates,
            PBLifterPlan plan, GameObject avatarRoot, AvatarAnimationIndex animationIndex)
        {
            var siblings = candidates.Where(pb => pb != physBone && EffectiveRoot(pb).parent == EffectiveRoot(physBone).parent).ToArray();
            if (siblings.Length == 0) return L("没有其他可合并候选项与其共享有效根节点的父级。", "No other merge candidate shares the effective root's parent.");
            var toggleRoot = GroupingToggleRoot(physBone, avatarRoot, plan, animationIndex);
            if (siblings.Any(sibling => GroupingToggleRoot(sibling, avatarRoot, plan, animationIndex) != toggleRoot))
                return L("激活动画的切换根不同。", "The activation-animation toggle roots differ.");
            foreach (var sibling in siblings)
            {
                var reason = IncompatibilityReason(physBone, sibling, plan);
                if (reason != null) return reason;
            }
            return L("与其他候选项分别兼容，但加入现有组后会超出容差范围或单组骨骼上限。", "It is individually compatible with other candidates, but adding it to an existing group would exceed the tolerance range or per-group bone limit.");
        }

        private static string IncompatibilityReason(VRCPhysBone a, VRCPhysBone b, PBLifterPlan plan)
        {
            var left = new CompatibilitySettings(a);
            var right = new CompatibilitySettings(b);
            if (left.IsActiveAndEnabled != right.IsActiveAndEnabled) return L("组件的启用或层级激活状态不同。", "Component enabled or hierarchy activation state differs.");
            if (left.Version != right.Version) return L("PhysBone 版本不同。", "PhysBone versions differ.");
            if (left.RootTransformParent != right.RootTransformParent) return L("有效根节点的父级不同。", "Effective root parents differ.");
            if (left.IgnoreOtherPhysBones != right.IgnoreOtherPhysBones) return L("忽略其他 PhysBone 的设置不同。", "Ignore Other PhysBones settings differ.");
            if (!SameVector(left.EndpointPosition, right.EndpointPosition, "endpointPosition", plan, true)) return ValueDifferenceReason("endpointPosition", plan);
            if (left.MultiChildType != right.MultiChildType) return L("多子节点模式不同。", "Multi-child modes differ.");
            if (left.IntegrationType != right.IntegrationType) return L("积分类型不同。", "Integration types differ.");
            if (!SameFloat(left.Pull, right.Pull, "pull", plan, true)) return ValueDifferenceReason("pull", plan);
            if (!SameCurve(left.PullCurve, right.PullCurve, plan, "pullCurve", true)) return ValueDifferenceReason("pullCurve", plan);
            if (!SameFloat(left.Spring, right.Spring, "spring", plan, true)) return ValueDifferenceReason("spring", plan);
            if (!SameCurve(left.SpringCurve, right.SpringCurve, plan, "springCurve", true)) return ValueDifferenceReason("springCurve", plan);
            if (!SameFloat(left.Stiffness, right.Stiffness, "stiffness", plan, true)) return ValueDifferenceReason("stiffness", plan);
            if (!SameCurve(left.StiffnessCurve, right.StiffnessCurve, plan, "stiffnessCurve", true)) return ValueDifferenceReason("stiffnessCurve", plan);
            if (!SameFloat(left.Gravity, right.Gravity, "gravity", plan, true)) return ValueDifferenceReason("gravity", plan);
            if (!SameCurve(left.GravityCurve, right.GravityCurve, plan, "gravityCurve", true)) return ValueDifferenceReason("gravityCurve", plan);
            if (!SameFloat(left.GravityFalloff, right.GravityFalloff, "gravityFalloff", plan, true)) return ValueDifferenceReason("gravityFalloff", plan);
            if (!SameCurve(left.GravityFalloffCurve, right.GravityFalloffCurve, plan, "gravityFalloffCurve", true)) return ValueDifferenceReason("gravityFalloffCurve", plan);
            if (left.ImmobileType != right.ImmobileType) return L("不可动类型不同。", "Immobile types differ.");
            if (!SameFloat(left.Immobile, right.Immobile, "immobile", plan, true)) return ValueDifferenceReason("immobile", plan);
            if (!SameCurve(left.ImmobileCurve, right.ImmobileCurve, plan, "immobileCurve", true)) return ValueDifferenceReason("immobileCurve", plan);
            if (left.LimitType != right.LimitType) return L("限制类型不同。", "Limit Types differ.");
            if (!SameFloat(left.MaxAngleX, right.MaxAngleX, "maxAngleX", plan, true)) return ValueDifferenceReason("maxAngleX", plan);
            if (!SameCurve(left.MaxAngleXCurve, right.MaxAngleXCurve, plan, "maxAngleXCurve", true)) return ValueDifferenceReason("maxAngleXCurve", plan);
            if (!SameFloat(left.MaxAngleZ, right.MaxAngleZ, "maxAngleZ", plan, true)) return ValueDifferenceReason("maxAngleZ", plan);
            if (!SameCurve(left.MaxAngleZCurve, right.MaxAngleZCurve, plan, "maxAngleZCurve", true)) return ValueDifferenceReason("maxAngleZCurve", plan);
            if (!SameVector(left.LimitRotation, right.LimitRotation, "limitRotation", plan, true)) return ValueDifferenceReason("limitRotation", plan);
            if (!SameCurve(left.LimitRotationXCurve, right.LimitRotationXCurve, plan, "limitRotationXCurve", true)) return ValueDifferenceReason("limitRotationXCurve", plan);
            if (!SameCurve(left.LimitRotationYCurve, right.LimitRotationYCurve, plan, "limitRotationYCurve", true)) return ValueDifferenceReason("limitRotationYCurve", plan);
            if (!SameCurve(left.LimitRotationZCurve, right.LimitRotationZCurve, plan, "limitRotationZCurve", true)) return ValueDifferenceReason("limitRotationZCurve", plan);
            if (!SameFloat(left.Radius, right.Radius, "radius", plan, true)) return ValueDifferenceReason("radius", plan);
            if (!SameCurve(left.RadiusCurve, right.RadiusCurve, plan, "radiusCurve", true)) return ValueDifferenceReason("radiusCurve", plan);
            if (left.AllowCollision != right.AllowCollision) return L("碰撞权限不同。", "Collision permissions differ.");
            if (!left.Colliders.SetEquals(right.Colliders)) return L("碰撞体列表不同。", "Collider lists differ.");
            if (!SameFloat(left.StretchMotion, right.StretchMotion, "stretchMotion", plan, true)) return ValueDifferenceReason("stretchMotion", plan);
            if (!SameCurve(left.StretchMotionCurve, right.StretchMotionCurve, plan, "stretchMotionCurve", true)) return ValueDifferenceReason("stretchMotionCurve", plan);
            if (!SameFloat(left.MaxStretch, right.MaxStretch, "maxStretch", plan, true)) return ValueDifferenceReason("maxStretch", plan);
            if (!SameCurve(left.MaxStretchCurve, right.MaxStretchCurve, plan, "maxStretchCurve", true)) return ValueDifferenceReason("maxStretchCurve", plan);
            if (!SameFloat(left.MaxSquish, right.MaxSquish, "maxSquish", plan, true)) return ValueDifferenceReason("maxSquish", plan);
            if (!SameCurve(left.MaxSquishCurve, right.MaxSquishCurve, plan, "maxSquishCurve", true)) return ValueDifferenceReason("maxSquishCurve", plan);
            if (left.AllowGrabbing != right.AllowGrabbing) return L("抓取权限不同。", "Grabbing permissions differ.");
            if (left.AllowPosing != right.AllowPosing) return L("姿势权限不同。", "Posing permissions differ.");
            if (left.SnapToHand != right.SnapToHand) return L("吸附至手部设置不同。", "Snap To Hand settings differ.");
            if (!SameFloat(left.GrabMovement, right.GrabMovement, "grabMovement", plan, true)) return ValueDifferenceReason("grabMovement", plan);
            if (left.IsAnimated != right.IsAnimated) return L("动画驱动设置不同。", "Is Animated settings differ.");
            if (left.ResetWhenDisabled != right.ResetWhenDisabled) return L("禁用时重置设置不同。", "Reset When Disabled settings differ.");
            if (left.Parameter != right.Parameter) return L("参数名称不同。", "Parameter names differ.");
            if (left.ChainLength != right.ChainLength) return L("骨骼链长度不同。", "Bone chain lengths differ.");
            return null;
        }

        private static string ValueDifferenceReason(string propertyPath, PBLifterPlan plan) => HasEffectiveTolerance(plan, propertyPath)
            ? L($"{PBLifterFieldLabels.Display(propertyPath)} 的差异超出已启用的容差。", $"{PBLifterFieldLabels.Display(propertyPath)} differs beyond its enabled tolerance.")
            : L($"{PBLifterFieldLabels.Display(propertyPath)} 不一致。", $"{PBLifterFieldLabels.Display(propertyPath)} differs.");

        private static readonly string[] CompatibleNumericPropertyPaths =
        {
            "endpointPosition", "pull", "spring", "stiffness", "gravity", "gravityFalloff", "immobile",
            "maxAngleX", "maxAngleZ", "limitRotation", "radius", "stretchMotion", "maxStretch",
            "maxSquish", "grabMovement",
        };

        private static readonly string[] CompatibleCurvePropertyPaths =
        {
            "pullCurve", "springCurve", "stiffnessCurve", "gravityCurve", "gravityFalloffCurve",
            "immobileCurve", "maxAngleXCurve", "maxAngleZCurve", "limitRotationXCurve",
            "limitRotationYCurve", "limitRotationZCurve", "radiusCurve", "stretchMotionCurve",
            "maxStretchCurve", "maxSquishCurve",
        };

        private static string BasicCandidateFailureReason(VRCPhysBone physBone, PBLifterPlan plan)
        {
            var grabbing = PermissionFlags(physBone.allowGrabbing, physBone.grabFilter);
            if (physBone.multiChildType != VRCPhysBoneBase.MultiChildType.Ignore &&
                !HasRelaxation(plan, HighRiskRelaxations.IgnoreMultiChildMode)) return L("多子节点模式不是 Ignore。", "The multi-child mode is not Ignore.");
            if ((grabbing.self || grabbing.others) && !HasRelaxation(plan, HighRiskRelaxations.IgnoreGrabbing))
            {
                if (grabbing.self && grabbing.others) return L("允许自己和他人抓取。", "Grabbing is allowed for self and others.");
                return grabbing.self ? L("允许自己抓取。", "Grabbing is allowed for self.") : L("允许他人抓取。", "Grabbing is allowed for others.");
            }
            if (!string.IsNullOrEmpty(physBone.parameter) && !HasRelaxation(plan, HighRiskRelaxations.IgnoreParameter))
                return L($"已设置参数名称“{physBone.parameter}”。", $"A parameter name is set: \"{physBone.parameter}\".");
            return null;
        }

        private static bool CompatibilityMatches(VRCPhysBone leftPhysBone, VRCPhysBone rightPhysBone,
            PBLifterPlan plan, bool applyTolerance)
        {
            var left = new CompatibilitySettings(leftPhysBone);
            var right = new CompatibilitySettings(rightPhysBone);

            return left.IsActiveAndEnabled == right.IsActiveAndEnabled &&
                   left.Version == right.Version &&
                   left.RootTransformParent == right.RootTransformParent &&
                   left.IgnoreOtherPhysBones == right.IgnoreOtherPhysBones &&
                   SameVector(left.EndpointPosition, right.EndpointPosition, "endpointPosition", plan, applyTolerance) &&
                   left.MultiChildType == right.MultiChildType &&
                   left.IntegrationType == right.IntegrationType &&
                   SameFloat(left.Pull, right.Pull, "pull", plan, applyTolerance) && SameCurve(left.PullCurve, right.PullCurve, plan, "pullCurve", applyTolerance) &&
                   SameFloat(left.Spring, right.Spring, "spring", plan, applyTolerance) && SameCurve(left.SpringCurve, right.SpringCurve, plan, "springCurve", applyTolerance) &&
                   SameFloat(left.Stiffness, right.Stiffness, "stiffness", plan, applyTolerance) && SameCurve(left.StiffnessCurve, right.StiffnessCurve, plan, "stiffnessCurve", applyTolerance) &&
                   SameFloat(left.Gravity, right.Gravity, "gravity", plan, applyTolerance) && SameCurve(left.GravityCurve, right.GravityCurve, plan, "gravityCurve", applyTolerance) &&
                   SameFloat(left.GravityFalloff, right.GravityFalloff, "gravityFalloff", plan, applyTolerance) && SameCurve(left.GravityFalloffCurve, right.GravityFalloffCurve, plan, "gravityFalloffCurve", applyTolerance) &&
                   left.ImmobileType == right.ImmobileType &&
                   SameFloat(left.Immobile, right.Immobile, "immobile", plan, applyTolerance) && SameCurve(left.ImmobileCurve, right.ImmobileCurve, plan, "immobileCurve", applyTolerance) &&
                   left.LimitType == right.LimitType &&
                   SameFloat(left.MaxAngleX, right.MaxAngleX, "maxAngleX", plan, applyTolerance) && SameCurve(left.MaxAngleXCurve, right.MaxAngleXCurve, plan, "maxAngleXCurve", applyTolerance) &&
                   SameFloat(left.MaxAngleZ, right.MaxAngleZ, "maxAngleZ", plan, applyTolerance) && SameCurve(left.MaxAngleZCurve, right.MaxAngleZCurve, plan, "maxAngleZCurve", applyTolerance) &&
                   SameVector(left.LimitRotation, right.LimitRotation, "limitRotation", plan, applyTolerance) &&
                   SameCurve(left.LimitRotationXCurve, right.LimitRotationXCurve, plan, "limitRotationXCurve", applyTolerance) &&
                   SameCurve(left.LimitRotationYCurve, right.LimitRotationYCurve, plan, "limitRotationYCurve", applyTolerance) &&
                   SameCurve(left.LimitRotationZCurve, right.LimitRotationZCurve, plan, "limitRotationZCurve", applyTolerance) &&
                   SameFloat(left.Radius, right.Radius, "radius", plan, applyTolerance) && SameCurve(left.RadiusCurve, right.RadiusCurve, plan, "radiusCurve", applyTolerance) &&
                   left.AllowCollision == right.AllowCollision && left.Colliders.SetEquals(right.Colliders) &&
                   SameFloat(left.StretchMotion, right.StretchMotion, "stretchMotion", plan, applyTolerance) && SameCurve(left.StretchMotionCurve, right.StretchMotionCurve, plan, "stretchMotionCurve", applyTolerance) &&
                   SameFloat(left.MaxStretch, right.MaxStretch, "maxStretch", plan, applyTolerance) && SameCurve(left.MaxStretchCurve, right.MaxStretchCurve, plan, "maxStretchCurve", applyTolerance) &&
                   SameFloat(left.MaxSquish, right.MaxSquish, "maxSquish", plan, applyTolerance) && SameCurve(left.MaxSquishCurve, right.MaxSquishCurve, plan, "maxSquishCurve", applyTolerance) &&
                   left.AllowGrabbing == right.AllowGrabbing && left.AllowPosing == right.AllowPosing &&
                   left.SnapToHand == right.SnapToHand &&
                   SameFloat(left.GrabMovement, right.GrabMovement, "grabMovement", plan, applyTolerance) &&
                   left.IsAnimated == right.IsAnimated && left.ResetWhenDisabled == right.ResetWhenDisabled &&
                   left.Parameter == right.Parameter && left.ChainLength == right.ChainLength;
        }

        private static bool SameFloat(float left, float right, string path, PBLifterPlan plan, bool applyTolerance)
        {
            if (HasEffectiveTolerance(plan, path) && !applyTolerance) return true;
            if (!HasEffectiveTolerance(plan, path)) return left.Equals(right);
            return CloseFloat(left, right, ToleranceFor(plan, path), ToleranceModeFor(plan, path));
        }

        private static bool SameVector(Vector3 left, Vector3 right, string path, PBLifterPlan plan, bool applyTolerance)
        {
            if (HasEffectiveTolerance(plan, path) && !applyTolerance) return true;
            if (!HasEffectiveTolerance(plan, path)) return left.Equals(right);
            var tolerance = ToleranceFor(plan, path);
            var mode = ToleranceModeFor(plan, path);
            return CloseFloat(left.x, right.x, tolerance, mode) && CloseFloat(left.y, right.y, tolerance, mode) &&
                   CloseFloat(left.z, right.z, tolerance, mode);
        }

        private static bool CloseFloat(float left, float right, float tolerance, ToleranceInterpretation mode)
        {
            var allowed = mode == ToleranceInterpretation.Relative
                ? tolerance * Mathf.Max(0.0001f, Mathf.Abs(left), Mathf.Abs(right))
                : tolerance;
            return Mathf.Abs(left - right) <= allowed + Mathf.Max(0.0000001f, allowed * 0.000001f);
        }

        private static bool SameCurve(AnimationCurve left, AnimationCurve right, PBLifterPlan plan, string propertyPath,
            bool applyTolerance)
        {
            if (!HasEffectiveTolerance(plan, propertyPath)) return Equals(left, right);
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.length != right.length ||
                left.preWrapMode != right.preWrapMode || left.postWrapMode != right.postWrapMode) return false;
            for (var index = 0; index < left.length; index++)
            {
                var a = left.keys[index];
                var b = right.keys[index];
                if (a.weightedMode != b.weightedMode) return false;
                if (!applyTolerance) continue;
                var tolerance = ToleranceFor(plan, propertyPath);
                var mode = ToleranceModeFor(plan, propertyPath);
                if (!CloseFloat(a.time, b.time, tolerance, mode) || !CloseFloat(a.value, b.value, tolerance, mode) ||
                    !CloseFloat(a.inTangent, b.inTangent, tolerance, mode) || !CloseFloat(a.outTangent, b.outTangent, tolerance, mode) ||
                    !CloseFloat(a.inWeight, b.inWeight, tolerance, mode) || !CloseFloat(a.outWeight, b.outWeight, tolerance, mode)) return false;
            }
            return true;
        }

        private static (bool self, bool others) PermissionFlags(VRCPhysBoneBase.AdvancedBool allow,
            VRCPhysBoneBase.PermissionFilter filter) => allow switch
        {
            VRCPhysBoneBase.AdvancedBool.False => (false, false),
            VRCPhysBoneBase.AdvancedBool.True => (true, true),
            VRCPhysBoneBase.AdvancedBool.Other => (filter.allowSelf, filter.allowOthers),
            _ => throw new ArgumentOutOfRangeException(nameof(allow), allow, null),
        };

        private readonly struct CompatibilitySettings
        {
            internal readonly bool IsActiveAndEnabled;
            internal readonly VRCPhysBoneBase.Version Version;
            internal readonly Transform RootTransformParent;
            internal readonly bool IgnoreOtherPhysBones;
            internal readonly Vector3 EndpointPosition;
            internal readonly VRCPhysBoneBase.MultiChildType MultiChildType;
            internal readonly VRCPhysBoneBase.IntegrationType IntegrationType;
            internal readonly float Pull, Spring, Stiffness, Gravity, GravityFalloff, Immobile, MaxAngleX, MaxAngleZ, Radius, StretchMotion, MaxStretch, MaxSquish, GrabMovement;
            internal readonly AnimationCurve PullCurve, SpringCurve, StiffnessCurve, GravityCurve, GravityFalloffCurve, ImmobileCurve, MaxAngleXCurve, MaxAngleZCurve, LimitRotationXCurve, LimitRotationYCurve, LimitRotationZCurve, RadiusCurve, StretchMotionCurve, MaxStretchCurve, MaxSquishCurve;
            internal readonly VRCPhysBoneBase.ImmobileType ImmobileType;
            internal readonly VRCPhysBoneBase.LimitType LimitType;
            internal readonly Vector3 LimitRotation;
            internal readonly (bool self, bool others) AllowCollision, AllowGrabbing, AllowPosing;
            internal readonly HashSet<VRCPhysBoneColliderBase> Colliders;
            internal readonly bool SnapToHand, IsAnimated, ResetWhenDisabled;
            internal readonly string Parameter;
            internal readonly int ChainLength;

            internal CompatibilitySettings(VRCPhysBone physBone)
            {
                IsActiveAndEnabled = physBone.enabled && physBone.gameObject.activeInHierarchy;
                Version = physBone.version;
                var root = EffectiveRoot(physBone);
                RootTransformParent = root.parent;
#if PBLIFTER_VRCSDK3_IGNORE_OTHER_PHYSBONE
                IgnoreOtherPhysBones = physBone.ignoreOtherPhysBones;
#else
                IgnoreOtherPhysBones = false;
#endif
                EndpointPosition = physBone.endpointPosition;
                MultiChildType = physBone.multiChildType;
                IntegrationType = physBone.integrationType;
                Pull = physBone.pull; PullCurve = NormalizeCurve(physBone.pullCurve, Pull);
                Spring = physBone.spring; SpringCurve = NormalizeCurve(physBone.springCurve, Spring);
                Stiffness = physBone.stiffness; StiffnessCurve = NormalizeCurve(physBone.stiffnessCurve, Stiffness);
                Gravity = physBone.gravity; GravityCurve = NormalizeCurve(physBone.gravityCurve, Gravity);
                if (Gravity == 0f) { GravityFalloff = 0f; GravityFalloffCurve = null; }
                else { GravityFalloff = physBone.gravityFalloff; GravityFalloffCurve = NormalizeCurve(physBone.gravityFalloffCurve, GravityFalloff); }
                ImmobileType = physBone.immobileType; Immobile = physBone.immobile; ImmobileCurve = NormalizeCurve(physBone.immobileCurve, Immobile);
                LimitType = physBone.limitType; MaxAngleX = physBone.maxAngleX; MaxAngleXCurve = NormalizeCurve(physBone.maxAngleXCurve, MaxAngleX);
                MaxAngleZ = physBone.maxAngleZ; MaxAngleZCurve = NormalizeCurve(physBone.maxAngleZCurve, MaxAngleZ);
                LimitRotation = physBone.limitRotation;
                LimitRotationXCurve = NormalizeCurve(physBone.limitRotationXCurve, LimitRotation.x);
                LimitRotationYCurve = NormalizeCurve(physBone.limitRotationYCurve, LimitRotation.y);
                LimitRotationZCurve = NormalizeCurve(physBone.limitRotationZCurve, LimitRotation.z);
                Radius = physBone.radius; RadiusCurve = NormalizeCurve(physBone.radiusCurve, Radius);
                AllowCollision = PermissionFlags(physBone.allowCollision, physBone.collisionFilter);
                Colliders = new HashSet<VRCPhysBoneColliderBase>(physBone.colliders);
                StretchMotion = physBone.stretchMotion; StretchMotionCurve = NormalizeCurve(physBone.stretchMotionCurve, StretchMotion);
                MaxStretch = physBone.maxStretch; MaxStretchCurve = NormalizeCurve(physBone.maxStretchCurve, MaxStretch);
                MaxSquish = physBone.maxSquish; MaxSquishCurve = NormalizeCurve(physBone.maxSquishCurve, MaxSquish);
                AllowGrabbing = PermissionFlags(physBone.allowGrabbing, physBone.grabFilter);
                AllowPosing = PermissionFlags(physBone.allowPosing, physBone.poseFilter);
                SnapToHand = physBone.snapToHand; GrabMovement = physBone.grabMovement;
                IsAnimated = physBone.isAnimated; ResetWhenDisabled = physBone.resetWhenDisabled; Parameter = physBone.parameter ?? "";
                ChainLength = PullCurve != null || SpringCurve != null || StiffnessCurve != null || GravityCurve != null ||
                              GravityFalloffCurve != null || ImmobileCurve != null || MaxAngleXCurve != null ||
                              MaxAngleZCurve != null || LimitRotationXCurve != null || LimitRotationYCurve != null ||
                              LimitRotationZCurve != null || RadiusCurve != null || StretchMotionCurve != null ||
                              MaxStretchCurve != null || MaxSquishCurve != null
                    ? ComputeChainLength(root, physBone.ignoreTransforms, EndpointPosition) : -1;
            }

            private static AnimationCurve NormalizeCurve(AnimationCurve curve, float value) =>
                value == 0f || curve == null || curve.length == 0 ? null : curve;
        }


        internal static IEnumerable<List<VRCPhysBone>> Cluster(IEnumerable<VRCPhysBone> source,
            PBLifterPlan plan)
        {
            var clusters = new List<List<VRCPhysBone>>();
            foreach (var candidate in source)
            {
                var added = false;
                foreach (var cluster in clusters)
                {
                    if (!CanJoinCluster(candidate, cluster, plan)) continue;
                    cluster.Add(candidate);
                    added = true;
                    break;
                }
                if (!added) clusters.Add(new List<VRCPhysBone> { candidate });
            }

            foreach (var cluster in clusters)
            foreach (var bucket in PartitionByAffectedTransformCount(cluster, 128))
                if (bucket.Count > 1) yield return bucket;
        }

        private static IEnumerable<List<VRCPhysBone>> PartitionByAffectedTransformCount(IEnumerable<VRCPhysBone> source,
            int maxAffectedTransforms)
        {
            var groups = new List<List<VRCPhysBone>>();
            var totals = new List<int>();
            foreach (var physBone in source.OrderByDescending(CountAffected))
            {
                var size = CountAffected(physBone);
                if (size > maxAffectedTransforms) continue;
                var groupIndex = totals.FindIndex(total => total + size <= maxAffectedTransforms);
                if (groupIndex < 0)
                {
                    groups.Add(new List<VRCPhysBone> { physBone });
                    totals.Add(size);
                }
                else
                {
                    groups[groupIndex].Add(physBone);
                    totals[groupIndex] += size;
                }
            }
            return groups;
        }

        private static bool WithinTolerance(VRCPhysBone candidate, List<VRCPhysBone> cluster,
            PBLifterPlan plan)
        {
            if (!CompatibilityMatches(cluster[0], candidate, plan, applyTolerance: false)) return false;
            var candidateSerialized = new SerializedObject(candidate);
            foreach (var path in CompatibleNumericPropertyPaths)
            {
                if (!HasEffectiveTolerance(plan, path)) continue;
                var value = candidateSerialized.FindProperty(path);
                if (value == null) return false;
                var centre = Aggregate(cluster, path, plan);
                if (!Close(value, centre, plan, path)) return false;
            }
            foreach (var path in CompatibleCurvePropertyPaths)
            {
                if (!HasEffectiveTolerance(plan, path)) continue;
                var value = candidateSerialized.FindProperty(path)?.animationCurveValue;
                var centre = AggregateCurve(cluster, path, plan);
                if (!CloseCurve(value, centre, plan, path)) return false;
            }
            return true;
        }

        private static bool CanJoinCluster(VRCPhysBone candidate, List<VRCPhysBone> cluster, PBLifterPlan plan)
        {
            if (plan.options.clustering == ClusteringMode.CentroidBounded)
                return WithinTolerance(candidate, cluster, plan);
            return cluster.All(member => WithinTolerance(candidate, new List<VRCPhysBone> { member }, plan));
        }

        private static void MergeCluster(List<VRCPhysBone> sources, PBLifterPlan plan, GameObject avatarRoot,
            AvatarAnimationIndex animationIndex)
        {
            var sourceRootParent = EffectiveRoot(sources[0]).parent;
            if (sourceRootParent == null) return;
            var sourceRoots = sources.Select(EffectiveRoot).ToHashSet();
            Transform mergedRoot;
            if (sources.Count == sourceRootParent.childCount &&
                !animationIndex.HasPositionOrRotationAnimation(sourceRootParent, avatarRoot))
            {
                mergedRoot = sourceRootParent;
            }
            else
            {
                mergedRoot = new GameObject("PB Lifter PhysBone Root").transform;
                mergedRoot.SetParent(sourceRootParent, false);
                var allPhysBones = avatarRoot.GetComponentsInChildren<VRCPhysBoneBase>(true);
                foreach (var physBone in allPhysBones.Where(physBone => physBone.ignoreTransforms.Any(sourceRoots.Contains)))
                {
                    physBone.ignoreTransforms.RemoveAll(sourceRoots.Contains);
                    physBone.ignoreTransforms.Add(mergedRoot);
                }
                foreach (var source in sources) EffectiveRoot(source).SetParent(mergedRoot, true);
            }

            var toggleRoot = GroupingToggleRoot(sources[0], avatarRoot, plan, animationIndex);
            var mergedHost = new GameObject("PB Lifter Auto Merged PhysBone");
            mergedHost.transform.SetParent((toggleRoot ?? avatarRoot).transform, false);
            var merged = mergedHost.AddComponent<VRCPhysBone>();
            EditorUtility.CopySerialized(sources[0], merged);
            var serialized = new SerializedObject(merged);
            foreach (var path in NumericPaths(serialized).Where(path => HasEffectiveTolerance(plan, path)).ToArray())
            {
                var property = serialized.FindProperty(path);
                if (property != null) SetNumeric(property, Aggregate(sources, path, plan));
            }
            foreach (var path in CompatibleCurvePropertyPaths.Where(path => HasEffectiveTolerance(plan, path)))
            {
                var property = serialized.FindProperty(path);
                if (property != null) property.animationCurveValue = AggregateCurve(sources, path, plan);
            }
            foreach (var source in sources) source.InitTransforms(true);
            var maxChainLength = sources.Max(BoneChainLength);
            foreach (var path in CompatibleCurvePropertyPaths)
            {
                var property = serialized.FindProperty(path);
                if (property == null) continue;
                var chainLength = path == "radiusCurve" ? maxChainLength : maxChainLength - 1;
                property.animationCurveValue = FixCurve(property.animationCurveValue, chainLength);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            merged.rootTransform = mergedRoot;
            merged.ignoreTransforms = sources.SelectMany(pb => pb.ignoreTransforms).Where(t => t != null).Distinct().ToList();
            merged.multiChildType = VRCPhysBoneBase.MultiChildType.Ignore;
            merged.isAnimated = sources.Any(source => source.isAnimated);
            foreach (var source in sources) Object.DestroyImmediate(source);
            Debug.Log($"PB Lifter merged {sources.Count} PhysBones into '{mergedHost.name}'.", merged);
        }

        private static int BoneChainLength(VRCPhysBoneBase physBone)
        {
            var length = physBone.maxBoneChainIndex;
            return physBone.endpointPosition != Vector3.zero ? length + 1 : length;
        }

        private static Transform EffectiveRoot(VRCPhysBone pb) => pb.rootTransform != null ? pb.rootTransform : pb.transform;

        private static int ComputeChainLength(Transform root, List<Transform> ignoredTransforms, Vector3 endpointPosition)
        {
            var ignored = new HashSet<Transform>(ignoredTransforms.Where(transform => transform != null));
            var count = CountDepth(root);
            return endpointPosition != Vector3.zero ? count + 1 : count;

            int CountDepth(Transform transform)
            {
                var maxChildCount = 0;
                for (var index = 0; index < transform.childCount; index++)
                {
                    var child = transform.GetChild(index);
                    if (ignored.Contains(child)) continue;
                    maxChildCount = Mathf.Max(maxChildCount, CountDepth(child));
                }
                return maxChildCount + 1;
            }
        }

        private static AnimationCurve FixCurve(AnimationCurve curve, int chainLength)
        {
            if (curve == null || curve.length == 0) return new AnimationCurve();
            if (chainLength <= 0) return AnimationCurve.Constant(0, 1, curve.Evaluate(0));
            var offset = 1f / (chainLength + 1f);
            var tangentRatio = (chainLength + 1f) / chainLength;
            var keys = curve.keys;
            for (var index = 0; index < keys.Length; index++)
            {
                var key = keys[index];
                key.time = Mathf.LerpUnclamped(offset, 1f, key.time);
                key.inTangent *= tangentRatio;
                key.outTangent *= tangentRatio;
                keys[index] = key;
            }
            return new AnimationCurve(keys);
        }

        private static int CountAffected(VRCPhysBone pb)
        {
            return AffectedTransforms(pb).Count();
        }

        internal static int CountAffectedForDisplay(VRCPhysBone pb) => CountAffected(pb);

        private static IEnumerable<Transform> AffectedTransforms(VRCPhysBone pb)
        {
            var ignored = new HashSet<Transform>(pb.ignoreTransforms.Where(t => t != null));
            return Descendants(EffectiveRoot(pb));
            IEnumerable<Transform> Descendants(Transform transform)
            {
                yield return transform;
                for (var i = 0; i < transform.childCount; i++)
                {
                    var child = transform.GetChild(i);
                    if (ignored.Contains(child)) continue;
                    foreach (var descendant in Descendants(child)) yield return descendant;
                }
            }
        }

        private static IEnumerable<string> NumericPaths(SerializedObject serialized) => CompatibleNumericPropertyPaths
            .Where(path => serialized.FindProperty(path) != null);

        private readonly struct NumericValue
        {
            internal readonly SerializedPropertyType Type;
            internal readonly Vector4 Value;
            internal NumericValue(SerializedProperty p) { Type = p.propertyType; Value = ToVector(p); }
            internal NumericValue(SerializedPropertyType type, Vector4 value) { Type = type; Value = value; }
        }

        private static NumericValue Aggregate(IEnumerable<VRCPhysBone> source, string path, PBLifterPlan plan)
        {
            var samples = source.Select(pb => new SerializedObject(pb).FindProperty(path))
                .Where(property => property != null).ToArray();
            if (samples.Length == 0) return new NumericValue(SerializedPropertyType.Float, Vector4.zero);
            if (plan.options.aggregation == NumericAggregation.Median)
            {
                var values = samples.Select(ToVector).ToArray();
                float Middle(Func<Vector4, float> selector)
                {
                    var ordered = values.Select(selector).OrderBy(value => value).ToArray();
                    var middle = ordered.Length / 2;
                    return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) * 0.5f : ordered[middle];
                }
                var x = Middle(value => value.x);
                var y = Middle(value => value.y);
                var z = Middle(value => value.z);
                var w = Middle(value => value.w);
                return new NumericValue(samples[0].propertyType, new Vector4(x, y, z, w));
            }
            var sum = Vector4.zero;
            var totalWeight = 0f;
            SerializedPropertyType type = SerializedPropertyType.Float;
            foreach (var pb in source)
            {
                var property = new SerializedObject(pb).FindProperty(path);
                if (property == null) continue;
                type = property.propertyType;
                var weight = plan.options.aggregation == NumericAggregation.WeightedMean &&
                             plan.options.weighting == Weighting.AffectedTransformCount ? CountAffected(pb) : 1;
                sum += ToVector(property) * weight;
                totalWeight += weight;
            }
            return new NumericValue(type, sum / Mathf.Max(totalWeight, 1f));
        }

        internal static float ToleranceFor(PBLifterPlan plan, string propertyPath)
        {
            var field = plan.fieldTolerances.FirstOrDefault(x => x.propertyPath == propertyPath);
            return field != null && field.allowDifference ? Mathf.Max(0, field.tolerance) : 0;
        }

        private static bool HasEffectiveTolerance(PBLifterPlan plan, string propertyPath) =>
            ToleranceFor(plan, propertyPath) > 0f;

        private static ToleranceInterpretation ToleranceModeFor(PBLifterPlan plan, string propertyPath)
        {
            var field = plan.fieldTolerances.FirstOrDefault(x => x.propertyPath == propertyPath);
            return field != null && field.overrideToleranceInterpretation
                ? field.toleranceInterpretation
                : plan.options.toleranceInterpretation;
        }

        private static bool Close(SerializedProperty value, NumericValue centre, PBLifterPlan plan, string propertyPath)
        {
            var tolerance = ToleranceFor(plan, propertyPath);
            var current = ToVector(value);
            var mode = ToleranceModeFor(plan, propertyPath);
            return CloseFloat(current.x, centre.Value.x, tolerance, mode) && CloseFloat(current.y, centre.Value.y, tolerance, mode) &&
                   CloseFloat(current.z, centre.Value.z, tolerance, mode) && CloseFloat(current.w, centre.Value.w, tolerance, mode);
        }

        private static bool CloseCurve(AnimationCurve value, AnimationCurve centre, PBLifterPlan plan, string propertyPath)
        {
            if (ReferenceEquals(value, centre)) return true;
            if (value == null || centre == null || value.length != centre.length ||
                value.preWrapMode != centre.preWrapMode || value.postWrapMode != centre.postWrapMode) return false;
            var tolerance = ToleranceFor(plan, propertyPath);
            var mode = ToleranceModeFor(plan, propertyPath);
            for (var index = 0; index < value.length; index++)
            {
                var current = value.keys[index];
                var aggregate = centre.keys[index];
                if (current.weightedMode != aggregate.weightedMode ||
                    !CloseFloat(current.time, aggregate.time, tolerance, mode) ||
                    !CloseFloat(current.value, aggregate.value, tolerance, mode) ||
                    !CloseFloat(current.inTangent, aggregate.inTangent, tolerance, mode) ||
                    !CloseFloat(current.outTangent, aggregate.outTangent, tolerance, mode) ||
                    !CloseFloat(current.inWeight, aggregate.inWeight, tolerance, mode) ||
                    !CloseFloat(current.outWeight, aggregate.outWeight, tolerance, mode)) return false;
            }
            return true;
        }

        private static Vector4 ToVector(SerializedProperty p) => p.propertyType switch
        {
            SerializedPropertyType.Float => new Vector4(p.floatValue, 0, 0, 0),
            SerializedPropertyType.Vector2 => p.vector2Value,
            SerializedPropertyType.Vector3 => p.vector3Value,
            SerializedPropertyType.Vector4 => p.vector4Value,
            _ => Vector4.zero
        };
        private static void SetNumeric(SerializedProperty property, NumericValue value)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Float: property.floatValue = value.Value.x; break;
                case SerializedPropertyType.Vector2: property.vector2Value = value.Value; break;
                case SerializedPropertyType.Vector3: property.vector3Value = value.Value; break;
                case SerializedPropertyType.Vector4: property.vector4Value = value.Value; break;
            }
        }

        private static AnimationCurve AggregateCurve(IEnumerable<VRCPhysBone> source, string path, PBLifterPlan plan)
        {
            var samples = source.Select(pb => (physBone: pb, curve: new SerializedObject(pb).FindProperty(path)?.animationCurveValue))
                .Where(sample => sample.curve != null).ToArray();
            if (samples.Length == 0) return null;

            var first = samples[0].curve;
            var keys = new Keyframe[first.length];
            for (var index = 0; index < keys.Length; index++)
            {
                var template = first.keys[index];
                var key = new Keyframe(
                    AggregateCurveValue(samples, index, value => value.time, plan),
                    AggregateCurveValue(samples, index, value => value.value, plan),
                    AggregateCurveValue(samples, index, value => value.inTangent, plan),
                    AggregateCurveValue(samples, index, value => value.outTangent, plan),
                    AggregateCurveValue(samples, index, value => value.inWeight, plan),
                    AggregateCurveValue(samples, index, value => value.outWeight, plan))
                {
                    weightedMode = template.weightedMode,
                };
                keys[index] = key;
            }

            return new AnimationCurve(keys)
            {
                preWrapMode = first.preWrapMode,
                postWrapMode = first.postWrapMode,
            };
        }

        private static float AggregateCurveValue(
            (VRCPhysBone physBone, AnimationCurve curve)[] samples,
            int keyIndex,
            Func<Keyframe, float> selector,
            PBLifterPlan plan)
        {
            var values = samples.Select(sample => (value: selector(sample.curve.keys[keyIndex]),
                weight: plan.options.aggregation == NumericAggregation.WeightedMean &&
                        plan.options.weighting == Weighting.AffectedTransformCount
                    ? CountAffected(sample.physBone)
                    : 1)).ToArray();

            if (plan.options.aggregation == NumericAggregation.Median)
            {
                var ordered = values.Select(value => value.value).OrderBy(value => value).ToArray();
                var middle = ordered.Length / 2;
                return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) * 0.5f : ordered[middle];
            }

            var total = values.Sum(value => value.value * value.weight);
            var weight = values.Sum(value => value.weight);
            return total / Mathf.Max(weight, 1);
        }

        private static string Format(NumericValue value) => value.Type switch
        {
            SerializedPropertyType.Float => value.Value.x.ToString("0.####"),
            SerializedPropertyType.Vector2 => $"({value.Value.x:0.####}, {value.Value.y:0.####})",
            SerializedPropertyType.Vector3 => $"({value.Value.x:0.####}, {value.Value.y:0.####}, {value.Value.z:0.####})",
            _ => $"({value.Value.x:0.####}, {value.Value.y:0.####}, {value.Value.z:0.####}, {value.Value.w:0.####})",
        };

    }
}
#endif
