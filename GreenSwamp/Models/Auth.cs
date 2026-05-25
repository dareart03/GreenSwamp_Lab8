using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GreenSwamp.Models
{
    // Таблица аутентификации — хранит хэш пароля и данные сессии
    [Table("auth")]
    public class Auth
    {
        [Key]
        [Column("user_id")]
        public int UserId { get; set; }

        [Column("password_hash")]
        public string PasswordHash { get; set; } = string.Empty;

        [Column("last_login")]
        public DateTime? LastLogin { get; set; }

        [Column("reset_token")]
        public string? ResetToken { get; set; }

        [Column("token_expiry")]
        public DateTime? TokenExpiry { get; set; }

        // Связь один-к-одному с User
        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;
    }
}
