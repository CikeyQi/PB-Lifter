<div align="right">
  <strong>English</strong> | <a href="README_zh.md">简体中文</a>
</div>

# PB Lifter

[![Unity 2022.3](https://img.shields.io/badge/Unity-2022.3-000000?logo=unity)](https://unity.com/releases/editor/whats-new/2022.3.0)
[![VRChat Avatars SDK](https://img.shields.io/badge/VRChat%20Avatars%20SDK-3.7.0--3.10.x-1f9bf0)](https://vrchat.com/home/download)
[![NDMF](https://img.shields.io/badge/NDMF-1.8.x-5c5c5c)](https://github.com/bdunderscore/ndmf)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

PB Lifter is an [NDMF](https://github.com/bdunderscore/ndmf) build plugin for VRChat avatars. It combines compatible `VRCPhysBone` components during the build to reduce component count. It is deliberately conservative: when PB Lifter cannot establish that a merge is appropriate, it leaves that PhysBone alone.

> [!IMPORTANT]
> PB Lifter does not edit the avatar in your Unity scene. It stores an editor-only build plan on the Avatar Root; NDMF applies the plan only to its build copy. A successful scan is a proposal, not proof that the resulting avatar is identical. Test the built avatar in VRChat before uploading.

## Before you start

PB Lifter is a good fit when an avatar has many similar, independent PhysBone chains, such as repeated accessories, hair locks, or decoration chains. Start with the default settings.

Do not expect a merge when the chains are intentionally different, are controlled by animation, are interactive, or have different collision, constraint, parameter, or permission settings. These are normally kept separate to preserve behavior.

| Requirement | Supported version |
| --- | --- |
| Unity | `2022.3` |
| VRChat Avatars SDK | `>= 3.7.0 < 3.11.0` |
| NDMF | `>= 1.8.0 < 2.0.0` |

Install the VRChat Avatars SDK and NDMF in the avatar project before adding PB Lifter.

## Install

### VCC

1. In VCC, open **Settings > Packages > Add Repository**.
2. Add this repository URL:

   ```text
   https://cikeyqi.github.io/PB-Lifter/vpm.json
   ```

3. Add **PB Lifter** to the avatar project.

[Add PB Lifter to VCC](https://cikeyqi.github.io/PB-Lifter/add-repo/)

### Unity package

Download `PB-Lifter-<version>.unitypackage` from [Releases](https://github.com/CikeyQi/PB-Lifter/releases), then import it into a compatible avatar project.

## Use it safely

1. Open **Tools > PB Lifter > Optimizer Window**.
2. Assign the avatar's root GameObject.
3. Keep the default **Merge Strategy**, **Tolerance Rules**, and high-risk options for the first scan.
4. Select **Scan & Preview**.
5. Review **Report** and **Diagnostics** using the guidance below.
6. When the proposal is acceptable, select **Confirm: Attach the build plan to Avatar Root**.
7. Build through NDMF as usual, then test the built avatar in VRChat.

The confirm button is enabled only when the preview can reduce the PhysBone component count. Attaching a plan does not change source PhysBones. Reopen the window and scan again whenever you change a plan, a PhysBone, or the avatar hierarchy.

## Read the preview

### Report

The report tells you the estimated component reduction and shows every planned group. Open a group to check its members, affected-bone count, and any numeric fields that will be aggregated. Use **Locate** to find a component in the hierarchy.

Approve a group when its members are intended to behave alike and any listed aggregated field is acceptable. If you do not want a specific chain changed, add an exclusion instead of relying on incidental incompatibility.

### Diagnostics

Diagnostics gives every `VRCPhysBone` either **Planned Merge** or **Not Merged**, with the first reason it was not selected. Common outcomes and useful actions are:

| Diagnostic outcome | Meaning | What to do |
| --- | --- | --- |
| No other candidate shares the effective-root parent | No compatible sibling is in the same structural group. | Usually leave it alone. Reparenting just to force a merge can change the rig. |
| Toggle roots differ | The chains are controlled by different activation-animation boundaries. | Keep them separate. This cannot be relaxed. |
| A numeric PhysBone property is animation-driven | Animation writes one of the PhysBone's numeric settings. | Keep it separate. This cannot be relaxed. |
| A field differs / differs beyond tolerance | A required setting is different. | Keep strict, or enable a tolerance only when the changed behavior is understood. |
| Permissions, colliders, parameter, constraint, or multi-child mode differ | The chains do not have matching non-numeric behavior. | Keep them separate. A relaxation can bypass only some eligibility checks; it does not make unlike settings compatible. |
| Candidate or merge-group affected-bone limit is reached | The configured size limit prevents the merge. | Leave the default first; increase the relevant limit only after testing the larger group. |
| Custom exclusion | You explicitly protected this node. | Remove or narrow the exclusion only if you now want it considered. |

## Choose settings

### Merge Strategy

The defaults favor conservative, predictable groups: weighted mean by affected-bone count, centroid-bounded clustering, a candidate limit of `100`, and a group limit of `128` affected transforms.

| Setting | Choose it when | Effect |
| --- | --- | --- |
| Numeric aggregation: Arithmetic Mean | Every chain should contribute equally. | Writes the ordinary average of tolerated numeric values. |
| Numeric aggregation: Weighted Mean | Longer chains should have more influence. Default. | Weights values equally or by affected-bone count. |
| Numeric aggregation: Median | You want the middle value to resist outliers. | Writes the median tolerated value. |
| Clustering: Centroid Bounded | You want larger groups where every member remains close to the group aggregate. Default. | Tests each new member against the aggregate. |
| Clustering: Complete Linkage | You want every pair in a group to remain within tolerance. | Produces stricter, often smaller groups. |
| Per-candidate affected-bone limit | A single chain is known to be safe at a different size. | Candidates at or above the limit are excluded. Minimum `1`. |
| Per-merge-group affected-bone limit | A tested group can safely contain more or fewer bones. | Eligible candidates are partitioned at the limit. Minimum `2`. |

### Tolerance Rules

Every numeric field and curve is strict by default. A tolerance permits PB Lifter to merge otherwise compatible chains when that field differs, then writes one aggregate value or curve to the merged component.

Use tolerance in small steps: enable one field, set the smallest acceptable value, scan, inspect the listed aggregate in the report, then test in game. Do not use **Enable All** as a general optimization shortcut.

`Absolute` tolerance compares the direct numeric difference. `Relative` tolerance scales the allowed difference with the value magnitude. A field can override the default mode. For curves, PB Lifter also requires matching key counts, wrap modes, and weighted-key modes.

### Exclusions

Add an exclusion for a node that must never change. Choose **This Node Only** to protect only the PhysBone on that node, or **This Node and Descendants** to protect an entire sub-rig. Exclusions are the preferred way to preserve a known-sensitive chain.

### High-risk relaxations

All high-risk relaxations are off by default. Each allows an otherwise excluded candidate into consideration; compatible settings and grouping requirements still apply.

| Relaxation | Consider only when |
| --- | --- |
| Disabled components | You have verified the components remain equivalent when enabled or disabled. |
| Hierarchy-inactive components | You have verified their activation hierarchy and built behavior. |
| Active grabbing | Grab behavior is intentionally equivalent after the merge. |
| Configured PhysBone parameter | Parameter-driven behavior is known not to depend on component separation. |
| Non-`Ignore` multi-child mode | You have tested the resulting chain behavior with the merged component's `Ignore` mode. |
| Humanoid bone-mapping path | The affected humanoid path has been checked in relevant locomotion and animation states. |
| Self activation animation | You have checked the activation transition and reset behavior. |
| Constraints on affected bones | Constraint evaluation and simulation have been tested together. |

Two protections cannot be disabled: numeric PhysBone properties driven by animation, and candidates whose activation-animation toggle roots differ. PB Lifter never merges those cases.

## What PB Lifter checks

For candidates in the same effective-root-parent and activation-toggle group, PB Lifter requires matching component state, PhysBone version, root relationship, `Ignore Other PhysBones`, multi-child and integration modes, limits, colliders, permissions, animation/reset settings, parameter, and chain length. Numeric fields and curves must also match unless you explicitly enable their tolerance.

During the build, PB Lifter reads NDMF's virtual animation index. It also remaps animation paths when roots are reparented, and translates `Ignore Other PhysBones` into ignored transforms before merging. The editor preview indexes clips directly referenced by the avatar, so a previous build plugin that generates animation curves may make the final build more conservative than the preview.

## Verify before upload

Test a built copy in VRChat with the conditions that matter for the avatar:

- Toggle every affected object on and off.
- Exercise relevant gestures, FX states, parameters, contacts, and constraints.
- Check grabbing, posing, collision, and reset behavior where enabled.
- Move through the motions that stress the merged chains, including locomotion for humanoid paths.
- Compare the result with the unoptimized avatar before upload.

## Interface language

The Unity window follows the system language. Chinese, Simplified Chinese, and Traditional Chinese systems show Chinese; other systems show English.

## Acknowledgements

PB Lifter includes and adapts code from [Avatar Optimizer](https://github.com/anatawa12/AvatarOptimizer/) by [anatawa12](https://github.com/anatawa12). Avatar Optimizer is licensed under the [MIT License](https://github.com/anatawa12/AvatarOptimizer/blob/master/LICENSE); its copyright notice is retained in this repository's [NOTICE](NOTICE).

## License

PB Lifter is released under the [MIT License](LICENSE).
