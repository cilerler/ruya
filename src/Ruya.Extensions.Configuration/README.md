# Ruya.Extensions.Configuration

Configuration extensions for .NET applications.

## Features

- **Feature flags**: `GetFeatureFlag<T>()` reads `FeatureManagement:{FeatureFlag}` from a public static
  `FeatureFlag` field on the settings type. A missing flag is disabled; a malformed Boolean fails configuration
  binding instead of being treated as enabled.
- **Prefixed environment variables**: `AddEnvironmentVariablesWithPrefix()` reads
  `EnvironmentVariablesPrefix` and, when configured, adds that prefixed provider at the call site. The prefix is
  removed from the resulting configuration keys.
- **Kubernetes configuration**: `AddKubernetesConfiguration()` loads the optional ConfigMap first and the optional
  Secret second, so secret values have higher precedence.
