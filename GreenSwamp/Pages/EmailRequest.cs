using System.ComponentModel.DataAnnotations;

namespace GreenSwamp.Pages
{
    public class EmailRequest
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address")]
        public string Email { get; set; } = string.Empty;
    }
}
