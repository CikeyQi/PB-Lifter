using UnityEngine;

namespace PBLifter
{
    public static class PBLifterLocalization
    {
        public static bool UsesChinese => Application.systemLanguage == SystemLanguage.Chinese ||
            Application.systemLanguage == SystemLanguage.ChineseSimplified ||
            Application.systemLanguage == SystemLanguage.ChineseTraditional;

        public static string Text(string chinese, string english) => UsesChinese ? chinese : english;
    }
}
