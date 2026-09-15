using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

internal class CommandScript : IRunCommand
{
    [Serializable] internal class Row
    {
        public string scene;
        public string path;
        public bool activeSelf;
        public bool activeInHierarchy;
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        public Vector3 localScale;
        public List<string> components = new List<string>();
    }
    [Serializable] internal class SceneRow { public string path; public string name; public bool dirty; public bool loaded; }
    [Serializable] internal class Report
    {
        public bool playing;
        public int limit = 500;
        public bool truncated;
        public string scope = "Loaded normal scenes; excludes DontDestroyOnLoad special scene";
        public List<SceneRow> scenes = new List<SceneRow>();
        public List<Row> objects = new List<Row>();
    }
    public void Execute(ExecutionResult result)
    {
        var report = new Report { playing = Application.isPlaying };
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            report.scenes.Add(new SceneRow { name = scene.name, path = scene.path, loaded = scene.isLoaded, dirty = scene.isDirty });
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null) continue;
                    if (report.objects.Count >= report.limit) { report.truncated = true; continue; }
                    var parts = new List<string>();
                    for (var p = t; p != null; p = p.parent) parts.Add(p.name + "[" + p.GetSiblingIndex() + "]");
                    parts.Reverse();
                    var row = new Row { scene = scene.path, path = string.Join("/", parts), activeSelf = t.gameObject.activeSelf, activeInHierarchy = t.gameObject.activeInHierarchy, localPosition = t.localPosition, localEulerAngles = t.localEulerAngles, localScale = t.localScale };
                    foreach (var c in t.GetComponents<Component>()) row.components.Add(c == null ? "<MissingScript>" : c.GetType().FullName);
                    report.objects.Add(row);
                }
            }
        }
        var sceneEntries = new List<string>();
        foreach (var scene in report.scenes)
            sceneEntries.Add("{\"path\":" + Q(scene.path) + ",\"name\":" + Q(scene.name) + ",\"dirty\":" + scene.dirty.ToString().ToLowerInvariant() + ",\"loaded\":" + scene.loaded.ToString().ToLowerInvariant() + "}");
        var rows = new List<string>();
        foreach (var row in report.objects)
        {
            var names = new List<string>();
            foreach (var name in row.components) names.Add(Q(name));
            rows.Add("{\"scene\":" + Q(row.scene) + ",\"path\":" + Q(row.path) + ",\"activeSelf\":" + row.activeSelf.ToString().ToLowerInvariant() + ",\"activeInHierarchy\":" + row.activeInHierarchy.ToString().ToLowerInvariant() + ",\"localPosition\":" + V(row.localPosition) + ",\"localEulerAngles\":" + V(row.localEulerAngles) + ",\"localScale\":" + V(row.localScale) + ",\"components\":[" + string.Join(",", names) + "]}");
        }
        result.Log("{\"playing\":" + report.playing.ToString().ToLowerInvariant() + ",\"limit\":" + report.limit + ",\"truncated\":" + report.truncated.ToString().ToLowerInvariant() + ",\"scope\":" + Q(report.scope) + ",\"scenes\":[" + string.Join(",", sceneEntries) + "],\"objects\":[" + string.Join(",", rows) + "]}");
    }
    private static string Q(string value)
    {
        if (value == null) return "null";
        var b = new System.Text.StringBuilder("\"");
        foreach (var c in value)
        {
            if (c == '\\' || c == '"') { b.Append('\\'); b.Append(c); }
            else if (c < 32) b.Append("\\u" + ((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }
    private static string V(Vector3 value)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return "[" + value.x.ToString("R", culture) + "," + value.y.ToString("R", culture) + "," + value.z.ToString("R", culture) + "]";
    }
}
