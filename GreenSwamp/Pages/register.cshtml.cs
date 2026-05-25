using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GreenSwamp.Pages
{
    // Страница регистрации — форма отправляется POST на /account/register (AccountController)
    public class registerModel : PageModel
    {
        public void OnGet()
        {
            if (TempData["Error"] is string error)
                ViewData["Error"] = error;
        }
    }
}
