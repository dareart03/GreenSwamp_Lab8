using System.ComponentModel.DataAnnotations;

namespace GreenSwamp.Pages
{
    public class ContactFormModel
    {
        [Required(ErrorMessage = "Имя обязательно")]
        [Display(Name = "Your Name")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email обязателен")]
        [EmailAddress(ErrorMessage = "Неверный формат email")]
        [RegularExpression(@".*\.edu$", ErrorMessage = "Email должен заканчиваться на .edu (например, student@swamp.edu)")]
        [Display(Name = "Your Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Тема сообщения обязательна")]
        [Display(Name = "Message Topic")]
        public string Subject { get; set; } = string.Empty;

        [Required(ErrorMessage = "Сообщение обязательно")]
        [MinLength(10, ErrorMessage = "Сообщение должно содержать минимум 10 символов")]
        [Display(Name = "Your Message")]
        public string Message { get; set; } = string.Empty;
    }
}
