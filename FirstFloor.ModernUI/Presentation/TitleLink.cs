using System.ComponentModel;
using System.Windows.Markup;

namespace FirstFloor.ModernUI.Presentation {
    [ContentProperty(nameof(Content))]
    public sealed class TitleLink : Link {
        private object _content;

        [Bindable(true)]
        public object Content {
            get { return _content; }
            set {
                if (Equals(value, _content)) return;
                _content = value;
                OnPropertyChanged();
            }
        }

        public override string DisplayName {
            get { return Content?.ToString(); }
            set { Content = value; }
        }

        private string _groupKey;

        public string GroupKey {
            get { return _groupKey; }
            set {
                if (Equals(value, _groupKey)) return;
                _groupKey = value;
                OnPropertyChanged();
            }
        }

        private bool _isAccented;

        public bool IsAccented {
            get { return _isAccented; }
            set {
                if (Equals(value, _isAccented)) return;
                _isAccented = value;
                OnPropertyChanged();
            }
        }
    }
}