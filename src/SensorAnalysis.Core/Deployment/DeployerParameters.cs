using System.ComponentModel;
using System.Reflection;

using SensorAnalysis.Core.Domain;

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
    /// When set, treats the property as a deployable-type name picker (unified registry).
    /// </summary>
    public bool IsDeployableType { get; set; }
}

/// <summary>
/// Runtime description of one deployer option property for binding UIs.
/// </summary>
public sealed class DeployerParameterInfo
{
    /// <summary>Creates a parameter info record.</summary>
    /// <param name="property">Reflected property on the options type.</param>
    /// <param name="label">UI label from <see cref="DeployerParamAttribute"/>.</param>
    /// <param name="required">Whether the field is required.</param>
    /// <param name="description">Optional help text.</param>
    /// <param name="isDeployableType">True when the value is chosen from the deployable registry.</param>
    public DeployerParameterInfo(
        PropertyInfo property,
        string label,
        bool required,
        string? description,
        bool isDeployableType = false)
    {
        Property = property;
        Label = label;
        Required = required;
        Description = description;
        IsDeployableType = isDeployableType;
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

    /// <summary>True when the value should be chosen from registered deployable types.</summary>
    public bool IsDeployableType { get; }

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
    /// <param name="optionsType">Deployer options CLR type.</param>
    /// <param name="options">Unused; reserved for future filtering.</param>
    /// <returns>Parameter descriptors.</returns>
    public static IReadOnlyList<DeployerParameterInfo> Describe(Type optionsType, object? options = null)
    {
        _ = options;
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
                attr.IsDeployableType));
        }
        return list;
    }

    /// <summary>Reads <c>TypeName</c> from options when present.</summary>
    /// <param name="options">Deployer options instance.</param>
    /// <returns>Type name string, or <c>null</c> when absent.</returns>
    public static string? GetTypeName(object options) =>
        options.GetType().GetProperty("TypeName")?.GetValue(options) as string;

    /// <summary>
    /// Returns <c>true</c> when every required parameter on <paramref name="options"/> has a usable value.
    /// </summary>
    /// <param name="options">Options instance.</param>
    /// <param name="error">First validation failure message, if any.</param>
    public static bool TryValidate(object options, out string? error)
    {
        foreach (var p in Describe(options.GetType(), options))
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
        foreach (var p in Describe(options.GetType(), options))
            map[p.Name] = p.Property.GetValue(options);
        return map;
    }

    /// <summary>One-line summary of captured parameters.</summary>
    public static string Summarize(object options)
    {
        var parts = new List<string>();
        foreach (var p in Describe(options.GetType(), options))
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
        if (raw is System.Text.Json.JsonElement je)
        {
            raw = je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Null => null!,
                System.Text.Json.JsonValueKind.Undefined => null!,
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
        if (target.IsEnum)
        {
            if (raw is string s)
                return Enum.Parse(target, s, ignoreCase: true);
            return Enum.ToObject(target, Convert.ToInt32(raw));
        }
        return TypeDescriptor.GetConverter(target).ConvertFrom(raw);
    }
}

/// <summary>
/// Services supplied when attaching a deployer (deployable registry, parameter bag, RNG salt).
/// </summary>
public sealed class DeployContext
{
    /// <summary>Registry of deployable type factories.</summary>
    public required DeployableRegistry Deployables { get; init; }

    /// <summary>
    /// Full parameter bag for the current directive (deployer + deployable fields).
    /// </summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; init; }
        = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>Salt mixed into deployer RNGs for additive / multi-directive diversity.</summary>
    /// <remarks>Legacy; deployers draw from <see cref="World.SimWorld.Random"/> instead.</remarks>
    public int RandomSalt { get; init; }

    /// <summary>Extracts per-type configuration for <paramref name="typeName"/>.</summary>
    /// <param name="typeName">Deployable registry key.</param>
    /// <returns>Subset of <see cref="Parameters"/> for that type's <see cref="IDeployableFactory.ConfigType"/>.</returns>
    public IReadOnlyDictionary<string, object?> ParametersFor(string typeName)
    {
        var factory = Deployables.Get(typeName);
        return DeployableParameterCatalog.Extract(Parameters, factory.ConfigType);
    }

    /// <summary>Legacy convenience for code that still resolves agents directly.</summary>
    /// <param name="name">Deployable registry key (must be an agent).</param>
    /// <returns>A new agent instance with parameters applied (not yet placed).</returns>
    /// <exception cref="InvalidOperationException">When <paramref name="name"/> is not an agent.</exception>
    public Agent ResolveAgent(string name)
    {
        var factory = Deployables.Get(name);
        var obj = factory.Create(new DeployableSpawnRequest
        {
            Position = default,
            Label = "",
            Parameters = ParametersFor(name),
            Rng = Random.Shared
        });
        return obj as Agent
            ?? throw new InvalidOperationException($"Deployable '{name}' is not an agent.");
    }
}
