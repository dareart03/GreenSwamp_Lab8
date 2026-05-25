using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GreenSwamp.Models
{
    // Таблица пользователей сайта
    [Table("users")]
    public class User
    {
        [Key]
        [Column("user_id")]
        public int UserId { get; set; }

        [Column("username")]
        public string Username { get; set; } = string.Empty;

        [Column("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [Column("avatar_url")]
        public string? AvatarUrl { get; set; }

        [Column("bio")]
        public string? Bio { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        // Навигационные свойства — EF подгрузит связанные данные
        public virtual ICollection<Post> Posts { get; set; } = new List<Post>();
        public virtual ICollection<Interaction> Interactions { get; set; } = new List<Interaction>();
        public virtual Auth? Auth { get; set; }
    }
}
