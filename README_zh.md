<div align="right">
  <a href="README.md">English</a> | <strong>简体中文</strong>
</div>

# PB Lifter

[![Unity 2022.3](https://img.shields.io/badge/Unity-2022.3-000000?logo=unity)](https://unity.com/releases/editor/whats-new/2022.3.0)
[![VRChat Avatars SDK](https://img.shields.io/badge/VRChat%20Avatars%20SDK-3.7.0--3.10.x-1f9bf0)](https://vrchat.com/home/download)
[![NDMF](https://img.shields.io/badge/NDMF-1.8.x-5c5c5c)](https://github.com/bdunderscore/ndmf)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)

PB Lifter 是面向 VRChat Avatar 的 [NDMF](https://github.com/bdunderscore/ndmf) 构建插件。它会查找兼容的 `VRCPhysBone` 组件并进行聚类，在构建 Avatar 时将每个聚类替换为一个组件，以减少 PhysBone 组件数量，同时避免轻易改变 Avatar 的行为。

> [!IMPORTANT]
> PB Lifter 不会修改 Unity 场景中的 Avatar。它只会在 Avatar Root 上保存仅编辑器可用的构建计划，并由 NDMF 应用到构建副本。特别是在启用容差或高风险放宽后，上传前务必在游戏内测试优化后的 Avatar。

## 目录

- [环境要求](#环境要求)
- [安装](#安装)
- [快速开始](#快速开始)
- [工作原理](#工作原理)
- [配置说明](#配置说明)
- [安全机制与限制](#安全机制与限制)
- [致谢](#致谢)
- [开发](#开发)
- [许可证](#许可证)

## 环境要求

| 依赖 | 支持版本 |
| --- | --- |
| Unity | `2022.3` |
| VRChat Avatars SDK | `>= 3.7.0 < 3.11.0` |
| NDMF | `>= 1.8.0 < 2.0.0` |

添加 PB Lifter 前，请先在 Avatar 工程中安装 VRChat Avatars SDK 和 NDMF。

## 安装

### VCC（推荐）

1. 在 VCC 中打开 **Settings > Packages > Add Repository**。
2. 添加以下仓库地址：

   ```text
   https://github.com/CikeyQi/PB-Lifter/releases/latest/download/vpm.json
   ```

3. 将 **PB Lifter** 添加到 Avatar 工程。

每个 GitHub Release 都会发布 VPM 安装包及其仓库列表。

### Unity package

从 [Releases](https://github.com/CikeyQi/PB-Lifter/releases) 下载 `PB-Lifter-<版本号>.unitypackage`，再将其导入兼容的 Avatar 工程。

## 快速开始

1. 打开 **Tools > PB Lifter > Optimizer Window**。
2. 指定 Avatar 的根 GameObject。
3. 检查默认策略、排除项和字段容差。
4. 点击 **扫描并预览**，查看建议的合并组与诊断结果。
5. 点击 **确认：将构建计划附加到 Avatar 根节点**，将计划保存到 Avatar Root。
6. 通过 NDMF 正常构建 Avatar，并在 VRChat 中验证动画、交互和模拟表现。

预览不会修改任何对象。附加计划同样不会修改源 PhysBone；构建时合并通道会在 NDMF 的构建副本中使用并移除该计划。

## 工作原理

PB Lifter 只会将有效根节点父级相同、且激活动画切换根相同的候选项分在一起。随后它检查 PhysBone 设置的兼容性，对兼容候选项聚类，创建一个合并后的 PhysBone；必要时会将原根节点重新挂到自动生成的辅助根节点下。

对于每个合并组，PB Lifter 会：

- 复制第一个组件中兼容的设置；
- 仅聚合已启用容差的数值或曲线字段；
- 合并忽略 Transform 列表，并将合并组件的多子节点模式设为 `Ignore`；
- 创建合并组件时保留激活动画的分组关系；
- 根据合并后的骨骼链长度规范化曲线。

其他所有属性都必须匹配，包括 PhysBone 版本、积分和限制模式、碰撞体、权限、参数设置、末端行为，以及所有未启用容差的设置。

## 配置说明

### 字段容差

默认情况下所有字段均严格匹配：数值或曲线不同就不会合并。只应为可以接受轻微行为变化的字段启用容差。

| 设置 | 选项 | 作用 |
| --- | --- | --- |
| 单字段容差 | 启用/关闭，非负数值 | 仅允许该数值或曲线字段存在差异。 |
| 容差类型 | 绝对值/相对值 | 直接比较数值差，或比较数值比例；字段可覆盖默认类型。 |
| 聚类方式 | 聚合值约束/完全链接 | 将候选项与当前聚合值比较，或与组内每个成员比较。 |
| 聚合方式 | 算术平均/加权平均/中位数 | 决定写入合并组件的数值。 |
| 加权依据 | 等权重/受影响 Transform 数 | 决定加权平均的权重。 |

提高容差可能改变最终 PhysBone 值。预览会列出每个建议合并组中将被聚合的字段。

### 排除规则

可以排除指定节点上的 PhysBone，或排除该节点及其全部子节点。对于无论设置是否兼容都不希望改动的骨骼，请使用排除规则。

### 高风险放宽

高风险放宽默认全部关闭，每个选项只会绕过对应的保护检查。可放宽的检查包括：

- Humanoid 骨骼映射
- 组件自身的激活动画
- 受影响骨骼上的约束
- 受影响骨骼数量上限
- 抓取
- 已配置的 PhysBone 参数
- 非 `Ignore` 的多子节点模式

只有在明确了解影响时才应启用这些选项，并应完整验证构建后的 Avatar。

## 安全机制与限制

PB Lifter 会主动跳过已禁用或未激活、有效根节点位于 Avatar Root 外、与其他 PhysBone 共享有效根节点，或命中自定义排除规则的 PhysBone。默认也会跳过上述高风险情形，并跳过受影响 Transform 数达到 100 的单个候选项；建议合并组还会被进一步拆分，使每组受影响 Transform 数不超过 128。

插件会从 Animator 以及 Avatar Descriptor 的 Base 与 Special 图层索引动画片段，以保护激活动画和 Transform 动画相关的行为。但它无法证明所有自定义骨架、动画来源和运行时交互在合并后都完全一致。扫描结果是有依据的构建建议，不能替代游戏内测试。

## 界面语言

Unity 窗口会跟随系统语言。中文、简体中文和繁体中文系统显示中文；其他系统显示英文。

## 致谢

PB Lifter 使用并改编了 [anatawa12](https://github.com/anatawa12) 的 [Avatar Optimizer](https://github.com/anatawa12/AvatarOptimizer/) 代码。Avatar Optimizer 使用 [MIT License](https://github.com/anatawa12/AvatarOptimizer/blob/master/LICENSE)，其版权声明已保留在本仓库的许可证中。

## 开发

本仓库是一个 Unity Package，源码分为：

- `Runtime/`：可序列化的构建计划类型和本地化辅助代码。
- `Editor/`：NDMF 构建通道、兼容性与聚类逻辑，以及优化器窗口。

GitHub Actions 使用 Unity `2022.3.62f1`、VRChat Avatars SDK `3.10.4` 和 NDMF `1.8.0` 编译包，再根据 `package.json` 中的版本发布 `.unitypackage`、VPM ZIP 和 `vpm.json`。

修改项目时，请保持“不破坏源 Avatar”的约定，并同时测试预览诊断和实际 Avatar 构建。

## 许可证

本项目采用 [MIT License](LICENSE.md)。
