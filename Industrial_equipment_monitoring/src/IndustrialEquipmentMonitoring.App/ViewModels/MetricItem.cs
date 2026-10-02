namespace IndustrialEquipmentMonitoring.App.ViewModels;

public sealed class MetricItem : ObservableObject
{
    private string _value;
    private string _caption;
    private string _tone;

    public MetricItem(string label, string value, string caption, string tone)
    {
        Label = label;
        _value = value;
        _caption = caption;
        _tone = tone;
    }

    public string Label { get; }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public string Caption
    {
        get => _caption;
        set => SetProperty(ref _caption, value);
    }

    public string Tone
    {
        get => _tone;
        set => SetProperty(ref _tone, value);
    }
}
