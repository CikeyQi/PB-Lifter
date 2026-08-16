#if PBLIFTER_VRCSDK3
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace PBLifter.Editor
{
    internal sealed class PBLifterWindow : EditorWindow
    {
        private static string L(string chinese, string english) => PBLifterLocalization.Text(chinese, english);

        private enum Tab { Strategy, Tolerances, Report, Diagnostics }

        private GameObject _avatarRoot;
        private PBLifterOptions _options = new PBLifterOptions();
        private readonly List<PBLifterFieldTolerance> _tolerances = new List<PBLifterFieldTolerance>();
        private readonly List<PBLifterPhysBoneExclusion> _exclusions = new List<PBLifterPhysBoneExclusion>();
        private Transform _newExclusionNode;
        private PBLifterPhysBoneExclusionScope _newExclusionScope;
        private PBLifterPass.ScanReport _report;
        private PBLifterPlan _previewPlan;
        private Tab _activeTab = Tab.Strategy;
        private int _selectedGroupIndex;
        private int _selectedToleranceIndex;
        private string _fieldSearch = string.Empty;
        private string _diagnosticSearch = string.Empty;
        private bool _showOnlyUnmerged = true;
        private bool _highRiskExpanded;

        private VisualElement _content;
        private readonly Dictionary<Tab, ToolbarToggle> _tabButtons = new Dictionary<Tab, ToolbarToggle>();
        private Button _scanButton;
        private Button _confirmButton;

        [MenuItem("Tools/PB Lifter/Optimizer Window")]
        private static void Open()
        {
            var window = GetWindow<PBLifterWindow>();
            window.titleContent = new GUIContent($"PB Lifter v{PBLifterVersion.Current}");
            window.minSize = new Vector2(620, 480);
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            _tabButtons.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            rootVisualElement.style.paddingLeft = 8;
            rootVisualElement.style.paddingRight = 8;
            rootVisualElement.style.paddingTop = 6;
            rootVisualElement.style.paddingBottom = 6;

            BuildHeader();
            BuildTabs();

            _content = new VisualElement();
            _content.style.flexGrow = 1;
            _content.style.minHeight = 0;
            _content.style.marginTop = 6;
            rootVisualElement.Add(_content);

            BuildFooter();
            RefreshView();
        }

        private void BuildHeader()
        {
            var header = new VisualElement();
            header.style.flexShrink = 0;
            header.style.marginBottom = 6;
            var titleRow = Row();
            var title = new Label("PB Lifter");
            title.style.fontSize = 16;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleRow.Add(title);
            var version = new Label($"v{PBLifterVersion.Current}");
            version.style.fontSize = 11;
            version.style.marginLeft = 4;
            version.style.color = new Color(0.7f, 0.7f, 0.7f);
            titleRow.Add(version);
            titleRow.Add(CreateBadge("NDMF", new Color(0.2f, 0.2f, 0.2f, 0.6f), new Color(0.8f, 0.8f, 0.8f)));
            header.Add(titleRow);

            var inputRow = Row();
            inputRow.style.marginTop = 4;
            var avatarField = new ObjectField
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                value = _avatarRoot,
            };
            avatarField.style.flexGrow = 1;
            avatarField.style.flexShrink = 1;
            avatarField.style.minWidth = 0;
            avatarField.RegisterValueChangedCallback(evt => SetAvatarRoot(evt.newValue as GameObject));
            inputRow.Add(avatarField);

            _scanButton = new Button(ScanAndShowReport) { text = L("扫描并预览", "Scan & Preview"), tooltip = L("按当前规则分析 Avatar，并显示可确认的优化预览。", "Analyze the Avatar using the current rules and show a reviewable optimization preview.") };
            _scanButton.style.minWidth = 80;
            _scanButton.style.height = 20;
            _scanButton.style.marginLeft = 6;
            inputRow.Add(_scanButton);
            header.Add(inputRow);
            rootVisualElement.Add(header);
        }

        private void BuildTabs()
        {
            var toolbar = new Toolbar();
            toolbar.style.flexShrink = 0;
            toolbar.style.marginBottom = 4;
            toolbar.style.height = 26;
            AddTabButton(toolbar, Tab.Strategy, L("合并策略", "Merge Strategy"));
            AddTabButton(toolbar, Tab.Tolerances, L("容差规则", "Tolerance Rules"));
            AddTabButton(toolbar, Tab.Report, L("优化预览", "Optimization Preview"));
            AddTabButton(toolbar, Tab.Diagnostics, L("诊断分析", "Diagnostics"));
            rootVisualElement.Add(toolbar);
        }

        private void AddTabButton(Toolbar toolbar, Tab tab, string text)
        {
            var button = new ToolbarToggle { text = text, value = tab == _activeTab };
            button.style.flexGrow = 1;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.RegisterValueChangedCallback(evt =>
            {
                if (!evt.newValue)
                {
                    button.SetValueWithoutNotify(true);
                    return;
                }
                _activeTab = tab;
                RefreshView();
            });
            _tabButtons.Add(tab, button);
            toolbar.Add(button);
        }

        private void BuildFooter()
        {
            var footer = new VisualElement();
            footer.style.flexShrink = 0;
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.marginTop = 6;
            footer.style.paddingTop = 6;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = new Color(1, 1, 1, 0.06f);
            var github = new Button(() => Application.OpenURL("https://github.com/CikeyQi/PB-Lifter")) { text = "GitHub" };
            github.tooltip = L("在浏览器中打开 PB Lifter GitHub 仓库", "Open the PB Lifter GitHub repository in a browser.");
            github.style.minHeight = 28;
            footer.Add(github);
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            footer.Add(spacer);
            _confirmButton = new Button(ConfirmPlan) { text = L("确认：将构建计划附加到 Avatar 根节点", "Confirm: Attach the build plan to Avatar Root") };
            _confirmButton.style.minHeight = 28;
            _confirmButton.style.paddingLeft = 14;
            _confirmButton.style.paddingRight = 14;
            footer.Add(_confirmButton);
            rootVisualElement.Add(footer);
        }

        private void SetAvatarRoot(GameObject avatarRoot)
        {
            if (_avatarRoot == avatarRoot) return;
            _avatarRoot = avatarRoot;
            _report = null;
            _selectedGroupIndex = 0;
            _selectedToleranceIndex = 0;
            DisposePreviewPlan();
            LoadPlanIfPresent();
            EnsureFields();
            RefreshView();
        }

        private void ScanAndShowReport()
        {
            if (_avatarRoot == null) return;
            Scan();
            _selectedGroupIndex = 0;
            _activeTab = Tab.Report;
            RefreshView();
        }

        private void RefreshView()
        {
            if (_content == null) return;
            foreach (var pair in _tabButtons)
            {
                pair.Value.SetValueWithoutNotify(pair.Key == _activeTab);
                pair.Value.style.backgroundColor = pair.Key == _activeTab
                    ? new Color(0.24f, 0.37f, 0.58f, 0.45f)
                    : Color.clear;
                pair.Value.style.color = pair.Key == _activeTab ? Color.white : new Color(0.75f, 0.75f, 0.75f);
            }

            _scanButton?.SetEnabled(_avatarRoot != null);
            if (_confirmButton != null)
            {
                var canConfirm = _avatarRoot != null && _report != null && _report.Reduction > 0;
                _confirmButton.SetEnabled(canConfirm);
                _confirmButton.style.backgroundColor = canConfirm ? new Color(0.18f, 0.44f, 0.28f) : new Color(0.25f, 0.25f, 0.25f);
                _confirmButton.style.color = canConfirm ? Color.white : new Color(0.6f, 0.6f, 0.6f);
                _confirmButton.style.unityFontStyleAndWeight = canConfirm ? FontStyle.Bold : FontStyle.Normal;
            }
            _content.Clear();
            if (_avatarRoot == null)
            {
                _content.Add(new HelpBox(L("请先指定 Avatar 根节点，然后配置规则或开始扫描。", "Select an Avatar Root, then configure rules or start scanning."), HelpBoxMessageType.Warning));
                return;
            }

            switch (_activeTab)
            {
                case Tab.Strategy: BuildStrategyView(_content); break;
                case Tab.Tolerances: BuildToleranceView(_content); break;
                case Tab.Report: BuildReportView(_content); break;
                case Tab.Diagnostics: BuildDiagnosticsView(_content); break;
            }
        }

        private void BuildStrategyView(VisualElement target)
        {
            var scroll = NewScrollView();
            scroll.Add(SectionTitle(L("合并策略", "Merge Strategy")));

            scroll.Add(PopupRow(L("数值聚合方式", "Numeric Aggregation"), new List<string>
            {
                L("算术平均", "Arithmetic Mean"),
                L("加权平均", "Weighted Mean"),
                L("中位数", "Median"),
            }, (int)_options.aggregation, index =>
            {
                _options.aggregation = (NumericAggregation)index;
                InvalidatePreview();
                RefreshView();
            }));

            var weighting = PopupRow(L("权重依据", "Weighting Basis"), new List<string>
            {
                L("等权重", "Equal Weight"),
                L("受影响骨骼数", "Affected Bone Count"),
            }, (int)_options.weighting, index =>
            {
                _options.weighting = (Weighting)index;
                InvalidatePreview();
            });
            weighting.SetEnabled(_options.aggregation == NumericAggregation.WeightedMean);
            scroll.Add(weighting);

            scroll.Add(PopupRow(L("聚类算法", "Clustering Algorithm"), new List<string>
            {
                L("聚合值约束", "Centroid Bounded"),
                L("完全链接", "Complete Linkage"),
            }, (int)_options.clustering, index =>
            {
                _options.clustering = (ClusteringMode)index;
                InvalidatePreview();
                RefreshView();
            }));

            scroll.Add(AffectedBoneLimitField(L("单个候选项受影响骨骼上限", "Affected-Bone Limit per Candidate"), _options.maxAffectedTransformsPerCandidate,
                1, value => _options.maxAffectedTransformsPerCandidate = value));
            scroll.Add(AffectedBoneLimitField(L("每组合并受影响骨骼上限", "Affected-Bone Limit per Merge Group"), _options.maxAffectedTransformsPerGroup,
                2, value => _options.maxAffectedTransformsPerGroup = value));

            scroll.Add(Divider());
            scroll.Add(SectionTitle(L("风险选项", "Risk Options")));
            var highRisk = new Foldout { text = L("高风险放宽", "High-Risk Relaxations"), value = _highRiskExpanded };
            highRisk.RegisterValueChangedCallback(evt => _highRiskExpanded = evt.newValue);
            highRisk.Add(new HelpBox(L("这些选项会跳过对应的安全筛选，可能改变交互、动画或模拟结果。仅在已验证目标 Avatar 后启用。", "These options skip their corresponding safety checks and may alter interaction, animation, or simulation. Enable them only after validating the target Avatar."),
                HelpBoxMessageType.Warning));
            AddRelaxationToggle(highRisk, L("允许合并已禁用的组件", "Allow merging disabled components"), HighRiskRelaxations.IgnoreDisabledComponent);
            AddRelaxationToggle(highRisk, L("允许合并层级中未激活的组件", "Allow merging hierarchy-inactive components"), HighRiskRelaxations.IgnoreHierarchyInactive);
            AddRelaxationToggle(highRisk, L("允许合并实际允许抓取（自己或他人）的组件", "Allow merging components with active grabbing permissions (self or others)"), HighRiskRelaxations.IgnoreGrabbing);
            AddRelaxationToggle(highRisk, L("允许合并带参数的组件", "Allow merging components with a parameter"), HighRiskRelaxations.IgnoreParameter);
            AddRelaxationToggle(highRisk, L("允许合并非 Ignore 多子节点模式", "Allow merging non-Ignore multi-child modes"), HighRiskRelaxations.IgnoreMultiChildMode);
            AddRelaxationToggle(highRisk, L("允许合并 Humanoid 骨骼映射路径", "Allow merging Humanoid bone-mapping paths"), HighRiskRelaxations.IgnoreHumanoidBoneMapping);
            AddRelaxationToggle(highRisk, L("允许合并组件自身激活动画", "Allow merging self-activation animation"), HighRiskRelaxations.IgnoreSelfActivationAnimation);
            AddRelaxationToggle(highRisk, L("允许合并受影响骨骼上带约束的组件", "Allow merging components with constraints on affected bones"), HighRiskRelaxations.IgnoreAffectedBoneConstraints);
            scroll.Add(highRisk);

            BuildExclusions(scroll);

            target.Add(scroll);
        }

        private void AddRelaxationToggle(VisualElement target, string label, HighRiskRelaxations relaxation)
        {
            var row = Row();
            var toggle = new Toggle { value = (_options.highRiskRelaxations & relaxation) != 0 };
            toggle.style.flexShrink = 0;
            toggle.style.marginRight = 2;
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue) _options.highRiskRelaxations |= relaxation;
                else _options.highRiskRelaxations &= ~relaxation;
                InvalidatePreview();
                RefreshView();
            });
            var caption = new Label(label);
            caption.style.flexGrow = 1;
            caption.style.whiteSpace = WhiteSpace.Normal;
            row.Add(toggle);
            row.Add(caption);
            target.Add(row);
        }

        private static Toggle AddRightAlignedToggle(VisualElement target, string label, bool value)
        {
            var row = Row();
            row.style.marginTop = 2;
            row.style.marginBottom = 2;
            var caption = new Label(label);
            caption.style.flexGrow = 1;
            caption.style.minWidth = 0;
            row.Add(caption);
            var toggle = new Toggle { value = value };
            toggle.style.flexShrink = 0;
            row.Add(toggle);
            target.Add(row);
            return toggle;
        }

        private VisualElement AffectedBoneLimitField(string label, int value, int minimum, Action<int> onChanged)
        {
            var field = Row();
            field.style.width = Length.Percent(100);
            field.style.flexGrow = 1;
            field.style.flexShrink = 1;
            field.style.minWidth = 0;
            field.style.marginTop = 2;
            field.style.marginBottom = 2;

            var caption = new Label(label);
            caption.style.flexGrow = 1;
            caption.style.minWidth = 0;
            field.Add(caption);

            var input = new IntegerField { value = value, isDelayed = true };
            input.style.width = 80;
            input.style.minWidth = 80;
            input.style.flexShrink = 0;
            input.style.marginLeft = 6;
            input.RegisterValueChangedCallback(evt =>
            {
                onChanged(Mathf.Max(minimum, evt.newValue));
                InvalidatePreview();
                RefreshView();
            });
            field.Add(input);

            return field;
        }

        private void BuildExclusions(VisualElement target)
        {
            target.Add(Divider());
            target.Add(SectionTitle(L("排除 PhysBone", "Exclude PhysBones")));

            var addRow = Row();
            addRow.style.marginBottom = 3;
            var node = new ObjectField(L("节点", "Node"))
            {
                objectType = typeof(Transform),
                allowSceneObjects = true,
                value = _newExclusionNode,
                tooltip = L("拖入要排除的层级节点", "Drag in the hierarchy node to exclude."),
            };
            node.style.flexGrow = 1;
            node.style.flexShrink = 1;
            node.style.minWidth = 0;
            var scopeChoices = new List<string> { L("仅当前节点", "This Node Only"), L("当前节点及子节点", "This Node and Descendants") };
            var scope = new PopupField<string>(scopeChoices,
                _newExclusionScope == PBLifterPhysBoneExclusionScope.ThisNodeOnly ? 0 : 1);
            scope.style.width = 140;
            scope.style.marginLeft = 4;
            var add = new Button(() =>
            {
                if (_newExclusionNode == null) return;
                if (_exclusions.All(item => item.node != _newExclusionNode || item.scope != _newExclusionScope))
                    _exclusions.Add(new PBLifterPhysBoneExclusion { node = _newExclusionNode, scope = _newExclusionScope });
                _newExclusionNode = null;
                InvalidatePreview();
                RefreshView();
            }) { text = L("添加", "Add") };
            add.style.minWidth = 48;
            add.style.marginLeft = 4;
            add.SetEnabled(_newExclusionNode != null);
            node.RegisterValueChangedCallback(evt =>
            {
                _newExclusionNode = evt.newValue as Transform;
                add.SetEnabled(_newExclusionNode != null);
            });
            scope.RegisterValueChangedCallback(evt => _newExclusionScope = evt.newValue == scopeChoices[0]
                ? PBLifterPhysBoneExclusionScope.ThisNodeOnly
                : PBLifterPhysBoneExclusionScope.ThisNodeAndDescendants);
            addRow.Add(node);
            addRow.Add(scope);
            addRow.Add(add);
            target.Add(addRow);

            foreach (var exclusion in _exclusions.ToArray())
            {
                var row = Row();
                row.style.marginTop = 2;
                var name = new Label(ExclusionNodeLabel(exclusion.node));
                name.style.flexGrow = 1;
                name.style.flexShrink = 1;
                name.style.minWidth = 0;
                name.style.overflow = Overflow.Hidden;
                name.style.textOverflow = TextOverflow.Ellipsis;
                name.tooltip = name.text;
                var scopeLabel = new Label(exclusion.scope == PBLifterPhysBoneExclusionScope.ThisNodeOnly ? L("仅当前节点", "This Node Only") : L("当前节点及子节点", "This Node and Descendants"));
                scopeLabel.style.width = 140;
                scopeLabel.style.marginLeft = 4;
                var remove = new Button(() =>
                {
                    _exclusions.Remove(exclusion);
                    InvalidatePreview();
                    RefreshView();
                }) { text = L("删除", "Remove") };
                remove.style.minWidth = 48;
                remove.style.marginLeft = 4;
                row.Add(name);
                row.Add(scopeLabel);
                row.Add(remove);
                target.Add(row);
            }
        }

        private string ExclusionNodeLabel(Transform node)
        {
            if (node == null) return L("缺失的节点", "Missing Node");
            if (_avatarRoot != null && (node == _avatarRoot.transform || node.IsChildOf(_avatarRoot.transform)))
            {
                var path = AnimationUtility.CalculateTransformPath(node, _avatarRoot.transform);
                return string.IsNullOrEmpty(path) ? L("Avatar 根节点", "Avatar Root") : path;
            }
            return node.name + L("（Avatar Root 外）", " (outside Avatar Root)");
        }

        private void BuildToleranceView(VisualElement target)
        {
            var toolbar = new Toolbar();
            var search = new ToolbarSearchField { value = _fieldSearch };
            search.style.flexGrow = 1;
            search.RegisterValueChangedCallback(evt =>
            {
                _fieldSearch = evt.newValue;
                RefreshView();
            });
            toolbar.Add(search);
            toolbar.Add(new ToolbarButton(() => SetAllTolerances(true)) { text = L("全部启用", "Enable All") });
            toolbar.Add(new ToolbarButton(() => SetAllTolerances(false)) { text = L("全部严格匹配", "Make All Strict") });
            target.Add(toolbar);

            var mode = PopupRow(L("默认容差类型", "Default Tolerance Mode"), new List<string>
            {
                L("绝对值", "Absolute"),
                L("相对值", "Relative"),
            }, (int)_options.toleranceInterpretation, index =>
            {
                _options.toleranceInterpretation = (ToleranceInterpretation)index;
                InvalidatePreview();
            });
            mode.style.marginTop = 4;
            target.Add(mode);
            target.Add(Divider());

            var fields = _tolerances.Where(field => string.IsNullOrWhiteSpace(_fieldSearch) ||
                field.propertyPath.IndexOf(_fieldSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (fields.Count == 0)
            {
                target.Add(new HelpBox(L("当前 Avatar 没有可配置的数值 PhysBone 字段。", "The current Avatar has no configurable numeric PhysBone fields."), HelpBoxMessageType.Warning));
                return;
            }

            _selectedToleranceIndex = Mathf.Clamp(_selectedToleranceIndex, 0, fields.Count - 1);
            var split = new TwoPaneSplitView(0, 270, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            split.style.minHeight = 0;
            var list = new ListView
            {
                itemsSource = fields,
                makeItem = MakeToleranceListItem,
                bindItem = (element, index) => BindToleranceListItem(element, fields[index]),
                selectionType = SelectionType.Single,
                fixedItemHeight = 28,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
            };
            list.style.flexGrow = 1;
            list.style.minHeight = 0;
            var detail = NewScrollView();
            list.selectionChanged += selection =>
            {
                var selected = selection.FirstOrDefault() as PBLifterFieldTolerance;
                if (selected == null) return;
                _selectedToleranceIndex = fields.IndexOf(selected);
                BuildToleranceDetail(detail, selected, list);
            };
            split.Add(list);
            split.Add(detail);
            target.Add(split);
            list.SetSelection(_selectedToleranceIndex);
            BuildToleranceDetail(detail, fields[_selectedToleranceIndex], list);
        }

        private void SetAllTolerances(bool enabled)
        {
            foreach (var field in _tolerances) field.allowDifference = enabled;
            InvalidatePreview();
            RefreshView();
        }

        private static VisualElement MakeToleranceListItem()
        {
            var row = Row();
            row.style.paddingLeft = 4;
            row.style.paddingRight = 4;
            var label = new Label { name = "name" };
            label.style.flexGrow = 1;
            label.style.flexShrink = 1;
            label.style.minWidth = 0;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            var badge = CreateBadge(string.Empty, Color.clear, Color.white);
            badge.name = "state";
            row.Add(label);
            row.Add(badge);
            return row;
        }

        private static void BindToleranceListItem(VisualElement element, PBLifterFieldTolerance item)
        {
            var label = element.Q<Label>("name");
            label.text = PBLifterFieldLabels.Display(item.propertyPath);
            label.tooltip = label.text;
            var badge = element.Q<Label>("state");
            var isCurve = item.propertyPath.EndsWith("Curve", StringComparison.Ordinal);
            SetBadge(badge, isCurve ? (item.allowDifference ? L("曲线容差", "Curve Tolerance") : L("曲线严格匹配", "Curve Strict")) : (item.allowDifference ? L("容差", "Tolerance") : L("严格匹配", "Strict")), item.allowDifference
                ? new Color(0.18f, 0.38f, 0.28f, 0.8f)
                : new Color(0.25f, 0.25f, 0.25f, 0.8f));
        }

        private void BuildToleranceDetail(VisualElement target, PBLifterFieldTolerance item, ListView list)
        {
            target.Clear();
            target.style.paddingLeft = 8;
            target.style.paddingRight = 8;
            target.style.paddingTop = 6;
            target.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.28f);
            target.Add(SectionTitle(PBLifterFieldLabels.Display(item.propertyPath)));
            var separator = new VisualElement();
            separator.style.height = 1;
            separator.style.backgroundColor = new Color(1, 1, 1, 0.08f);
            separator.style.marginTop = 4;
            separator.style.marginBottom = 8;
            target.Add(separator);

            var enabled = AddRightAlignedToggle(target, L("允许该字段在容差内不同", "Allow this field to differ within tolerance"), item.allowDifference);
            enabled.RegisterValueChangedCallback(evt =>
            {
                item.allowDifference = evt.newValue;
                InvalidatePreview();
                RefreshView();
            });

            var isCurve = item.propertyPath.EndsWith("Curve", StringComparison.Ordinal);
            var toleranceRow = Row();
            toleranceRow.style.marginTop = 2;
            toleranceRow.style.marginBottom = 2;
            var toleranceCaption = new Label(isCurve ? L("关键帧容差", "Keyframe Tolerance") : L("容差", "Tolerance"));
            toleranceCaption.style.flexGrow = 1;
            toleranceCaption.style.minWidth = 0;
            toleranceRow.Add(toleranceCaption);
            var tolerance = new FloatField { value = item.tolerance, isDelayed = true };
            tolerance.style.width = 80;
            tolerance.style.minWidth = 80;
            tolerance.style.flexShrink = 0;
            toleranceRow.SetEnabled(item.allowDifference);
            tolerance.RegisterValueChangedCallback(evt =>
            {
                item.tolerance = Mathf.Max(0, evt.newValue);
                tolerance.SetValueWithoutNotify(item.tolerance);
                InvalidatePreview();
                list.RefreshItems();
            });
            toleranceRow.Add(tolerance);
            target.Add(toleranceRow);

            var overrideMode = AddRightAlignedToggle(target, L("覆盖默认容差类型", "Override default tolerance mode"), item.overrideToleranceInterpretation);
            overrideMode.parent.style.display = isCurve ? DisplayStyle.None : DisplayStyle.Flex;
            overrideMode.parent.SetEnabled(item.allowDifference);
            overrideMode.RegisterValueChangedCallback(evt =>
            {
                item.overrideToleranceInterpretation = evt.newValue;
                InvalidatePreview();
                BuildToleranceDetail(target, item, list);
                list.RefreshItems();
            });

            var interpretation = PopupRow(L("容差类型", "Tolerance Mode"), new List<string>
            {
                L("绝对值", "Absolute"),
                L("相对值", "Relative"),
            }, (int)item.toleranceInterpretation, index =>
            {
                item.toleranceInterpretation = (ToleranceInterpretation)index;
                InvalidatePreview();
                list.RefreshItems();
            }, 100);
            interpretation.style.display = isCurve ? DisplayStyle.None : DisplayStyle.Flex;
            interpretation.SetEnabled(item.allowDifference && item.overrideToleranceInterpretation);
            target.Add(interpretation);
        }

        private void BuildReportView(VisualElement target)
        {
            if (_report == null)
            {
                target.Add(new HelpBox(L("尚未扫描。请在顶部选择 Avatar 根节点后点击“扫描并预览”。", "No scan has been run. Select an Avatar Root above, then click Scan & Preview."), HelpBoxMessageType.Info));
                return;
            }

            var percent = _report.SourceCount == 0 ? 0 : (float)_report.Reduction / _report.SourceCount * 100;
            var metrics = Row();
            metrics.style.marginTop = 4;
            metrics.style.marginBottom = 6;
            metrics.Add(Metric("PhysBone", $"{_report.SourceCount} → {_report.ResultCount}"));
            metrics.Add(Metric(L("预计减少", "Estimated Reduction"), PBLifterLocalization.Text($"{_report.Reduction}（{percent:0.#}%）", $"{_report.Reduction} ({percent:0.#}%)")));
            metrics.Add(Metric(L("计划合并", "Planned for Merging"), $"{_report.MergedCount} / {_report.EligibleCount}"));
            target.Add(metrics);
            if (_report.Groups.Count == 0)
            {
                target.Add(new HelpBox(L("没有可合并的组。请查看“诊断”页，或调整字段容差和合并策略。", "No groups can be merged. Review Diagnostics or adjust the field tolerances and merge strategy."), HelpBoxMessageType.Warning));
                return;
            }

            _selectedGroupIndex = Mathf.Clamp(_selectedGroupIndex, 0, _report.Groups.Count - 1);
            var split = new TwoPaneSplitView(0, 260, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            split.style.minHeight = 0;

            var detail = NewScrollView();
            detail.style.paddingLeft = 8;
            detail.style.paddingRight = 8;

            var groups = Enumerable.Range(0, _report.Groups.Count).Cast<object>().ToList();
            var groupList = new ListView
            {
                itemsSource = groups,
                makeItem = () => new Label(),
                bindItem = (element, index) => ((Label)element).text = GroupLabel((int)groups[index]),
                selectionType = SelectionType.Single,
                fixedItemHeight = 28,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
            };
            groupList.selectionChanged += selection =>
            {
                var selected = selection.FirstOrDefault();
                if (!(selected is int index)) return;
                _selectedGroupIndex = index;
                BuildGroupDetail(detail);
            };
            split.Add(groupList);
            split.Add(detail);
            target.Add(split);
            groupList.SetSelection(_selectedGroupIndex);
            BuildGroupDetail(detail);
        }

        private string GroupLabel(int index)
        {
            var group = _report.Groups[index];
            var affected = group.Sum(PBLifterPass.CountAffectedForDisplay);
            return L($"第 {index + 1} 组：{group.Count} 个 → 1 个（减少 {group.Count - 1}；影响骨骼 {affected}）", $"Group {index + 1}: {group.Count} → 1 (reduce by {group.Count - 1}; {affected} affected bones)");
        }

        private void BuildGroupDetail(VisualElement target)
        {
            target.Clear();
            if (_report == null || _previewPlan == null || _report.Groups.Count == 0) return;
            var group = _report.Groups[_selectedGroupIndex];
            target.Add(SectionTitle(L($"第 {_selectedGroupIndex + 1} 组成员", $"Group {_selectedGroupIndex + 1} Members")));
            var separator = new VisualElement();
            separator.style.height = 1;
            separator.style.backgroundColor = new Color(1, 1, 1, 0.08f);
            separator.style.marginTop = 4;
            separator.style.marginBottom = 8;
            target.Add(separator);
            foreach (var physBone in group)
            {
                var path = AnimationUtility.CalculateTransformPath(physBone.transform, _avatarRoot.transform);
                var row = Row();
                var text = new VisualElement();
                text.style.flexGrow = 1;
                var name = new Label(physBone.name);
                name.style.unityFontStyleAndWeight = FontStyle.Bold;
                var pathLabel = new Label(string.IsNullOrEmpty(path) ? L("Avatar 根节点", "Avatar Root") : path);
                pathLabel.style.fontSize = 10;
                pathLabel.style.color = new Color(0.65f, 0.65f, 0.65f);
                pathLabel.style.overflow = Overflow.Hidden;
                pathLabel.style.textOverflow = TextOverflow.Ellipsis;
                pathLabel.tooltip = pathLabel.text;
                text.Add(name);
                text.Add(pathLabel);
                row.Add(text);
                var locate = new Button(() => Locate(physBone)) { text = L("定位", "Locate") };
                locate.style.minWidth = 48;
                row.Add(locate);
                target.Add(row);
            }
            var changes = PBLifterPass.DifferingNumericFields(group, _previewPlan).ToArray();
            var changesBox = new HelpBox(changes.Length > 0
                ? L("将聚合的数值差异字段：", "Differing numeric fields to be aggregated: ") + string.Join(PBLifterLocalization.UsesChinese ? "、" : ", ", changes)
                : L("未检测到可聚合的数值差异字段。", "No aggregatable numeric field differences were detected."), HelpBoxMessageType.None);
            changesBox.style.marginLeft = 0;
            changesBox.style.marginRight = 0;
            changesBox.style.marginTop = 10;
            target.Add(changesBox);
        }

        private void BuildDiagnosticsView(VisualElement target)
        {
            if (_report == null)
            {
                target.Add(new HelpBox(L("尚未扫描。完成扫描后，这里会列出每个 PhysBone 的合并状态和首要原因。", "No scan has been run. After scanning, this page lists each PhysBone's merge status and primary reason."), HelpBoxMessageType.Info));
                return;
            }

            var toolbar = new Toolbar();
            var filter = new ToolbarToggle { text = _showOnlyUnmerged ? L("显示全部", "Show All") : L("仅显示未合并项", "Show Unmerged Only"), value = _showOnlyUnmerged };
            filter.RegisterValueChangedCallback(evt =>
            {
                _showOnlyUnmerged = evt.newValue;
                RefreshView();
            });
            toolbar.Add(filter);
            var search = new ToolbarSearchField { value = _diagnosticSearch };
            search.style.flexGrow = 1;
            search.RegisterValueChangedCallback(evt =>
            {
                _diagnosticSearch = evt.newValue;
                RefreshView();
            });
            toolbar.Add(search);
            target.Add(toolbar);

            var diagnostics = _report.Diagnostics.Where(item => !_showOnlyUnmerged || !item.Planned)
                .Where(item => string.IsNullOrWhiteSpace(_diagnosticSearch) ||
                    item.PhysBone.name.IndexOf(_diagnosticSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Reason.IndexOf(_diagnosticSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (diagnostics.Count == 0)
            {
                target.Add(new HelpBox(L("没有符合当前筛选条件的诊断项。", "No diagnostics match the current filter."), HelpBoxMessageType.Info));
                return;
            }

            var list = new ListView
            {
                itemsSource = diagnostics,
                makeItem = MakeDiagnosticRow,
                bindItem = (element, index) => BindDiagnosticRow(element, diagnostics[index]),
                selectionType = SelectionType.None,
                fixedItemHeight = 62,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
            };
            list.style.flexGrow = 1;
            list.style.minHeight = 0;
            target.Add(list);
        }

        private static VisualElement MakeDiagnosticRow()
        {
            var row = new VisualElement();
            row.style.paddingTop = 3;
            row.style.paddingBottom = 3;
            var top = Row();
            top.style.minWidth = 0;
            top.style.width = Length.Percent(100);
            var status = CreateBadge(string.Empty, Color.clear, Color.white);
            status.name = "status";
            var path = new Label { name = "path" };
            path.style.flexGrow = 1;
            path.style.flexShrink = 1;
            path.style.minWidth = 0;
            path.style.overflow = Overflow.Hidden;
            path.style.textOverflow = TextOverflow.Ellipsis;
            path.style.marginRight = 6;
            var locate = new Button { name = "locate", text = L("定位", "Locate") };
            locate.style.minWidth = 48;
            locate.style.flexShrink = 0;
            locate.RegisterCallback<ClickEvent>(_ =>
            {
                var item = locate.userData as PBLifterPass.ScanDiagnostic;
                if (item?.PhysBone != null) Locate(item.PhysBone);
            });
            top.Add(status);
            top.Add(path);
            top.Add(locate);
            var reason = new Label { name = "reason" };
            reason.style.whiteSpace = WhiteSpace.Normal;
            reason.style.fontSize = 11;
            row.Add(top);
            row.Add(reason);
            return row;
        }

        private void BindDiagnosticRow(VisualElement row, PBLifterPass.ScanDiagnostic item)
        {
            var path = AnimationUtility.CalculateTransformPath(item.PhysBone.transform, _avatarRoot.transform);
            SetBadge(row.Q<Label>("status"), item.Planned ? L("计划合并", "Planned Merge") : L("不合并", "Not Merged"), item.Planned
                ? new Color(0.18f, 0.42f, 0.26f, 0.85f)
                : new Color(0.42f, 0.22f, 0.22f, 0.85f));
            row.Q<Label>("path").text = string.IsNullOrEmpty(path) ? item.PhysBone.name : path;
            row.Q<Label>("path").tooltip = path;
            row.Q<Label>("reason").text = item.Reason;
            row.Q<Button>("locate").userData = item;
        }

        private static void Locate(VRCPhysBone physBone)
        {
            Selection.activeObject = physBone.gameObject;
            EditorGUIUtility.PingObject(physBone.gameObject);
        }

        private void Scan()
        {
            EnsureFields();
            DisposePreviewPlan();
            _previewPlan = CreatePreviewPlan();
            _report = PBLifterPass.Analyze(_avatarRoot, _previewPlan);
        }

        private void InvalidatePreview()
        {
            if (_report == null) return;
            _report = null;
            DisposePreviewPlan();
        }

        private void EnsureFields()
        {
            if (_avatarRoot == null) return;
            var paths = _avatarRoot.GetComponentsInChildren<VRCPhysBone>(true)
                .SelectMany(PBLifterPass.TolerablePropertyPaths).Distinct().OrderBy(path => path).ToArray();
            foreach (var path in paths)
                if (_tolerances.All(field => field.propertyPath != path))
                    _tolerances.Add(new PBLifterFieldTolerance { propertyPath = path });
            _tolerances.RemoveAll(field => !paths.Contains(field.propertyPath));
        }

        private PBLifterPlan CreatePreviewPlan()
        {
            var temporary = new GameObject("PB Lifter Preview Plan") { hideFlags = HideFlags.HideAndDontSave };
            var plan = temporary.AddComponent<PBLifterPlan>();
            plan.options = CopyOptions(_options);
            plan.fieldTolerances = CopyTolerances(_tolerances);
            plan.excludedPhysBones = CopyExclusions(_exclusions);
            return plan;
        }

        private void ConfirmPlan()
        {
            if (_avatarRoot == null || _report == null) return;
            var plan = _avatarRoot.GetComponent<PBLifterPlan>();
            if (plan == null) plan = Undo.AddComponent<PBLifterPlan>(_avatarRoot);
            Undo.RecordObject(plan, "Configure PB Lifter Plan");
            plan.options = CopyOptions(_options);
            plan.fieldTolerances = CopyTolerances(_tolerances);
            plan.excludedPhysBones = CopyExclusions(_exclusions);
            EditorUtility.SetDirty(plan);
            Selection.activeGameObject = _avatarRoot;
            _report = null;
            DisposePreviewPlan();
            ShowNotification(new GUIContent(L("构建计划已附加；源 Avatar 不会被直接修改。", "Build plan attached; the source Avatar will not be modified directly.")));
            RefreshView();
        }

        private void OnDisable() => DisposePreviewPlan();

        private void DisposePreviewPlan()
        {
            if (_previewPlan == null) return;
            UnityEngine.Object.DestroyImmediate(_previewPlan.gameObject);
            _previewPlan = null;
        }

        private void LoadPlanIfPresent()
        {
            _tolerances.Clear();
            _exclusions.Clear();
            _newExclusionNode = null;
            _newExclusionScope = PBLifterPhysBoneExclusionScope.ThisNodeOnly;
            if (_avatarRoot == null) return;
            var plan = _avatarRoot.GetComponent<PBLifterPlan>();
            if (plan == null) return;
            _options = CopyOptions(plan.options);
            _tolerances.AddRange(CopyTolerances(plan.fieldTolerances));
            _exclusions.AddRange(CopyExclusions(plan.excludedPhysBones));
        }

        private static ScrollView NewScrollView()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;
            scroll.style.minWidth = 0;
            scroll.contentContainer.style.minWidth = 0;
            scroll.contentContainer.style.width = Length.Percent(100);
            return scroll;
        }

        private static VisualElement SectionTitle(string text)
        {
            var title = new Label(text);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginTop = 4;
            title.style.marginBottom = 4;
            return title;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        private static VisualElement Divider()
        {
            var divider = new VisualElement();
            divider.style.height = 1;
            divider.style.marginTop = 8;
            divider.style.marginBottom = 4;
            divider.style.backgroundColor = new Color(1, 1, 1, 0.12f);
            return divider;
        }

        private static Label SizedLabel(string text, float width, bool bold)
        {
            var label = new Label(text);
            if (width > 0) label.style.width = width;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        private static VisualElement Metric(string label, string value)
        {
            var metric = new VisualElement();
            metric.style.flexGrow = 1;
            metric.style.minWidth = 120;
            metric.style.marginRight = 4;
            metric.style.paddingLeft = 8;
            metric.style.paddingTop = 6;
            metric.style.paddingBottom = 6;
            metric.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.45f);
            metric.style.borderTopLeftRadius = 4;
            metric.style.borderTopRightRadius = 4;
            metric.style.borderBottomLeftRadius = 4;
            metric.style.borderBottomRightRadius = 4;
            metric.style.borderLeftWidth = 2;
            metric.style.borderLeftColor = new Color(0.24f, 0.58f, 0.76f);
            var caption = new Label(label);
            caption.style.fontSize = 11;
            var number = new Label(value);
            number.style.fontSize = 16;
            number.style.unityFontStyleAndWeight = FontStyle.Bold;
            metric.Add(caption);
            metric.Add(number);
            return metric;
        }

        private static Label CreateBadge(string text, Color background, Color foreground)
        {
            var badge = new Label(text);
            badge.style.fontSize = 10;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.color = foreground;
            badge.style.backgroundColor = background;
            badge.style.paddingLeft = 5;
            badge.style.paddingRight = 5;
            badge.style.paddingTop = 1;
            badge.style.paddingBottom = 1;
            badge.style.marginLeft = 4;
            badge.style.borderTopLeftRadius = 3;
            badge.style.borderTopRightRadius = 3;
            badge.style.borderBottomLeftRadius = 3;
            badge.style.borderBottomRightRadius = 3;
            return badge;
        }

        private static void SetBadge(Label badge, string text, Color background)
        {
            badge.text = text;
            badge.style.backgroundColor = background;
            badge.style.color = new Color(0.9f, 0.9f, 0.9f);
        }

        private static VisualElement Segmented(string label, IReadOnlyList<string> choices, int selectedIndex, Action<int> onChanged)
        {
            var field = new VisualElement();
            field.style.width = Length.Percent(100);
            field.style.minWidth = 0;
            field.style.paddingLeft = 0;
            field.style.marginTop = 2;
            field.style.marginBottom = 4;
            var caption = new Label(label);
            caption.style.marginBottom = 2;
            field.Add(caption);
            var row = Row();
            row.style.width = Length.Percent(100);
            row.style.minWidth = 0;
            var toggles = new List<ToolbarToggle>();
            for (var i = 0; i < choices.Count; i++)
            {
                var index = i;
                var toggle = new ToolbarToggle { text = choices[i], value = index == selectedIndex };
                toggle.style.flexGrow = 1;
                toggle.style.flexShrink = 1;
                toggle.style.flexBasis = 0;
                toggle.style.minWidth = 0;
                toggle.style.overflow = Overflow.Hidden;
                toggle.style.textOverflow = TextOverflow.Ellipsis;
                toggle.style.unityTextAlign = TextAnchor.MiddleCenter;
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (!evt.newValue)
                    {
                        if (toggles.All(item => !item.value)) toggle.SetValueWithoutNotify(true);
                        return;
                    }
                    foreach (var item in toggles)
                        if (item != toggle) item.SetValueWithoutNotify(false);
                    onChanged(index);
                });
                toggles.Add(toggle);
                row.Add(toggle);
            }
            field.Add(row);
            return field;
        }

        private static VisualElement PopupRow(string label, List<string> choices, int selectedIndex, Action<int> onChanged,
            float labelWidth = 150)
        {
            var row = Row();
            row.style.width = Length.Percent(100);
            row.style.minWidth = 0;
            row.style.marginTop = 2;
            row.style.marginBottom = 4;
            row.Add(SizedLabel(label, labelWidth, false));
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            spacer.style.minWidth = 0;
            row.Add(spacer);
            var picker = new PopupField<string>(choices, selectedIndex);
            picker.style.width = 180;
            picker.style.minWidth = 180;
            picker.style.flexShrink = 0;
            picker.RegisterValueChangedCallback(evt => onChanged(choices.IndexOf(evt.newValue)));
            row.Add(picker);
            return row;
        }

        private static Toggle OptionToggle(string label, bool value, Action<bool> onChanged)
        {
            var toggle = new Toggle(label) { value = value };
            toggle.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            return toggle;
        }

        private static PBLifterOptions CopyOptions(PBLifterOptions source) => new PBLifterOptions
        {
            aggregation = source.aggregation,
            weighting = source.weighting,
            clustering = source.clustering,
            toleranceInterpretation = source.toleranceInterpretation,
            highRiskRelaxations = source.highRiskRelaxations,
            maxAffectedTransformsPerCandidate = source.maxAffectedTransformsPerCandidate,
            maxAffectedTransformsPerGroup = source.maxAffectedTransformsPerGroup,
        };

        private static List<PBLifterFieldTolerance> CopyTolerances(IEnumerable<PBLifterFieldTolerance> source) => source
            .Select(field => new PBLifterFieldTolerance
            {
                propertyPath = field.propertyPath,
                allowDifference = field.allowDifference,
                tolerance = field.tolerance,
                overrideToleranceInterpretation = field.overrideToleranceInterpretation,
                toleranceInterpretation = field.toleranceInterpretation,
            }).ToList();

        private static List<PBLifterPhysBoneExclusion> CopyExclusions(IEnumerable<PBLifterPhysBoneExclusion> source) =>
            (source ?? Enumerable.Empty<PBLifterPhysBoneExclusion>()).Where(item => item != null)
            .Select(item => new PBLifterPhysBoneExclusion { node = item.node, scope = item.scope }).ToList();
    }
}
#endif
