namespace AiAgentCanvas.Capabilities.Scheduling;

public interface IScheduledTaskStore : IDisposable
{
    void SaveTask(ScheduledTaskRecord task);
    List<ScheduledTaskRecord> ListTasks();
    bool RemoveTask(string id);
    void SaveResult(string taskId, string description, string result);
    List<ScheduledTaskResult> GetResults(int limit = 10);
}
