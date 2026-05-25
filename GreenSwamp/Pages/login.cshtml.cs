using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GreenSwamp.Pages
{
    // Страница логина — форма отправляется POST на /account/login (AccountController)
    public class loginModel : PageModel
    {
        public void OnGet()
        {
            // Передаём ошибку из TempData во ViewData, чтобы показать в шаблоне
            if (TempData["Error"] is string error)
                ViewData["Error"] = error;
        }
    }
}
