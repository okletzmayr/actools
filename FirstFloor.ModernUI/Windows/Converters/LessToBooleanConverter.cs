using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using JetBrains.Annotations;

namespace FirstFloor.ModernUI.Windows.Converters {
    [ValueConversion(typeof(double), typeof(bool))]
    public class LessToBooleanConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            return value.AsDouble() < parameter.AsDouble();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }

    [ValueConversion(typeof(double), typeof(double))]
    public class LogarithmicScale : DependencyObject, IValueConverter, IMultiValueConverter {
        private bool _dirty = true;

        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double),
                typeof(LogarithmicScale), new PropertyMetadata(0d, (o, e) => {
                    var l = (LogarithmicScale)o;
                    l._minimum = (double)e.NewValue;
                    l._dirty = true;
                }));

        private double _minimum = 0d;

        public double Minimum {
            get => _minimum;
            set => SetValue(MinimumProperty, value);
        }

        public static readonly DependencyProperty MiddleProperty = DependencyProperty.Register(nameof(Middle), typeof(double),
                typeof(LogarithmicScale), new PropertyMetadata(0d, (o, e) => {
                    var l = (LogarithmicScale)o;
                    l._middle = (double)e.NewValue;
                    l._dirty = true;
                }));

        private double _middle = 0d;

        public double Middle {
            get => _middle;
            set => SetValue(MiddleProperty, value);
        }

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double),
                typeof(LogarithmicScale), new PropertyMetadata(0d, (o, e) => {
                    var l = (LogarithmicScale)o;
                    l._maximum = (double)e.NewValue;
                    l._dirty = true;
                }));

        private double _maximum = 0d;

        public double Maximum {
            get => _maximum;
            set => SetValue(MaximumProperty, value);
        }

        public static readonly DependencyProperty FixedValuesProperty = DependencyProperty.Register(nameof(FixedValues), typeof(string),
                typeof(LogarithmicScale), new PropertyMetadata(null, (o, e) => {
                    var l = (LogarithmicScale)o;
                    l._fixedValues = (string)e.NewValue;
                    l._fixedValuesArray = l._fixedValues?
                            .Split(new[]{ ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.AsDouble()).ToArray();
                    l._dirty = true;
                }));

        [CanBeNull]
        private double[] _fixedValuesArray;
        private string _fixedValues;

        public string FixedValues {
            get => _fixedValues;
            set => SetValue(FixedValuesProperty, value);
        }

        private double _a, _b, _c;
        private bool _linear;

        private void Update() {
            if (!_dirty) return;
            _dirty = false;

            GetCoefficients(_minimum, _middle, _maximum, out _linear, out _a, out _b, out _c);
        }

        public static void GetCoefficients(double minimum, double middle, double maximum,
                out bool linear, out double a, out double b, out double c) {
            var d = minimum - 2d * middle + maximum;
            if (d == 0d || middle == minimum) {
                linear = true;
                a = minimum;
                b = maximum - minimum;
                c = 0d;
            } else {
                linear = false;
                a = (minimum * maximum - middle * middle) / d;
                b = Math.Pow(middle - minimum, 2d) / d;
                c = 2d * Math.Log((maximum - middle) / (middle - minimum));
            }
        }

        public static double Convert(double value, bool isLinear, double a, double b, double c) {
            return isLinear ? (value - a) / b : Math.Log((value - a) / b) / c;
        }

        public static double ConvertBack(double value, bool isLinear, double a, double b, double c, [CanBeNull] double[] fixedValues) {
            var linear = isLinear ? value * b + a : a + b * Math.Exp(c * value);
            return fixedValues?.Aggregate((x, y) => Math.Abs(x - linear) < Math.Abs(y - linear) ? x : y) ?? linear;
        }

        object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            Update();
            return Convert(value.AsDouble(), _linear, _a, _b, _c);
        }

        object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            Update();
            return ConvertBack(value.AsDouble(), _linear, _a, _b, _c, _fixedValuesArray);
        }

        object IMultiValueConverter.Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            Update();
            return Convert(values.FirstOrDefault().AsDouble(), _linear, _a, _b, _c);
        }

        object[] IMultiValueConverter.ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            Update();
            return new object[] { ConvertBack(value.AsDouble(), _linear, _a, _b, _c, _fixedValuesArray) };
        }
    }
}