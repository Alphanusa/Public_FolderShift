using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace FolderShift
{
    public abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }

        protected void Raise(params string[] names)
        {
            foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }

    /// <summary>Dasar layar: status sibuk, pesan error, dan cara aman menjalankan pekerjaan async.</summary>
    public abstract class Page : Observable
    {
        bool busy;
        string busyText;
        string error;

        public bool Busy { get => busy; private set { Set(ref busy, value); Raise(nameof(ShowBusy)); } }
        public string BusyText { get => busyText; set { Set(ref busyText, value); Raise(nameof(ShowBusy)); } }
        /// <summary>Lapisan "sedang bekerja" hanya untuk pekerjaan yang punya pesan.</summary>
        public bool ShowBusy => busy && !string.IsNullOrEmpty(busyText);
        public string Error { get => error; set { Set(ref error, value); Raise(nameof(HasError)); } }
        public bool HasError => !string.IsNullOrEmpty(error);

        protected async void Run(Func<Task> work, string busyMessage = null)
        {
            if (Busy) return;
            Error = null;
            BusyText = busyMessage;
            Busy = true;
            try { await work(); }
            catch (Exception ex) { Error = ex.Message; }
            finally
            {
                Busy = false;
                BusyText = null;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public sealed class Command : ICommand
    {
        readonly Action run;
        readonly Func<bool> can;

        public Command(Action run, Func<bool> can = null) { this.run = run; this.can = can; }

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object parameter) => can?.Invoke() ?? true;
        public void Execute(object parameter) => run();
    }

    public sealed class InverseBoolToVisibility : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
