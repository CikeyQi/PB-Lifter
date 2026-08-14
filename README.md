<div align="right">
  <strong>English</strong> | <a href="README_zh.md">简体中文</a>
</div>

# PB Lifter

**PB Lifter** is an [NDMF](https://github.com/bdunderscore/ndmf) plugin for VRChat avatars. During the build, it merges compatible VRC PhysBone components to reduce component count and runtime overhead.

It never edits the source avatar in your scene. All hierarchy changes are applied only to NDMF's build copy.

## Requirements

- Unity `2022.3`
- VRChat Avatars SDK `>= 3.7.0 < 3.11.0`
- NDMF `>= 1.8.0 < 2.0.0`

## Installation

In VCC, open **Settings > Packages > Add Repository** and enter:

`https://github.com/CikeyQi/PB-Lifter/releases/latest/download/vpm.json`

Then add PB Lifter to an avatar project that already has the VRChat Avatars SDK and NDMF installed. The repository listing and VPM package ZIP are published with each GitHub Release.

Alternatively, download `PB-Lifter-<version>.unitypackage` from the Releases page and import it directly into the project.

After importing, open:

`Tools > PB Lifter > Optimizer Window`

Assign an Avatar Root, configure the rules, and select **Scan Preview**. After reviewing the results, use the button at the bottom of the window to attach the optimization plan to the Avatar Root.

## How merging works

By default, PB Lifter uses strict compatibility checks. PhysBones are merged only when their critical settings match and their hierarchy, animation, and safety conditions are compatible. The strict-mode behavior is designed with Avatar Optimizer's Optimize PhysBone Settings feature as a reference.

Numeric and curve values must match exactly unless tolerance is explicitly enabled for that field. Non-numeric settings—including component version, modes, colliders, permissions, parameter names, and bone-chain length—always require compatibility.

## Tolerances and aggregation

You can enable a tolerance for each numeric or curve field independently. Only enabled fields participate in tolerance comparison and aggregation; all other fields remain strict.

- **Value aggregation:** mean, weighted mean by affected bone count, or median.
- **Clustering:** centroid-bounded clustering checks each new member against the current aggregate; complete linkage requires every member pair in a group to meet the tolerance.
- **Tolerance interpretation:** absolute tolerance compares numeric difference; relative tolerance compares the value ratio. Numeric fields can override the default interpretation.

Increasing a tolerance can change the values written to the merged component. Review the differing fields listed in the preview before applying the plan.

## Safety checks and high-risk relaxations

PB Lifter excludes cases that can change behavior, including active grabbing, a configured PhysBone parameter, non-`Ignore` multi-child modes, component activation animation, Humanoid mappings, constraints on affected bones, and candidates with 100 or more affected bones.

High-risk relaxations are collapsed and disabled by default. Enabling one skips only its corresponding condition. Validate interaction, animation, and simulation in game before uploading an avatar.

You can also exclude a PhysBone at a selected node, or at that node and all of its descendants.

## Preview and diagnostics

The optimization preview shows estimated component reduction, planned merge groups, member paths, and numeric fields that will be aggregated. Diagnostics give each PhysBone that is not planned for merging a primary reason, such as an incompatible setting, tolerance overflow, grabbing permission, configured parameter, activation-animation root, or custom exclusion.

## Localization

The plugin automatically follows Unity's system language. Chinese, Simplified Chinese, and Traditional Chinese systems use the Chinese interface; all other system languages use English.

## License

Released under the [MIT License](LICENSE.md).
