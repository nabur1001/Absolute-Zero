using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class BuildVisualPlayer
{
    public static string Main()
    {
        string outputPath = Path.Combine(Path.GetTempPath(), "AZVisual4P_Current", "Player", "AbsoluteZeroVisual4P.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled)
            .Select(scene => scene.path).ToArray();
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development | BuildOptions.DetailedBuildReport
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Build failed: {report.summary.result}, errors={report.summary.totalErrors}");
        return $"Succeeded|errors={report.summary.totalErrors}|warnings={report.summary.totalWarnings}|bytes={report.summary.totalSize}|path={outputPath}";
    }
}
