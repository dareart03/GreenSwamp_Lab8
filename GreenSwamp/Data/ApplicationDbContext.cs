using GreenSwamp.Models;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace GreenSwamp.Data
{
    // Основной контекст базы данных — через него идут все запросы к SQLite
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // DbSet — это "таблицы", к которым можно делать запросы через LINQ
        public DbSet<User> Users { get; set; }
        public DbSet<Post> Posts { get; set; }
        public DbSet<Tag> Tags { get; set; }
        public DbSet<PostTag> PostTags { get; set; }
        public DbSet<Interaction> Interactions { get; set; }
        public DbSet<Event> Events { get; set; }
        public DbSet<Auth> Auths { get; set; }
        public DbSet<TrendingPond> TrendingPonds { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // PostTag имеет составной ключ (post_id + tag_id)
            modelBuilder.Entity<PostTag>()
                .HasKey(pt => new { pt.PostId, pt.TagId });

            // Уникальный индекс: один пользователь не может дважды поставить один тип взаимодействия
            modelBuilder.Entity<Interaction>()
                .HasIndex(i => new { i.UserId, i.PostId, i.InteractionType })
                .IsUnique();

            // Post ↔ Event: один к одному
            modelBuilder.Entity<Event>()
                .HasOne(e => e.Post)
                .WithOne(p => p.Event)
                .HasForeignKey<Event>(e => e.PostId);

            // Post ↔ Replies: само-ссылающаяся связь
            modelBuilder.Entity<Post>()
                .HasOne(p => p.ParentPost)
                .WithMany(p => p.Replies)
                .HasForeignKey(p => p.ParentPostId)
                .OnDelete(DeleteBehavior.Restrict);

            // TrendingPond — это SQL VIEW, а не таблица, EF не управляет ею
            modelBuilder.Entity<TrendingPond>(eb =>
            {
                eb.HasNoKey();
                eb.ToView("trending_ponds");
            });
        }
    }

    // Вспомогательный класс для представления trending_ponds (SQL VIEW)
    [Keyless]
    public class TrendingPond
    {
        [Column("tag_id")]
        public int TagId { get; set; }

        [Column("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [Column("recent_posts")]
        public int RecentPosts { get; set; }
    }
}
