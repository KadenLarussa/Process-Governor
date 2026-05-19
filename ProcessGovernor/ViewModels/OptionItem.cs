namespace ProcessGovernor.ViewModels;

public sealed class OptionItem<T>
{
    public OptionItem(T value, string label, string description)
    {
        Value = value;
        Label = label;
        Description = description;
    }

    public T Value { get; }

    public string Label { get; }

    public string Description { get; }

    public override string ToString() => Label;
}
