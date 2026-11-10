using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DualSensePrecision.App.Controls;

public enum SliderFormat { Percent, Multiplier, Plain }

/// <summary>Label + live value + slider + one-line description. Value is two-way bindable.</summary>
public sealed partial class SliderRow : UserControl
{
    private bool _ready;

    public SliderRow()
    {
        InitializeComponent();
        Slider.Minimum = 0; Slider.Maximum = 1; Slider.StepFrequency = 0.01; Slider.SmallChange = 0.01; Slider.LargeChange = 0.1;
        // Ignore the slider's own range-coercion events during construction; only user input after load flows out.
        Loaded += (_, _) => { _ready = true; Slider.Value = Value; UpdateText(); };
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SliderRow), new PropertyMetadata(0.0, (d, _) => ((SliderRow)d).OnValueChanged()));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(SliderRow), new PropertyMetadata(0.0, (d, e) => ((SliderRow)d).Slider.Minimum = (double)e.NewValue));
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(SliderRow), new PropertyMetadata(1.0, (d, e) => ((SliderRow)d).Slider.Maximum = (double)e.NewValue));
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(SliderRow), new PropertyMetadata(0.01, (d, e) =>
        {
            var s = (SliderRow)d;
            s.Slider.StepFrequency = (double)e.NewValue;
            s.Slider.SmallChange = (double)e.NewValue;
        }));
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(SliderRow), new PropertyMetadata("", (d, e) => ((SliderRow)d).LabelText.Text = (string)e.NewValue));
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SliderRow), new PropertyMetadata("", (d, e) => ((SliderRow)d).DescriptionText.Text = (string)e.NewValue));
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(SliderFormat), typeof(SliderRow), new PropertyMetadata(SliderFormat.Percent, (d, _) => ((SliderRow)d).UpdateText()));
    public SliderFormat Format { get => (SliderFormat)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }

    private void OnValueChanged()
    {
        if (Math.Abs(Slider.Value - Value) > 1e-9) Slider.Value = Value;
        UpdateText();
    }

    private void OnSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready) return;
        if (Math.Abs(e.NewValue - Value) > 1e-9) Value = e.NewValue;
    }

    private void UpdateText() => ValueText.Text = Format switch
    {
        SliderFormat.Percent => $"{Value * 100:0.0}%",
        SliderFormat.Multiplier => $"{Value:0.00}×",
        _ => $"{Value:0.##}",
    };
}
