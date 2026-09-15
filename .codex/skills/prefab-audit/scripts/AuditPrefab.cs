using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal class CommandScript : IRunCommand
{
    private const string AssetPath = "Assets/Prefabs/Player.prefab";

    [Serializable] internal class Finding
    {
        public string path;
        public string component;
        public string property;
        public string category;
    }
    [Serializable] internal class Report
    {
        public string asset;
        public int objects;
        public int components;
        public int emptyReferences;
        public bool dirtyBefore;
        public bool dirtyAfter;
        public List<Finding> findings = new List<Finding>();
    }
    public void Execute(ExecutionResult result)
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(AssetPath);
        if (root == null) { result.LogError("Prefab not found: " + AssetPath); return; }
        var report = new Report { asset = AssetPath, dirtyBefore = EditorUtility.IsDirty(root) };
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null) continue;
            report.objects++;
            var path = GetPath(transform);
            var components = transform.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    report.findings.Add(new Finding { path = path, component = "slot " + i, category = "Missing component/script slot" });
                    continue;
                }
                report.components++;
                using (var serialized = new SerializedObject(component))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue != null) continue;
                        if (property.objectReferenceInstanceIDValue == 0) { report.emptyReferences++; continue; }
                        report.findings.Add(new Finding { path = path, component = component.GetType().FullName, property = property.propertyPath, category = "Unresolved nonzero object reference; confirm required field" });
                    }
                }
            }
        }
        report.dirtyAfter = EditorUtility.IsDirty(root);
        var entries = new List<string>();
        foreach (var f in report.findings)
            entries.Add("{\"path\":" + Q(f.path) + ",\"component\":" + Q(f.component) + ",\"property\":" + Q(f.property) + ",\"category\":" + Q(f.category) + "}");
        result.Log("{\"asset\":" + Q(report.asset) + ",\"objects\":" + report.objects + ",\"components\":" + report.components + ",\"emptyReferences\":" + report.emptyReferences + ",\"dirtyBefore\":" + report.dirtyBefore.ToString().ToLowerInvariant() + ",\"dirtyAfter\":" + report.dirtyAfter.ToString().ToLowerInvariant() + ",\"findings\":[" + string.Join(",", entries) + "]}");
    }
    private static string GetPath(Transform target)
    {
        var parts = new List<string>();
        while (target != null) { parts.Add(target.name + "[" + target.GetSiblingIndex() + "]"); target = target.parent; }
        parts.Reverse();
        return string.Join("/", parts);
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
}
