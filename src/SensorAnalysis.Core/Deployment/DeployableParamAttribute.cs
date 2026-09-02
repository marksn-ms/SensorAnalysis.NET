using System.ComponentModel;
using System.Reflection;

namespace SensorAnalysis.Core.Deployment;

/// <summary>
/// Declares a configurable property on a deployable type (agent, incident, …)
/// shown in the UI when that type is selected in a deployer directive.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DeployableParamAttribute : Attribute
{
    /// <summary>Creates a deployable parameter descriptor.</summary>
    /// <param name="label">Short label for the form field.</param>
    public DeployableParamAttribute(string label) => Label = label;

    /// <summary>Human-readable field label.</summary>
    public string Label { get; }

    /// <summary>When <c>true</c>, a value is required before adding a directive.</summary>
    public bool Required { get; set; } = true;

    /// <summary>Optional help text for tooltips.</summary>
    public string? Description { get; set; }
}

/// <summary>Runtime description of one deployable configuration property.</summary>
public sealed class DeployableParameterInfo
{
    /// <summary>Creates a deployable parameter info record.</summary>
    /// <param name="property">Reflected property on the configuration type.</param>
    /// <param name="label">UI label from <see cref="DeployableParamAttribute"/>.</param>
    /// <param name="required">Whether the field is required.</param>
    /// <param name="description">Optional help text.</param>
    public DeployableParameterInfo(PropertyInfo property, string label, bool required, string? description)
    {
        Property = property;
        Label = label;
        Required = required;
        Description = description;
    }

    /// <summary>Reflected property on the configuration type.</summary>
    public PropertyInfo Property { get; }

    /// <summary>Property name (dictionary key).</summary>
    public string Name => Property.Name;

    /// <summary>UI label.</summary>
    public string Label { get; }

    /// <summary>Whether a value is required before adding a directive.</summary>
    public bool Required { get; }

    /// <summary>Optional help text.</summary>
    public string? Description { get; }

    /// <summary>CLR type of the property.</summary>
    public Type PropertyType => Property.PropertyType;
}

/// <summary>Reflection helpers for deployable configuration types.</summary>
public static class DeployableParameterCatalog
{
    /// <summary>
    /// Lists public instance properties marked with <see cref="DeployableParamAttribute"/>,
    /// in declaration order.
    /// </summary>
    /// <param name="configType">Deployable configuration CLR type, or <c>null</c> for none.</param>
    /// <returns>Parameter descriptors; empty when <paramref name="configType"/> is <c>null</c>.</returns>
    public static IReadOnlyList<DeployableParameterInfo> Describe(Type? configType)
    {
        if (configType is null) return Array.Empty<DeployableParameterInfo>();
        var list = new List<DeployableParameterInfo>();
        foreach (var prop in configType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            var attr = prop.GetCustomAttribute<DeployableParamAttribute>();
            if (attr is null) continue;
            list.Add(new DeployableParameterInfo(prop, attr.Label, attr.Required, attr.Description));
        }
        return list;
    }

    /// <summary>Reads default values from a fresh configuration instance.</summary>
    /// <param name="configType">Deployable configuration CLR type, or <c>null</c> for none.</param>
    /// <returns>Name→default value map.</returns>
    public static Dictionary<string, object?> Defaults(Type? configType)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (configType is null) return map;
        var instance = Activator.CreateInstance(configType);
        if (instance is null) return map;
        foreach (var p in Describe(configType))
            map[p.Name] = p.Property.GetValue(instance);
        return map;
    }

    /// <summary>Copies matching values from a dictionary onto a configuration instance.</summary>
    /// <param name="target">Configuration instance to update.</param>
    /// <param name="values">Source name→value map.</param>
    public static void Apply(object target, IReadOnlyDictionary<string, object?> values)
    {
        foreach (var p in Describe(target.GetType()))
        {
            if (!values.TryGetValue(p.Name, out var raw) || raw is null) continue;
            p.Property.SetValue(target, ConvertValue(raw, p.PropertyType));
        }
    }

    /// <summary>Extracts deployable-parameter keys from a full directive parameter bag.</summary>
    /// <param name="all">Full directive parameter bag.</param>
    /// <param name="configType">Deployable configuration CLR type, or <c>null</c> for none.</param>
    /// <returns>Subset of <paramref name="all"/> relevant to the deployable type.</returns>
    public static Dictionary<string, object?> Extract(
        IReadOnlyDictionary<string, object?> all,
        Type? configType)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (configType is null) return map;
        foreach (var p in Describe(configType))
        {
            if (all.TryGetValue(p.Name, out var value))
                map[p.Name] = value;
        }
        return map;
    }

    /// <summary>
    /// Returns <c>true</c> when every required deployable parameter has a usable value.
    /// </summary>
    /// <param name="configType">Deployable configuration CLR type, or <c>null</c> for none.</param>
    /// <param name="values">Parameter bag to validate.</param>
    /// <param name="error">First validation failure message, if any.</param>
    /// <returns><c>true</c> when validation passes.</returns>
    public static bool TryValidate(
        Type? configType,
        IReadOnlyDictionary<string, object?> values,
        out string? error)
    {
        if (configType is null)
        {
            error = null;
            return true;
        }

        foreach (var p in Describe(configType))
        {
            if (!p.Required) continue;
            if (!values.TryGetValue(p.Name, out var value) || value is null)
            {
                error = $"{p.Label} is required.";
                return false;
            }

            if (p.PropertyType == typeof(string) && string.IsNullOrWhiteSpace((string)value))
            {
                error = $"{p.Label} is required.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static object? ConvertValue(object raw, Type target)
    {
        if (raw is System.Text.Json.JsonElement je)
        {
            raw = je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Null => null!,
                System.Text.Json.JsonValueKind.String => je.GetString()!,
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Number => je.GetDouble(),
                _ => je.GetRawText()
            };
        }

        if (raw is null) return null;
        if (target.IsInstanceOfType(raw)) return raw;
        if (target == typeof(string)) return Convert.ToString(raw);
        if (target == typeof(bool)) return Convert.ToBoolean(raw);
        if (target == typeof(int)) return Convert.ToInt32(raw);
        if (target == typeof(long)) return Convert.ToInt64(raw);
        if (target == typeof(double)) return Convert.ToDouble(raw);
        if (target == typeof(float)) return Convert.ToSingle(raw);
        return TypeDescriptor.GetConverter(target).ConvertFrom(raw);
    }
}

/// <summary>Default deployment-time settings shared by all agent factories.</summary>
public sealed class AgentDeployDefaults
{
    /// <summary>Sensor range assigned to placed agents.</summary>
    [DeployableParam("Sensor range")]
    public double SensorRange { get; set; } = 80;

    /// <summary>
    /// Maximum agents that may share one event (orbiting cooperative agents only).
    /// </summary>
    [DeployableParam("Max share event", Required = false,
        Description = "Max agents per event for OrbitingCoop / MitigatedOrbit.")]
    public int MaxShareEvent { get; set; } = 3;
}
