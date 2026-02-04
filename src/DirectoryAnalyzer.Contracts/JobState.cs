namespace DirectoryAnalyzer.Contracts
{
    public enum JobState
    {
        Pending = 0,
        Dispatched = 1,
        Running = 2,
        Completed = 3,
        Failed = 4,
        Canceled = 5
    }
}
