using System;
using DirectoryAnalyzer.Services;

namespace DirectoryAnalyzer.ViewModels
{
    public abstract class BaseAnalyzerViewModel : BaseViewModel
    {
        private string _progressMessage;

        public string ProgressMessage
        {
            get => _progressMessage;
            protected set => SetProperty(ref _progressMessage, value);
        }

        protected void SetStatus(string detailedMessage, string globalStatus)
        {
            StatusMessage = detailedMessage;
            if (!string.IsNullOrWhiteSpace(globalStatus))
            {
                StatusService.Instance.SetStatus(globalStatus);
            }
        }
    }
}
