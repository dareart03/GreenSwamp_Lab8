using GreenSwamp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GreenSwamp.Pages
{
    public class ContactModel : PageModel
    {
        private readonly IContactService _contactService;

        [BindProperty]
        public ContactFormModel Contact { get; set; } = new();

        public bool ShowSuccess { get; set; } = false;

        public ContactModel(IContactService contactService)
        {
            _contactService = contactService;
        }

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            await _contactService.SaveContactAsync(Contact);

            ShowSuccess = true;
            Contact = new ContactFormModel();

            return Page();
        }
    }
}
