using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DirectoryAnalyzer.ViewModels
{
    public abstract class BaseAnalyzerViewModel : INotifyPropertyChanged
    {
        private bool _isBusy;
        private string _statusMessage;
        private string _progressMessage;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool IsBusy
        {
            get => _isBusy;
            protected set => SetProperty(ref _isBusy, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            protected set => SetProperty(ref _statusMessage, value);
        }

        public string ProgressMessage
        {
            get => _progressMessage;
            protected set => SetProperty(ref _progressMessage, value);
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return false;
            }

            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
