using GreenSwamp.Pages;

namespace GreenSwamp.Services
{
    public interface IContactService
    {
        Task SaveContactAsync(ContactFormModel model);
    }
}
