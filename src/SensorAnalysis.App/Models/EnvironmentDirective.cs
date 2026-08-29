using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SensorAnalysis.Core.Deployment;

namespace SensorAnalysis.App.Models;

/// <summary>
/// One editable field in the directive draft (deployer or deployable parameter).
/// </summary>
public partial class DirectiveParameterField : ObservableObject
{
    /// <summary>Creates a field from catalog metadata and a current value.</summary>
    /// <param name="name">Options property name.</param>
    /// <param name="label">UI label.</param>
    /// <param name="required">Whether empty values block Add.</param>
    /// <param name="description">Tooltip / help text.</param>
    /// <param name="propertyType">CLR type of the underlying property.</param>
    /// <param name="value">Current or default value.</param>
    /// <param name="choiceValues">Combo choices when applicable.</param>
    /// <param name="isDeployableType">True when this field selects a deployable type name.</param>
    public DirectiveParameterField(
        string name,
        string label,
        bool required,
        string description,
        Type propertyType,
        object? value,
        IReadOnlyList<string>? choiceValues = null,
        bool isDeployableType = false)
    {
        Name = name;
        Label = label;
        Required = required;
        Description = description;
        PropertyType = propertyType;
        IsDeployableType = isDeployableType;
        ChoiceValues = choiceValues ?? Array.Empty<string>();
        SetValueFromObject(value);
    }

    /// <summary>Options property name.</summary>
    public string Name { get; }

    /// <summary>UI label.</summary>
    public string Label { get; }

    /// <summary>Whether empty values block Add.</summary>
    public bool Required { get; }

    /// <summary>Tooltip / help.</summary>
    public string Description { get; }

    /// <summary>True when this field selects a deployable type name.</summary>
    public bool IsDeployableType { get; }

    /// <summary>CLR type of the underlying property.</summary>
    public Type PropertyType { get; }

    /// <summary>Combo choices when <see cref="IsChoiceCombo"/>.</summary>
    public IReadOnlyList<string> ChoiceValues { get; }

    /// <summary>True when the control should be a checkbox.</summary>
    public bool IsBool => PropertyType == typeof(bool);

    /// <summary>True when the control should be a combo.</summary>
    public bool IsChoiceCombo => IsDeployableType || ChoiceValues.Count > 0;

    /// <summary>True when the control should be a free-text / numeric box.</summary>
    public bool IsText => !IsBool && !IsChoiceCombo;

    [ObservableProperty] private string _textValue = "";
    [ObservableProperty] private bool _boolValue;
    [ObservableProperty] private string _choiceValue = "";

    /// <summary>Whether the current value satisfies required/parse rules.</summary>
    public bool IsValid
    {
        get
        {
            if (IsBool) return true;
            if (IsChoiceCombo)
            {
                if (!Required && string.IsNullOrWhiteSpace(ChoiceValue)) return true;
                return !string.IsNullOrWhiteSpace(ChoiceValue);
            }

            if (string.IsNullOrWhiteSpace(TextValue))
                return !Required;

            if (PropertyType == typeof(int) || PropertyType == typeof(long))
                return long.TryParse(TextValue, out _);
            if (PropertyType == typeof(double) || PropertyType == typeof(float))
                return double.TryParse(TextValue, out _);
            return true;
        }
    }

    /// <summary>Boxed value suitable for directive parameter storage.</summary>
    /// <returns>Property value in a type suitable for serialization.</returns>
    public object? GetBoxedValue()
    {
        if (IsBool) return BoolValue;
        if (IsChoiceCombo) return ChoiceValue;
        if (string.IsNullOrWhiteSpace(TextValue)) return GetDefaultForType();
        if (PropertyType == typeof(int)) return int.Parse(TextValue);
        if (PropertyType == typeof(long)) return long.Parse(TextValue);
        if (PropertyType == typeof(double)) return double.Parse(TextValue);
        if (PropertyType == typeof(float)) return float.Parse(TextValue);
        return TextValue;
    }

    private object? GetDefaultForType()
    {
        if (PropertyType == typeof(int) || PropertyType == typeof(long)) return 0;
        if (PropertyType == typeof(double) || PropertyType == typeof(float)) return 0.0;
        if (PropertyType == typeof(bool)) return false;
        return "";
    }

    private void SetValueFromObject(object? value)
    {
        if (IsBool)
        {
            BoolValue = value is true;
            return;
        }

        if (IsChoiceCombo)
        {
            ChoiceValue = value?.ToString() ?? "";
            return;
        }

        TextValue = value?.ToString() ?? "";
    }

    partial void OnTextValueChanged(string value) => OnPropertyChanged(nameof(IsValid));
    partial void OnBoolValueChanged(bool value) => OnPropertyChanged(nameof(IsValid));
    partial void OnChoiceValueChanged(string value) => OnPropertyChanged(nameof(IsValid));
}

/// <summary>
/// A scheduled environment change: at <see cref="AtTick"/>, run <see cref="DeployerName"/>
/// with a captured options property bag.
/// </summary>
public partial class EnvironmentDirective : ObservableObject
{
    private static int _nextId;

    /// <summary>Stable id for list identity and RNG salt.</summary>
    public int Id { get; } = Interlocked.Increment(ref _nextId);

    /// <summary>Tick at which this directive should run (0 = before first step).</summary>
    [ObservableProperty] private long _atTick;

    /// <summary>Registered deployer name.</summary>
    [ObservableProperty] private string _deployerName = "Gaussian";

    /// <summary>RNG salt mixed into the deployer (0 is the shared CLI/GUI default).</summary>
    public int Salt { get; set; }

    /// <summary>Captured deployer and deployable parameter values.</summary>
    public Dictionary<string, object?> Parameters { get; set; } = new(StringComparer.Ordinal);

    /// <summary>True after the directive has been executed in the current world session.</summary>
    [ObservableProperty] private bool _isApplied;

    /// <summary>Last error message if apply failed; otherwise empty.</summary>
    [ObservableProperty] private string _status = "";

    /// <summary>One-line summary for the directive list.</summary>
    public string Summary
    {
        get
        {
            var bits = Parameters.Select(kv => $"{kv.Key}={kv.Value}");
            return $"t={AtTick}: {DeployerName} ({string.Join(", ", bits)})";
        }
    }

    partial void OnAtTickChanged(long value) => OnPropertyChanged(nameof(Summary));
    partial void OnDeployerNameChanged(string value) => OnPropertyChanged(nameof(Summary));
}
