using System;
using System.Threading.Tasks;
using FirstFloor.ModernUI.Helpers;
using JetBrains.Annotations;

namespace FirstFloor.ModernUI.Commands {
    public interface IAsyncCommand {
        bool IsInProcess { get; }
        Task ExecuteAsync([CanBeNull] object parameter);
    }

    public class AsyncCommand : CommandExt, IAsyncCommand {
        [NotNull]
        private readonly Func<Task> _execute;

        [CanBeNull]
        private readonly Func<bool> _canExecute;

        private bool _inProcess;
        public bool IsInProcess => _inProcess;

        public Task ExecuteAsync(object parameter) {
            return ExecuteAsync();
        }

        private readonly TimeSpan _additionalDelay;

        public AsyncCommand([NotNull] Func<Task> execute, Func<bool> canExecute, TimeSpan additionalDelay = default(TimeSpan), bool isAutomaticRequeryDisabled = false)
                : base(false, isAutomaticRequeryDisabled) {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
            _additionalDelay = additionalDelay;
        }

        public AsyncCommand([NotNull] Func<Task> execute, TimeSpan additionalDelay = default(TimeSpan), bool isAutomaticRequeryDisabled = false)
                : this(execute, null, additionalDelay, isAutomaticRequeryDisabled) {}

        protected override bool CanExecuteOverride() {
            return !_inProcess && (_canExecute == null || _canExecute());
        }

        protected override void ExecuteOverride() {
            ExecuteAsync().Forget();
        }

        protected virtual Task ExecuteInner() {
            return _execute();
        }

        public async Task ExecuteAsync() {
            try {
                _inProcess = true;
                RaiseCanExecuteChanged();

                await ExecuteInner();

                if (_additionalDelay != TimeSpan.Zero) {
                    await Task.Delay(_additionalDelay);
                }
            } catch(Exception e) {
                Logging.Error(e);
                throw;
            } finally {
                _inProcess = false;
                RaiseCanExecuteChanged();
            }
        }
    }

    public class AsyncCommand<T> : CommandExt<T>, IAsyncCommand {
        [NotNull]
        private readonly Func<T, Task> _execute;

        [CanBeNull]
        private readonly Func<T, bool> _canExecute;

        private bool _inProcess;
        public bool IsInProcess => _inProcess;

        public Task ExecuteAsync(object parameter) {
            if (parameter == null) return ExecuteAsync(default(T));
            return ConvertXamlCompatible(parameter, out T value) ? ExecuteAsync(value) : Task.Delay(0);
        }

        private readonly int _additionalDelay;

        public AsyncCommand([NotNull] Func<T, Task> execute, Func<T, bool> canExecute = null, int additionalDelay = 0, bool isAutomaticRequeryDisabled = false)
                : base(false, isAutomaticRequeryDisabled) {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
            _additionalDelay = additionalDelay;
        }

        protected override bool CanExecuteOverride(T parameter) {
            return !_inProcess && (_canExecute == null || _canExecute(parameter));
        }

        public T CurrentParameter;

        public async Task ExecuteAsync(T parameter) {
            try {
                _inProcess = true;
                CurrentParameter = parameter;
                RaiseCanExecuteChanged();

                await _execute(parameter);

                if (_additionalDelay != 0) {
                    await Task.Delay(_additionalDelay);
                }
            } catch (Exception e) {
                Logging.Error(e);
                throw;
            } finally {
                _inProcess = false;
                CurrentParameter = default(T);
                RaiseCanExecuteChanged();
            }
        }

        protected override void ExecuteOverride(T parameter) {
            ExecuteAsync(parameter).Forget();
        }
    }
}