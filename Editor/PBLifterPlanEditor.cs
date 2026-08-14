#if PBLIFTER_VRCSDK3
using UnityEditor;
using UnityEngine;

namespace PBLifter.Editor
{
    [CustomEditor(typeof(PBLifterPlan))]
    internal sealed class PBLifterPlanEditor : UnityEditor.Editor
    {
        private static string L(string chinese, string english) => PBLifterLocalization.Text(chinese, english);

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                L("这是 PB Lifter 的非破坏性构建计划。请在 PB Lifter 窗口中编辑；源 PhysBone 不会被修改，NDMF 只会将计划应用到构建副本。", "This component is a non-destructive PB Lifter build plan. Edit it through the PB Lifter window; source PhysBones remain unchanged and NDMF applies the plan only to its build copy."),
                MessageType.Info);
            if (GUILayout.Button(L("打开 PB Lifter 窗口", "Open PB Lifter Window")))
                EditorApplication.ExecuteMenuItem("Tools/PB Lifter/Optimizer Window");
            EditorGUILayout.Space(4);
            DrawDefaultInspector();
        }
    }
}
#endif
