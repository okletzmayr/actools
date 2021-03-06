using System.Windows;
using System.Windows.Controls;
using FirstFloor.ModernUI.Helpers;

namespace FirstFloor.ModernUI.Windows.Controls {
    public class RememberingExpander : Expander {
        public static readonly DependencyProperty DefaultValueProperty = DependencyProperty.Register(nameof(DefaultValue), typeof(bool),
                typeof(RememberingExpander), new PropertyMetadata(true, (o, e) => {
                    ((RememberingExpander)o)._defaultValue = (bool)e.NewValue;
                }));

        private bool _defaultValue = true;

        public bool DefaultValue {
            get => _defaultValue;
            set => SetValue(DefaultValueProperty, value);
        }

        public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(nameof(Key), typeof(string),
                typeof(RememberingExpander), new PropertyMetadata(OnKeyChanged));

        public string Key {
            get => (string)GetValue(KeyProperty);
            set => SetValue(KeyProperty, value);
        }

        private static void OnKeyChanged(DependencyObject o, DependencyPropertyChangedEventArgs e) {
            ((RememberingExpander)o).OnKeyChanged((string)e.NewValue);
        }

        private readonly Busy _busy = new Busy();
        private bool _loaded;

        private void OnKeyChanged(string newValue) {
            if (newValue != null) {
                _busy.DoDelay(() => {
                    IsExpanded = ValuesStorage.GetBool(newValue, DefaultValue);
                    _loaded = true;
                }, 10);
            }
        }

        protected override void OnExpanded() {
            base.OnExpanded();
            if (_loaded) {
                if (DefaultValue) {
                    ValuesStorage.Remove(Key);
                } else {
                    ValuesStorage.Set(Key, true);
                }
            }
        }

        protected override void OnCollapsed() {
            base.OnCollapsed();
            if (_loaded) {
                if (!DefaultValue) {
                    ValuesStorage.Remove(Key);
                } else {
                    ValuesStorage.Set(Key, false);
                }
            }
        }
    }
}
