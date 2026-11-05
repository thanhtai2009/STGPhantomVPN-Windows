using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace STGPhantomVPN.ViewModels
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void Raise([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }
    }

    public sealed class RelayCommand : ICommand
    {
        readonly Action<object> _run;
        readonly Func<object, bool> _can;

        public RelayCommand(Action<object> run, Func<object, bool> can = null)
        {
            _run = run;
            _can = can;
        }

        public RelayCommand(Action run, Func<bool> can = null) : this(_ => run(), can == null ? (Func<object, bool>)null : _ => can()) { }

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object parameter) => _can == null || _can(parameter);
        public void Execute(object parameter) => _run(parameter);
    }

    /// <summary>Fire-and-forget async command; exceptions are logged instead of crashing the dispatcher.</summary>
    public sealed class AsyncCommand : ICommand
    {
        readonly Func<object, Task> _run;
        readonly Func<object, bool> _can;
        bool _running;

        public AsyncCommand(Func<object, Task> run, Func<object, bool> can = null)
        {
            _run = run;
            _can = can;
        }

        public AsyncCommand(Func<Task> run, Func<bool> can = null) : this(_ => run(), can == null ? (Func<object, bool>)null : _ => can()) { }

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object parameter) => !_running && (_can == null || _can(parameter));

        public async void Execute(object parameter)
        {
            _running = true;
            CommandManager.InvalidateRequerySuggested();
            try { await _run(parameter); }
            catch (Exception ex) { Services.Log.Error("Command failed", ex); }
            finally
            {
                _running = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
