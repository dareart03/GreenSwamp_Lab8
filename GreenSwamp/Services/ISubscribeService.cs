namespace GreenSwamp.Services
{
    public interface ISubscribeService
    {
        Task SaveSubscriberAsync(string email);
    }
}
