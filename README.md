<div align="right">
  <strong>English</strong> | <a href="README_zh.md">简体中文</a>
</div>

# PB Lifter

[![Unity 2022.3](https://img.shields.io/badge/Unity-2022.3-000000?logo=unity)](https://unity.com/releases/editor/whats-new/2022.3.0)
[![VRChat Avatars SDK](https://img.shields.io/badge/VRChat%20Avatars%20SDK-3.7.0--3.10.x-1f9bf0)](https://vrchat.com/home/download)
[![NDMF](https://img.shields.io/badge/NDMF-1.8.x-5c5c5c)](https://github.com/bdunderscore/ndmf)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)

PB Lifter is an [NDMF](https://github.com/bdunderscore/ndmf) build plugin for VRChat avatars. It finds compatible `VRCPhysBone` components, clusters them, and replaces each cluster with one component during the avatar build. The goal is to reduce PhysBone component count without casually changing avatar behavior.

> [!IMPORTANT]
> PB Lifter does not edit the avatar in the Unity scene. It saves an editor-only build plan on the Avatar Root, and NDMF applies that plan to its build copy. Always test an optimized avatar in game before upload, especially after enabling tolerances or high-risk relaxations.

## Contents

- [Requirements](#requirements)
- [Installation](#installation)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Configuration](#configuration)
- [Safety and limitations](#safety-and-limitations)
- [Acknowledgements](#acknowledgements)
- [Development](#development)
- [License](#license)

## Requirements

| Dependency | Supported version |
| --- | --- |
| Unity | `2022.3` |
| VRChat Avatars SDK | `>= 3.7.0 < 3.11.0` |
| NDMF | `>= 1.8.0 < 2.0.0` |

Install the VRChat Avatars SDK and NDMF in the avatar project before adding PB Lifter.

## Installation

### VCC (recommended)

1. In VCC, open **Settings > Packages > Add Repository**.
2. Add the repository URL below:

   ```text
   https://github.com/CikeyQi/PB-Lifter/releases/latest/download/vpm.json
   ```

3. Add **PB Lifter** to the avatar project.

Each GitHub Release publishes both the VPM package and the repository listing.

### Unity package

Download `PB-Lifter-<version>.unitypackage` from [Releases](https://github.com/CikeyQi/PB-Lifter/releases), then import it into a compatible avatar project.

## Quick start

1. Open **Tools > PB Lifter > Optimizer Window**.
2. Assign the avatar's root GameObject.
3. Review the default strategy, exclusions, and field tolerances.
4. Select **Scan & Preview** to inspect proposed merge groups and diagnostics.
5. Select **Confirm: Attach the build plan to Avatar Root** to save the plan on the Avatar Root.
6. Build the avatar normally through NDMF, then verify animation, interaction, and simulation in VRChat.

The preview is non-destructive. Attaching the plan also leaves source PhysBones unchanged; the plan is consumed and removed from NDMF's build copy after the merge pass.

## How it works

PB Lifter only groups candidates that share both an effective-root parent and the same activation-animation toggle root. Within each group, it verifies compatible PhysBone settings, clusters compatible candidates, creates one merged PhysBone, and reparents source roots under a generated helper root when required.

For a merge group, PB Lifter:

- copies the first component's compatible settings;
- aggregates only numeric or curve fields whose tolerance is enabled;
- combines ignored transforms and forces the merged component's multi-child mode to `Ignore`;
- preserves activation grouping when it creates the merged host; and
- normalizes curves for the merged chain length.

All other properties must match. This includes PhysBone version, integration and limit modes, colliders, permissions, parameter settings, endpoint behavior, and other non-tolerated settings.

## Configuration

### Field tolerances

By default, all values are strict: a differing numeric value or curve prevents a merge. Enable tolerance only for fields where a small behavioral change is acceptable.

| Setting | Options | Effect |
| --- | --- | --- |
| Per-field tolerance | Enabled / disabled, non-negative value | Allows differences only for that numeric or curve field. |
| Tolerance interpretation | Absolute / relative | Compares a difference directly or as a ratio. A field can override the default. |
| Clustering | Centroid bounded / complete linkage | Checks a candidate against the current aggregate or against every group member. |
| Aggregation | Arithmetic mean / weighted mean / median | Chooses the value written to the merged component. |
| Weighted mean basis | Equal / affected transform count | Controls weighted-mean weights. |

Increasing a tolerance can change the final PhysBone values. The preview lists fields that will be aggregated for each proposed group.

### Exclusions

Exclude a PhysBone on a selected node, or exclude that node and all descendants. Use this for rigs whose behavior you want to keep untouched regardless of compatibility.

### High-risk relaxations

High-risk relaxations are off by default and each bypasses only its matching guard. The available relaxations are:

- Humanoid bone mapping
- Self activation animation
- Constraints on affected bones
- Affected-bone count limit
- Grabbing
- Configured PhysBone parameter
- Non-`Ignore` multi-child mode

Enable these only with a concrete reason and validate the built avatar thoroughly.

## Safety and limitations

PB Lifter intentionally skips PhysBones that are disabled or inactive, outside the Avatar Root, share an effective root with another PhysBone, or fail a configured exclusion. It also excludes the risky cases listed above by default and skips individual candidates with 100 or more affected transforms. Proposed merge groups are additionally partitioned so that a group contains at most 128 affected transforms.

The plugin indexes animation clips from Animators and the avatar descriptor's base and special layers to protect activation-animation and transform-animation behavior. It cannot prove that every custom rig, animation source, or runtime interaction is behaviorally identical after a merge. Treat the scan as an informed plan, not as a replacement for in-game testing.

## Interface language

The Unity window follows the system language. Chinese, Simplified Chinese, and Traditional Chinese systems show Chinese; other systems show English.

## Acknowledgements

PB Lifter includes and adapts code from [Avatar Optimizer](https://github.com/anatawa12/AvatarOptimizer/) by [anatawa12](https://github.com/anatawa12). Avatar Optimizer is licensed under the [MIT License](https://github.com/anatawa12/AvatarOptimizer/blob/master/LICENSE); its copyright notice is retained in this repository's license.

## Development

This repository is a Unity Package. The source is split into:

- `Runtime/`: serialized build-plan types and localization helpers.
- `Editor/`: NDMF pass, compatibility and clustering logic, and the optimizer window.

The GitHub Actions workflow compiles the package with Unity `2022.3.62f1`, VRChat Avatars SDK `3.10.4`, and NDMF `1.8.0`, then publishes a `.unitypackage`, VPM ZIP, and `vpm.json` for the version in `package.json`.

For changes, keep the source-avatar non-destructive guarantee intact and test both preview diagnostics and an actual avatar build.

## License

PB Lifter is released under the [MIT License](LICENSE.md).
