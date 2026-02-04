using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DirectoryAnalyzer.Services
{
    public sealed class StatusService : INotifyPropertyChanged
    {
        private static readonly StatusService InstanceInternal = new StatusService();
        private string _statusMessage = "Pronto";

        private StatusService()
        {
        }

        public static StatusService Instance => InstanceInternal;

        public event PropertyChangedEventHandler PropertyChanged;

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage == value)
                {
                    return;
                }

                _statusMessage = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
            }
        }

        public void SetStatus(string statusMessage)
        {
            StatusMessage = statusMessage;
        }
    }
}
