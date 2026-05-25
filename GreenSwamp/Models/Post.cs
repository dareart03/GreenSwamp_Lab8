using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GreenSwamp.Models
{
    // Таблица постов (ribbits)
    [Table("posts")]
    public class Post
    {
        [Key]
        [Column("post_id")]
        public int PostId { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        // Текст поста
        [Column("content")]
        public string Content { get; set; } = string.Empty;

        // Тип: text / image / video
        [Column("post_type")]
        public string PostType { get; set; } = "text";

        // URL медиафайла (картинки или видео)
        [Column("media_url")]
        public string? MediaUrl { get; set; }

        [Column("media_type")]
        public string? MediaType { get; set; }

        [Column("alt_text")]
        public string? AltText { get; set; }

        [Column("thumbnail_url")]
        public string? ThumbnailUrl { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Если это ответ на другой пост — здесь будет id родителя
        [Column("parent_post_id")]
        public int? ParentPostId { get; set; }

        // Навигационные свойства
        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;

        [ForeignKey("ParentPostId")]
        public virtual Post? ParentPost { get; set; }

        public virtual ICollection<Post> Replies { get; set; } = new List<Post>();
        public virtual ICollection<Interaction> Interactions { get; set; } = new List<Interaction>();
        public virtual ICollection<PostTag> PostTags { get; set; } = new List<PostTag>();
        public virtual Event? Event { get; set; }
    }
}
