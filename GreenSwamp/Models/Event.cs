using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GreenSwamp.Models
{
    // Таблица событий — привязаны к постам
    [Table("events")]
    public class Event
    {
        [Key]
        [Column("event_id")]
        public int EventId { get; set; }

        [Column("post_id")]
        public int PostId { get; set; }

        [Column("event_time")]
        public DateTime EventTime { get; set; }

        [Column("location")]
        public string Location { get; set; } = string.Empty;

        [Column("host_org")]
        public string? HostOrg { get; set; }

        [Column("rsvp_count")]
        public int RsvpCount { get; set; }

        [Column("max_capacity")]
        public int? MaxCapacity { get; set; }

        // Связь с постом (один к одному)
        [ForeignKey("PostId")]
        public virtual Post Post { get; set; } = null!;
    }
}
