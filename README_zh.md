<div align="right">
  <a href="README.md">English</a> | <strong>简体中文</strong>
</div>

# PB Lifter

**PB Lifter** 是面向 VRChat Avatar 的 [NDMF](https://github.com/bdunderscore/ndmf) 插件。它会在构建阶段合并兼容的 VRC PhysBone 组件，以减少组件数量和运行负担。

它不会直接修改场景中的源 Avatar。所有层级调整仅会应用到 NDMF 的构建副本。

## 环境要求

- Unity `2022.3`
- VRChat Avatars SDK `>= 3.7.0 < 3.11.0`
- NDMF `>= 1.8.0 < 2.0.0`

## 安装

在 VCC 中打开 **Settings > Packages > Add Repository**，输入：

`https://github.com/CikeyQi/PB-Lifter/releases/latest/download/vpm.json`

随后将 PB Lifter 添加到已经安装 VRChat Avatars SDK 与 NDMF 的 Avatar 工程。每次 GitHub Release 都会同步发布仓库列表和 VPM 安装包 ZIP。

也可以从 Releases 页面下载 `PB-Lifter-<版本号>.unitypackage`，直接导入工程。

导入后，在 Unity 菜单中打开：

`Tools > PB Lifter > Optimizer Window`

指定 Avatar 根节点、配置规则后，点击 **扫描并预览**。确认预览结果后，使用窗口底部按钮将构建计划附加到 Avatar 根节点。

## 合并方式

默认情况下，PB Lifter 使用严格兼容性检查。只有关键设置相同，且层级、动画和安全条件均兼容的 PhysBone 才会合并。严格模式以 Avatar Optimizer 的 Optimize PhysBone Settings 功能为参考。

数值字段和曲线字段只有在明确启用该字段的容差后才允许不同。非数值设置（包括组件版本、模式、碰撞体、权限、参数名称和骨骼链长度）始终需要兼容。

## 容差与聚合

每个数值或曲线字段都可以单独启用容差。只有启用容差的字段会参与容差比较和聚合，其他字段始终严格匹配。

- **数值聚合方式：** 算术平均、按受影响骨骼数加权的平均值或中位数。
- **聚类算法：** 聚合值约束会使用当前聚合结果检查新成员；完全链接要求同一组中的每一对成员都满足容差。
- **容差类型：** 绝对值按数值差比较；相对值按数值比例比较。数值字段可单独覆盖默认类型。

提高容差可能改变写入合并后组件的数值或曲线。应用构建计划前，请在预览中检查将要聚合的数值差异字段。

## 安全检查与高风险放宽

为避免改变行为，PB Lifter 默认会排除以下情况：允许抓取、设置了 PhysBone 参数、使用非 `Ignore` 的多子节点模式、组件自身受激活动画切换、Humanoid 骨骼映射、受影响骨骼上的约束，以及受影响骨骼数达到 100 的候选项。

“高风险放宽”默认折叠且所有选项均关闭。每个选项只会跳过对应的检查条件；启用后，请在上传 Avatar 前验证交互、动画和模拟表现。

你还可以排除指定节点上的 PhysBone，或排除该节点及其所有子节点上的 PhysBone。

## 预览与诊断

优化预览会展示预计减少的组件数量、计划合并组、成员路径，以及将被聚合的数值字段。诊断页会为每个未计划合并的 PhysBone 给出首要原因，例如设置不兼容、超出容差、抓取权限、已设置参数、激活动画切换根或自定义排除规则。

## 本地化

插件会自动跟随 Unity 的系统语言。中文、简体中文和繁体中文系统显示中文界面；其他系统语言显示英文界面。

## 许可证

本项目采用 [MIT License](LICENSE.md)。
