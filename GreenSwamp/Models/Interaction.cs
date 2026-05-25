using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GreenSwamp.Models
{
    // Таблица взаимодействий: лайки, реribbs, комментарии, rsvp
    [Table("interactions")]
    public class Interaction
    {
        [Key]
        [Column("interaction_id")]
        public int InteractionId { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("post_id")]
        public int PostId { get; set; }

        // Тип: "like" | "reribb" | "comment" | "rsvp"
        [Column("interaction_type")]
        public string InteractionType { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Текст (только для comment)
        [Column("content")]
        public string? Content { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;

        [ForeignKey("PostId")]
        public virtual Post Post { get; set; } = null!;
    }
}
