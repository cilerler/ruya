using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ruya.OpenTelemetry;

/// <summary>
/// Detects Kubernetes and container runtime attributes.
/// </summary>
internal static class EnvironmentDetector
{
    public static bool IsRunningInKubernetes()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST"));
    }

    public static bool IsRunningInContainer()
    {
        return Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true"
            || File.Exists("/.dockerenv")
            || File.Exists("/run/.containerenv");
    }

    public static Dictionary<string, object> DetectKubernetesAttributes()
    {
        var attributes = new Dictionary<string, object>();

        TryAddEnvVar(attributes, "k8s.namespace.name", "KUBERNETES_NAMESPACE", "POD_NAMESPACE");
        TryAddEnvVar(attributes, "k8s.pod.name", "KUBERNETES_POD_NAME", "HOSTNAME");
        TryAddEnvVar(attributes, "k8s.node.name", "KUBERNETES_NODE_NAME", "NODE_NAME");
        TryAddEnvVar(attributes, "k8s.deployment.name", "KUBERNETES_DEPLOYMENT_NAME");
        TryAddEnvVar(attributes, "k8s.container.name", "KUBERNETES_CONTAINER_NAME");

        return attributes;
    }

    public static Dictionary<string, object> DetectContainerAttributes()
    {
        var attributes = new Dictionary<string, object>();

        try
        {
            if (File.Exists("/proc/self/cgroup"))
            {
                var cgroupContent = File.ReadAllText("/proc/self/cgroup");
                var containerId = ExtractContainerId(cgroupContent);
                if (!string.IsNullOrEmpty(containerId))
                {
                    attributes["container.id"] = containerId;
                }
            }
        }
        catch
        {
            // Ignore - running without container ID
        }

        TryAddEnvVar(attributes, "container.runtime", "DOTNET_ASPIRE_CONTAINER_RUNTIME");

        return attributes;
    }

    private static string? ExtractContainerId(string cgroupContent)
    {
        foreach (var line in cgroupContent.Split('\n'))
        {
            if (line.Contains("docker") || line.Contains("containerd") || line.Contains("cri-o"))
            {
                var parts = line.Split('/');
                var lastPart = parts.LastOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(lastPart) && lastPart.Length >= 12)
                {
                    return lastPart.Length > 64 ? lastPart[..64] : lastPart;
                }
            }
        }
        return null;
    }

    private static void TryAddEnvVar(Dictionary<string, object> attributes, string key, params string[] envVarNames)
    {
        foreach (var envVar in envVarNames)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(value))
            {
                attributes[key] = value;
                return;
            }
        }
    }
}
