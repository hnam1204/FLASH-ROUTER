namespace FlashAI.Modules.Activity.Contracts;

public interface IActivityEventStream
{
    IDisposable Subscribe(Func<ActivityEventVm, Task> handler);

    void Publish(ActivityEventVm activityEvent);
}
