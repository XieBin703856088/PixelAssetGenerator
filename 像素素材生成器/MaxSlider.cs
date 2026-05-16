using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PixelAssetGenerator
{
    public class MaxSlider : UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(MaxSlider),
                new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, null, CoerceValue));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(MaxSlider),
                new PropertyMetadata(0d));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(MaxSlider),
                new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty TickFrequencyProperty =
            DependencyProperty.Register(nameof(TickFrequency), typeof(double), typeof(MaxSlider),
                new PropertyMetadata(0.1d, OnStepPropertyChanged));

        public static readonly DependencyProperty IsSnapToTickEnabledProperty =
            DependencyProperty.Register(nameof(IsSnapToTickEnabled), typeof(bool), typeof(MaxSlider),
                new PropertyMetadata(false, OnStepPropertyChanged));

        public static readonly DependencyProperty SmallChangeProperty =
            DependencyProperty.Register(nameof(SmallChange), typeof(double), typeof(MaxSlider),
                new PropertyMetadata(0.01d));

        public static readonly DependencyProperty LargeChangeProperty =
            DependencyProperty.Register(nameof(LargeChange), typeof(double), typeof(MaxSlider),
                new PropertyMetadata(0.1d));

        private readonly TextBox _valueBox;

        public MaxSlider()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var slider = new Slider
            {
                VerticalAlignment = VerticalAlignment.Center,
                IsMoveToPointEnabled = true
            };
            slider.SetBinding(Slider.MinimumProperty, new Binding(nameof(Minimum)) { Source = this });
            slider.SetBinding(Slider.MaximumProperty, new Binding(nameof(Maximum)) { Source = this });
            slider.SetBinding(Slider.ValueProperty, new Binding(nameof(Value))
            {
                Source = this,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            slider.SetBinding(Slider.TickFrequencyProperty, new Binding(nameof(TickFrequency)) { Source = this });
            slider.SetBinding(Slider.IsSnapToTickEnabledProperty, new Binding(nameof(IsSnapToTickEnabled)) { Source = this });
            slider.SetBinding(Slider.SmallChangeProperty, new Binding(nameof(SmallChange)) { Source = this });
            slider.SetBinding(Slider.LargeChangeProperty, new Binding(nameof(LargeChange)) { Source = this });
            slider.SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = this });

            _valueBox = new TextBox
            {
                MinWidth = 56,
                Height = 28,
                Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(6, 3, 6, 3),
                HorizontalContentAlignment = HorizontalAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _valueBox.SetBinding(TextBox.TextProperty, new Binding(nameof(Value))
            {
                Source = this,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            _valueBox.SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = this });
            _valueBox.LostFocus += ValueBox_LostFocus;
            _valueBox.KeyDown += ValueBox_KeyDown;

            Grid.SetColumn(slider, 0);
            Grid.SetColumn(_valueBox, 1);
            grid.Children.Add(slider);
            grid.Children.Add(_valueBox);

            Content = grid;
        }

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public double TickFrequency
        {
            get => (double)GetValue(TickFrequencyProperty);
            set => SetValue(TickFrequencyProperty, value);
        }

        public bool IsSnapToTickEnabled
        {
            get => (bool)GetValue(IsSnapToTickEnabledProperty);
            set => SetValue(IsSnapToTickEnabledProperty, value);
        }

        public double SmallChange
        {
            get => (double)GetValue(SmallChangeProperty);
            set => SetValue(SmallChangeProperty, value);
        }

        public double LargeChange
        {
            get => (double)GetValue(LargeChangeProperty);
            set => SetValue(LargeChangeProperty, value);
        }

        private static void OnStepPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MaxSlider slider)
            {
                slider.CoerceValue(ValueProperty);
            }
        }

        private static object CoerceValue(DependencyObject d, object baseValue)
        {
            var slider = (MaxSlider)d;
            var value = (double)baseValue;

            if (!slider.IsSnapToTickEnabled || slider.TickFrequency <= 0)
            {
                return value;
            }

            var tick = (decimal)slider.TickFrequency;
            var current = (decimal)value;
            var steps = decimal.Round(current / tick, 0, MidpointRounding.AwayFromZero);
            return (double)(steps * tick);
        }

        private void ValueBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                EnsureMaximumFromValue();
            }
        }

        private void ValueBox_LostFocus(object sender, RoutedEventArgs e)
        {
            EnsureMaximumFromValue();
        }

        private void EnsureMaximumFromValue()
        {
            if (Value > Maximum)
            {
                Maximum = Value;
            }
        }
    }
}
