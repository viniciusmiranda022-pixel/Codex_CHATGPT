using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace DirectoryAnalyzer.ViewModels
{
    public abstract class BaseViewModel : INotifyPropertyChanged
    {
        private bool _isBusy;
        private string _statusMessage;
        private double _progress;

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

        public double Progress
        {
            get => _progress;
            protected set => SetProperty(ref _progress, value);
        }

        public ICommand RunCommand { get; protected set; }
        public ICommand CancelCommand { get; protected set; }
        public ICommand ExportCsvCommand { get; protected set; }
        public ICommand ExportXmlCommand { get; protected set; }
        public ICommand ExportHtmlCommand { get; protected set; }
        public ICommand ExportSqlCommand { get; protected set; }

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
