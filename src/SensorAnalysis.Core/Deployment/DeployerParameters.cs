using System.ComponentModel;
using System.Reflection;

namespace SensorAnalysis.Core.Deployment;

/// <summary>
/// Declares how a public property on a deployer options type is shown and validated in the UI.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DeployerParamAttribute : Attribute
{
    /// <summary>Creates a parameter descriptor with the given UI label.</summary>
    /// <param name="label">Short label for the form field.</param>
    public DeployerParamAttribute(string label) => Label = label;

    /// <summary>Human-readable field label.</summary>
    public string Label { get; }

    /// <summary>
    /// When <c>true</c>, the UI must have a non-empty / valid value before Add is enabled.
    /// </summary>
    public bool Required { get; set; } = true;

    /// <summary>Optional longer help text for tooltips.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// When set, treats the property as an agent-factory name picker.
    /// </summary>
    public bool IsAgentType { get; set; }

    /// <summary>
    /// When set, treats the property as an incident-type name (free string / known kinds).
    /// </summary>
    public bool IsIncidentType { get; set; }
}

/// <summary>
/// Runtime description of one deployer option property for binding UIs.
/// </summary>
public sealed class DeployerParameterInfo
{
    /// <summary>Creates a parameter info record.</summary>
    public DeployerParameterInfo(
        PropertyInfo property,
        string label,
        bool required,
        string? description,
        bool isAgentType,
        bool isIncidentType = false)
    {
        Property = property;
        Label = label;
        Required = required;
        Description = description;
        IsAgentType = isAgentType;
        IsIncidentType = isIncidentType;
    }

    /// <summary>Reflected property on the options instance.</summary>
    public PropertyInfo Property { get; }

    /// <summary>Property name (dictionary key).</summary>
    public string Name => Property.Name;

    /// <summary>UI label.</summary>
    public string Label { get; }

    /// <summary>Whether a value is required before adding a directive.</summary>
    public bool Required { get; }

    /// <summary>Optional help text.</summary>
    public string? Description { get; }

    /// <summary>True when the value should be chosen from registered agent factories.</summary>
    public bool IsAgentType { get; }

    /// <summary>True when the value is an incident type name.</summary>
    public bool IsIncidentType { get; }

    /// <summary>CLR type of the property.</summary>
    public Type PropertyType => Property.PropertyType;
}

/// <summary>
/// Reflection helpers for deployer options types annotated with <see cref="DeployerParamAttribute"/>.
/// </summary>
public static class DeployerParameterCatalog
{
    /// <summary>
    /// Lists public instance properties marked with <see cref="DeployerParamAttribute"/>,
    /// in declaration order.
    /// </summary>
    /// <param name="optionsType">Options CLR type.</param>
    /// <returns>Parameter descriptors.</returns>
    public static IReadOnlyList<DeployerParameterInfo> Describe(Type optionsType)
    {
        var list = new List<DeployerParameterInfo>();
        foreach (var prop in optionsType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            var attr = prop.GetCustomAttribute<DeployerParamAttribute>();
            if (attr is null) continue;
            list.Add(new DeployerParameterInfo(
                prop,
                attr.Label,
                attr.Required,
                attr.Description,
                attr.IsAgentType,
                attr.IsIncidentType));
        }
        return list;
    }

    /// <summary>
    /// Returns <c>true</c> when every required parameter on <paramref name="options"/> has a usable value.
    /// </summary>
    /// <param name="options">Options instance.</param>
    /// <param name="error">First validation failure message, if any.</param>
    public static bool TryValidate(object options, out string? error)
    {
        foreach (var p in Describe(options.GetType()))
        {
            if (!p.Required) continue;
            var value = p.Property.GetValue(options);
            if (value is null)
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

    /// <summary>Copies property values from a dictionary into a new options instance.</summary>
    public static object Materialize(Type optionsType, IReadOnlyDictionary<string, object?> values)
    {
        var options = Activator.CreateInstance(optionsType)
            ?? throw new InvalidOperationException($"Cannot create {optionsType.Name}.");
        foreach (var p in Describe(optionsType))
        {
            if (!values.TryGetValue(p.Name, out var raw) || raw is null) continue;
            var converted = ConvertValue(raw, p.PropertyType);
            p.Property.SetValue(options, converted);
        }
        return options;
    }

    /// <summary>Reads annotated properties into a name→value map (for directive storage).</summary>
    public static Dictionary<string, object?> Capture(object options)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var p in Describe(options.GetType()))
            map[p.Name] = p.Property.GetValue(options);
        return map;
    }

    /// <summary>One-line summary of captured parameters.</summary>
    public static string Summarize(object options)
    {
        var parts = new List<string>();
        foreach (var p in Describe(options.GetType()))
        {
            var v = p.Property.GetValue(options);
            if (v is bool b)
                parts.Add($"{p.Label}={b}");
            else
                parts.Add($"{p.Label}={v}");
        }
        return string.Join(", ", parts);
    }

    private static object? ConvertValue(object raw, Type target)
    {
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

/// <summary>
/// Services supplied when attaching a deployer (agent factories, RNG salt).
/// Shared by CLI and UI so placement logic stays in one place.
/// </summary>
public sealed class DeployContext
{
    /// <summary>Resolves an agent factory name to a new agent instance.</summary>
    public required Func<string, Domain.Agent> ResolveAgent { get; init; }

    /// <summary>Salt mixed into deployer RNGs for additive / multi-directive diversity.</summary>
    public int RandomSalt { get; init; }
}
