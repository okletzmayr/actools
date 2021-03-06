using System;
using System.Threading;
using System.Threading.Tasks;
using FirstFloor.ModernUI.Commands;
using FirstFloor.ModernUI.Dialogs;
using JetBrains.Annotations;

namespace FirstFloor.ModernUI.Helpers {
    public class NonfatalErrorSolution : AsyncCommand {
        private readonly NonfatalErrorEntry _entry;
        private readonly Func<CancellationToken, Task> _execute;

        [NotNull]
        public string DisplayName { get; }

        private bool _solved;

        public bool Solved {
            get => _solved;
            set {
                if (value == _solved) return;
                _solved = value;
                OnPropertyChanged();
                RaiseCanExecuteChanged();
            }
        }

        public NonfatalErrorSolution([CanBeNull] string displayName, NonfatalErrorEntry entry, [CanBeNull] Func<CancellationToken, Task> execute)
                : base(() => Task.Delay(0), () => execute != null) {
            _entry = entry;
            _execute = execute;
            DisplayName = displayName ?? "Fix it";
        }

        protected override bool CanExecuteOverride() {
            return base.CanExecuteOverride() && !Solved;
        }

        protected override async Task ExecuteInner() {
            Logging.Debug("Fixing error: " + DisplayName);

            try {
                using (var waiting = new WaitingDialog()) {
                    waiting.Report("Solving the issue…");
                    await Task.Yield().ConfigureAwait(false);
                    await _execute(CancellationToken.None).ConfigureAwait(false);
                }
            } catch (Exception e) when (e.IsCanceled()) {
                return;
            } catch (Exception e) {
                NonfatalError.Notify("Can’t solve the issue", e);
                return;
            }

            Solved = true;

            if (_entry != null) {
                NonfatalError.Instance.Errors.Remove(_entry);
            }
        }
    }
}