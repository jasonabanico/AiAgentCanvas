namespace AiAgentCanvas.Capabilities.Scheduling;

public interface IScheduledTaskStore : IDisposable
{
    void SaveTask(ScheduledTaskRecord task);
    List<ScheduledTaskRecord> ListTasks();
    bool RemoveTask(string id);
    void SaveResult(string taskId, string description, string result);
    List<ScheduledTaskResult> GetResults(int limit = 10);

    /// <summary>
    /// Records that the runner executed the task, with the error message when it
    /// failed. Persisting this is what stops a restart from re-running everything.
    /// </summary>
    void MarkRun(string id, DateTimeOffset runAt, string? error = null);
}
