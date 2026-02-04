namespace DirectoryAnalyzer.Contracts
{
    public sealed class ErrorInfo
    {
        public string ContractVersion { get; set; } = "1.0";
        public string Code { get; set; }
        public string Message { get; set; }
        public string Details { get; set; }
        public string ExceptionType { get; set; }
        public string StackHash { get; set; }
    }
}
