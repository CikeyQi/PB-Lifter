using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class PBLifterReleaseExporter
{
    [Serializable]
    private sealed class PackageManifest
    {
        public string version;
    }

    public static void ExportUnityPackage()
    {
        const string exportPath = "Assets/PB Lifter";
        var projectPath = Directory.GetParent(Application.dataPath).FullName;
        var manifestPath = Path.GetFullPath(Path.Combine(projectPath, "../../package.json"));
        var manifest = JsonUtility.FromJson<PackageManifest>(File.ReadAllText(manifestPath));
        if (string.IsNullOrWhiteSpace(manifest.version)) throw new InvalidOperationException("package.json has no version.");

        Directory.CreateDirectory("Builds");
        AssetDatabase.ExportPackage(exportPath, $"Builds/PB-Lifter-{manifest.version}.unitypackage", ExportPackageOptions.Recurse);
    }
}
