using System.Text;
using GreenSwamp.Api;
using GreenSwamp.Data;
using GreenSwamp.Models;
using GreenSwamp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

// ── Сервисы ───────────────────────────────────────────────────────────────────

builder.Services.AddRazorPages();
builder.Services.AddControllersWithViews();

// ── БД (Лаб 5) ────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Двойная аутентификация: Cookie (для сайта) + JWT (для API) ────────────────
// Используем две схемы одновременно. Контроллеры явно указывают какую использовать:
//   [Authorize]                           → Cookie (для MVC-страниц)
//   [Authorize(AuthenticationSchemes = "JwtBearer")]  → JWT (для REST API)
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey   = jwtSettings["SecretKey"]!;

builder.Services.AddAuthentication(options =>
{
    // Схема по умолчанию — Cookie (для сайта)
    options.DefaultScheme          = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath         = "/account/login";
    options.LogoutPath        = "/account/logout";
    options.AccessDeniedPath  = "/account/login";
    options.ExpireTimeSpan    = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
})
.AddJwtBearer("JwtBearer", options =>
{
    // Параметры валидации JWT-токена
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer           = true,
        ValidateAudience         = true,
        ValidateLifetime         = true,          // проверять срок действия
        ValidateIssuerSigningKey = true,
        ValidIssuer              = jwtSettings["Issuer"],
        ValidAudience            = jwtSettings["Audience"],
        IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew                = TimeSpan.Zero  // без запаса по времени
    };
    // Для API возвращать 401 JSON, а не редирект на страницу логина
    options.Events = new JwtBearerEvents
    {
        OnChallenge = ctx =>
        {
            ctx.HandleResponse();
            ctx.Response.StatusCode  = 401;
            ctx.Response.ContentType = "application/json";
            return ctx.Response.WriteAsync("{\"error\":\"Требуется авторизация. Передай JWT-токен в заголовке Authorization: Bearer <token>\"}");
        }
    };
});

builder.Services.AddHttpContextAccessor();

// ── SignalR (Лаб 8) ──────────────────────────────────────────────────────────
// SignalR встроен в ASP.NET 8, пакет не нужен
builder.Services.AddSignalR();
builder.Services.AddScoped<IJwtService, JwtService>();

// ── Email-сервисы (Лаб 4) ─────────────────────────────────────────────────────
builder.Services.Configure<MailSettings>(builder.Configuration.GetSection("MailSettings"));
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ISubscribeService, SubscribeService>();
builder.Services.AddScoped<IContactService, ContactService>();

// ── Swagger / OpenAPI (Лаб 7) ─────────────────────────────────────────────────
// Swagger генерирует интерактивную документацию по аннотациям на контроллерах.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "GreenSwamp REST API",
        Version     = "v1",
        Description = "REST API для социальной сети GreenSwamp. " +
                      "Для методов с 🔒 нужен JWT-токен: сначала POST /api/v1/auth/login, " +
                      "затем нажми Authorize и вставь токен."
    });

    // Добавляем JWT-аутентификацию в Swagger UI — кнопка Authorize
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "Bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Вставь JWT-токен, полученный из POST /api/v1/auth/login"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });

    // Включаем XML-комментарии (/// summary над методами) в Swagger
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath)) c.IncludeXmlComments(xmlPath);
});

// ── Сборка приложения ─────────────────────────────────────────────────────────
var app = builder.Build();

// Инициализация БД и seed-данных
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();

    db.Database.ExecuteSqlRaw(@"
        CREATE VIEW IF NOT EXISTS trending_ponds AS
        SELECT t.tag_id, t.tag_name, COUNT(pt.post_id) AS recent_posts
        FROM tags t
        JOIN post_tags pt ON t.tag_id = pt.tag_id
        JOIN posts p ON pt.post_id = p.post_id
        WHERE p.created_at > datetime('now', '-1 day')
        GROUP BY t.tag_id
        ORDER BY recent_posts DESC
        LIMIT 10
    ");

    if (!db.Users.Any())
    {
        // ── Пользователи (20 штук) ────────────────────────────────────────────
        var users = new List<User>
        {
            new() { Username = "lily_pad",        DisplayName = "Lily Pad",         Bio = "Just a frog in a pond 🐸",            AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-60), IsActive = true },
            new() { Username = "swamp_wizard",    DisplayName = "Swamp Wizard",      Bio = "Повелитель болот",             AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-55), IsActive = true },
            new() { Username = "ribbit_reporter", DisplayName = "Ribbit Reporter",   Bio = "Breaking news from the bog 📰",        AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-50), IsActive = true },
            new() { Username = "muddy_boots",     DisplayName = "Muddy Boots",       Bio = "Люблю походы и лягушек",               AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-45), IsActive = true },
            new() { Username = "green_leaf",      DisplayName = "Green Leaf",        Bio = "Ботаник. Большой поклонник лягушек.",           AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-42), IsActive = true },
            new() { Username = "pond_hopper",     DisplayName = "Pond Hopper",       Bio = "Always jumping to the next thing 🌊",  AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-38), IsActive = true },
            new() { Username = "toadstool",       DisplayName = "Toadstool",         Bio = "Грибы и лягушки, в основном грибы",        AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-35), IsActive = true },
            new() { Username = "cricket_song",    DisplayName = "Cricket Song",      Bio = "Night sounds and campus vibes 🎵",     AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-30), IsActive = true },
            new() { Username = "algae_bloom",     DisplayName = "Algae Bloom",       Bio = "Фотосинтез — это моё всё",           AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-28), IsActive = true },
            new() { Username = "dragonfly99",     DisplayName = "Dragonfly 99",      Bio = "Быстрый. Переливающийся. Голодный.",            AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-25), IsActive = true },
            new() { Username = "marsh_melody",    DisplayName = "Marsh Melody",      Bio = "Music student, swamp lover 🎸",        AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-22), IsActive = true },
            new() { Username = "frogsworth",      DisplayName = "Prof. Frogsworth",  Bio = "Преподаватель информатики. Да, лягушка.",            AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-20), IsActive = true },
            new() { Username = "dew_drop",        DisplayName = "Dew Drop",          Bio = "Morning person. Tea not coffee. ☕",   AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-18), IsActive = true },
            new() { Username = "bullfrog42",      DisplayName = "Bullfrog 42",       Bio = "Ответ — 42. Всегда.",            AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-15), IsActive = true },
            new() { Username = "cattail_kate",    DisplayName = "Cattail Kate",      Bio = "Art major, wetland advocate 🎨",       AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-12), IsActive = true },
            new() { Username = "swamp_fox",       DisplayName = "Swamp Fox",         Bio = "Running club captain 🦊",              AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-10), IsActive = true },
            new() { Username = "lilybell",        DisplayName = "Lilybell",          Bio = "Garden club president 🌸",             AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-8),  IsActive = true },
            new() { Username = "reedy_redd",      DisplayName = "Reedy Redd",        Bio = "Саксофон и болотные песни",            AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-6),  IsActive = true },
            new() { Username = "spore_storm",     DisplayName = "Spore Storm",       Bio = "Biology PhD student 🧬",               AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-4),  IsActive = true },
            new() { Username = "mossy_rock",      DisplayName = "Mossy Rock",        Bio = "Геология + экология, двойной диплом",       AvatarUrl = "/images/default-avatar.svg", CreatedAt = DateTime.UtcNow.AddDays(-2),  IsActive = true },
        };
        db.Users.AddRange(users);
        db.SaveChanges();

        // Пароль "password123" для всех seed-пользователей.
        // Алгоритм совпадает с AccountController: SHA256(password + "greenswamp_salt") → Base64
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes("password123" + "greenswamp_salt")));
        db.Auths.AddRange(users.Select(u => new Auth { UserId = u.UserId, PasswordHash = hash }));
        db.SaveChanges();

        // ── Теги ─────────────────────────────────────────────────────────────
        var tags = new List<Tag>
        {
            new() { TagName = "campus",     CreatedAt = DateTime.UtcNow },
            new() { TagName = "events",     CreatedAt = DateTime.UtcNow },
            new() { TagName = "swamplife",  CreatedAt = DateTime.UtcNow },
            new() { TagName = "study",      CreatedAt = DateTime.UtcNow },
            new() { TagName = "music",      CreatedAt = DateTime.UtcNow },
            new() { TagName = "food",       CreatedAt = DateTime.UtcNow },
            new() { TagName = "nature",     CreatedAt = DateTime.UtcNow },
            new() { TagName = "sports",     CreatedAt = DateTime.UtcNow },
            new() { TagName = "art",        CreatedAt = DateTime.UtcNow },
            new() { TagName = "science",    CreatedAt = DateTime.UtcNow },
        };
        db.Tags.AddRange(tags);
        db.SaveChanges();

        // ── Посты (50 штук) ───────────────────────────────────────────────────
        var rnd   = new Random(42);
        var now   = DateTime.UtcNow;
        var posts = new List<Post>
        {
            new() { UserId = users[0].UserId,  Content = "Welcome to Greenswamp everyone! 🐸 This is the place to be. #swamplife #campus",               PostType = "text", CreatedAt = now.AddDays(-59) },
            new() { UserId = users[1].UserId,  Content = "Лайфхак: вид на пруд из библиотеки — лучшее место для учёбы на кампусе. #study #campus",               PostType = "text", CreatedAt = now.AddDays(-58) },
            new() { UserId = users[2].UserId,  Content = "СРОЧНО: В столовой теперь подают салат из листьев кувшинки. Наш корреспондент уже там. #food #campus", PostType = "text", CreatedAt = now.AddDays(-57) },
            new() { UserId = users[3].UserId,  Content = "Утренняя прогулка по болотам сегодня была невероятной. #nature #swamplife",                      PostType = "text", CreatedAt = now.AddDays(-56) },
            new() { UserId = users[4].UserId,  Content = "Знаете ли вы, что на северной стене корпуса естественных наук растёт 14 видов мха? #science #nature", PostType = "text", CreatedAt = now.AddDays(-55) },
            new() { UserId = users[5].UserId,  Content = "Побывала в трёх учебных группах и не усвоила ничего. Классика. #study",            PostType = "text", CreatedAt = now.AddDays(-54) },
            new() { UserId = users[6].UserId,  Content = "Грибы у восточных ворот вернулись!! Не ешьте их. Наверное. #nature #campus",           PostType = "text", CreatedAt = now.AddDays(-53) },
            new() { UserId = users[7].UserId,  Content = "Open mic night at the swamp hall this Friday! Come perform 🎵 #music #events",                   PostType = "text", CreatedAt = now.AddDays(-52) },
            new() { UserId = users[8].UserId,  Content = "Напоминание: сезон водорослей наступил. Берегите свой пруд. #nature #swamplife",       PostType = "text", CreatedAt = now.AddDays(-51) },
            new() { UserId = users[9].UserId,  Content = "Рекорд скорости стрекозы: 54 км/ч над двором сегодня. Не благодарите. #science #sports",  PostType = "text", CreatedAt = now.AddDays(-50) },
            new() { UserId = users[10].UserId, Content = "Jazz quartet rehearsing in the marsh pavilion every Tuesday 7pm. All welcome 🎸 #music #campus",  PostType = "text", CreatedAt = now.AddDays(-49) },
            new() { UserId = users[11].UserId, Content = "Часы приёма перенесены на скамейку у болота. Приносите домашнее задание по алгоритмам. #study #campus",        PostType = "text", CreatedAt = now.AddDays(-48) },
            new() { UserId = users[12].UserId, Content = "First coffee of the morning hits different when the frogs are singing. ☕ #swamplife",            PostType = "text", CreatedAt = now.AddDays(-47) },
            new() { UserId = users[13].UserId, Content = "42 дня до сессии. Это я так, не считаю. #study",                                            PostType = "text", CreatedAt = now.AddDays(-46) },
            new() { UserId = users[14].UserId, Content = "New mural going up on the biology building this weekend. Come watch! 🎨 #art #events #campus",    PostType = "text", CreatedAt = now.AddDays(-45) },
            new() { UserId = users[15].UserId, Content = "Пробежка в 5 утра по болотному маршруту — очень рекомендую. Видела цаплю. #sports #nature",           PostType = "text", CreatedAt = now.AddDays(-44) },
            new() { UserId = users[16].UserId, Content = "Garden club just planted 200 native wildflowers outside the dorms 🌸 #campus #nature",            PostType = "text", CreatedAt = now.AddDays(-43) },
            new() { UserId = users[17].UserId, Content = "Сыграла болотный блюз сегодня вечером во дворе. Немного народу, но атмосфера отличная. #music #swamplife",      PostType = "text", CreatedAt = now.AddDays(-42) },
            new() { UserId = users[18].UserId, Content = "Третья глава диссертации сдана. Награждаю себя прогулкой у пруда. #science #study",                  PostType = "text", CreatedAt = now.AddDays(-41) },
            new() { UserId = users[19].UserId, Content = "Нашла жеоду у старого ручья. Геология кампуса недооценена. #science #nature",                PostType = "text", CreatedAt = now.AddDays(-40) },
            new() { UserId = users[0].UserId,  Content = "Campus food festival this Saturday! All the local food trucks 🌮 #food #events #campus",          PostType = "text", CreatedAt = now.AddDays(-39) },
            new() { UserId = users[1].UserId,  Content = "Кто хочет основать клуб настольных ролевых игр? Лягушки и подземелья. #campus #events",                   PostType = "text", CreatedAt = now.AddDays(-38) },
            new() { UserId = users[2].UserId,  Content = "Эксклюзив: пруд за химическим корпусом на 40% состоит из кофеина. Мы проверили. #science #food",          PostType = "text", CreatedAt = now.AddDays(-37) },
            new() { UserId = users[3].UserId,  Content = "День ухода за тропами в заповеднике в воскресенье. Перчатки выдаём! #nature #events",     PostType = "text", CreatedAt = now.AddDays(-36) },
            new() { UserId = users[4].UserId,  Content = "Мастер-класс по определению мхов в четверг в полдень, ботаническая лаборатория, каб. 3. #science #campus",                 PostType = "text", CreatedAt = now.AddDays(-35) },
            new() { UserId = users[5].UserId,  Content = "Пыталась учиться. Отвлеклась на уток. 10/10, повторю снова. #study #swamplife",                PostType = "text", CreatedAt = now.AddDays(-34) },
            new() { UserId = users[6].UserId,  Content = "Счётчик поганок у восточных ворот: 47. На прошлой неделе было 39. #nature #science",               PostType = "text", CreatedAt = now.AddDays(-33) },
            new() { UserId = users[7].UserId,  Content = "Swamp Soul Night recap: six performers, one standing ovation, zero dry eyes 🎵 #music #events",   PostType = "text", CreatedAt = now.AddDays(-32) },
            new() { UserId = users[8].UserId,  Content = "Фотосинтез > кофе, переубедите меня #science #swamplife",                                     PostType = "text", CreatedAt = now.AddDays(-31) },
            new() { UserId = users[9].UserId,  Content = "Установил личный рекорд в спринте по двору. Аэродинамика стрекозы рулит. #sports",      PostType = "text", CreatedAt = now.AddDays(-30) },
            new() { UserId = users[10].UserId, Content = "Летний сетлист готов. Восемь своих, два кавера. Дебют на следующей неделе. #music #events",            PostType = "text", CreatedAt = now.AddDays(-29) },
            new() { UserId = users[11].UserId, Content = "Напоминание: гостевая лекция об алгоритмах, вдохновлённых болотом, в пятницу в 15:00, зал Б. #study #science", PostType = "text", CreatedAt = now.AddDays(-28) },
            new() { UserId = users[12].UserId, Content = "Утренний ритуал: чай, дневник, десять минут наблюдения за прудом. Очень терапевтично. #swamplife",    PostType = "text", CreatedAt = now.AddDays(-27) },
            new() { UserId = users[13].UserId, Content = "У каждой задачи ровно 42 решения. Докажите обратное. #study #science",                         PostType = "text", CreatedAt = now.AddDays(-26) },
            new() { UserId = users[14].UserId, Content = "The mural is half done and already stunning. Stop by the bio building! 🎨 #art #campus",          PostType = "text", CreatedAt = now.AddDays(-25) },
            new() { UserId = users[15].UserId, Content = "Клуб бегунов: новый маршрут через северные болота начиная со следующего понедельника. #sports #nature",      PostType = "text", CreatedAt = now.AddDays(-24) },
            new() { UserId = users[16].UserId, Content = "Seed swap happening at the garden this weekend. Bring something, take something! 🌱 #nature #events", PostType = "text", CreatedAt = now.AddDays(-23) },
            new() { UserId = users[17].UserId, Content = "Уличное выступление у главного входа сегодня с 16 до 18. Чаевые приветствуются, аплодисменты принимаются. #music",              PostType = "text", CreatedAt = now.AddDays(-22) },
            new() { UserId = users[18].UserId, Content = "Защитила исследовательский проект! Комиссия спросила про лягушек. Я была готова. #science #study",     PostType = "text", CreatedAt = now.AddDays(-21) },
            new() { UserId = users[19].UserId, Content = "Горные пласты за спортзалом хранят историю на 200 миллионов лет. Обожаю этот кампус. #science",   PostType = "text", CreatedAt = now.AddDays(-20) },
            new() { UserId = users[0].UserId,  Content = "Еженедельное напоминание: потрогайте траву (или мох, или водоросли). Выйдите на улицу. #swamplife #nature",           PostType = "text", CreatedAt = now.AddDays(-19) },
            new() { UserId = users[1].UserId,  Content = "Горячая тема: лягушки — лучшие партнёры по учёбе, чем люди. Никаких непрошеных советов. #study #swamplife",   PostType = "text", CreatedAt = now.AddDays(-18) },
            new() { UserId = users[2].UserId,  Content = "Замечено: профессор Фроксворт проверяет работы на листе кувшинки. Вот это преданность. #campus #swamplife",      PostType = "text", CreatedAt = now.AddDays(-17) },
            new() { UserId = users[3].UserId,  Content = "Ночной поход в заповеднике завтра, фонарики выдаём, лягушки гарантированы. #nature #events",         PostType = "text", CreatedAt = now.AddDays(-16) },
            new() { UserId = users[4].UserId,  Content = "Насчитала 6 видов папоротника на сегодняшней прогулке. Новый личный рекорд. #nature #science",        PostType = "text", CreatedAt = now.AddDays(-15) },
            new() { UserId = users[5].UserId,  Content = "Плейлист для учёбы: 3 часа звуков дождя. Усвоено: 2 факта. Всё равно стоило. #study",               PostType = "text", CreatedAt = now.AddDays(-8)  },
            new() { UserId = users[6].UserId,  Content = "Обновление счётчика грибов: 51. Произошли события. #nature #science",                         PostType = "text", CreatedAt = now.AddDays(-5)  },
            new() { UserId = users[7].UserId,  Content = "Концерт недели сессии: акустические выступления весь день в атриуме, в четверг. #music #events #campus",       PostType = "text", CreatedAt = now.AddDays(-3)  },
            new() { UserId = users[8].UserId,  Content = "Лето почти наступило, и водоросли РАСЦВЕТАЮТ. Любуемся. #swamplife #nature",          PostType = "text", CreatedAt = now.AddDays(-1)  },
            new() { UserId = users[9].UserId,  Content = "Итоговые соревнования стрекоз по скорости во дворе завтра в полдень. Приходите болеть. #sports #events #campus", PostType = "text", CreatedAt = now.AddHours(-6) },
        };
        db.Posts.AddRange(posts);
        db.SaveChanges();

        // ── Привязка тегов к постам ───────────────────────────────────────────
        // Словарь тегов для удобного поиска по имени
        var tagMap = tags.ToDictionary(t => t.TagName);
        var postTagList = new List<PostTag>();

        // Извлекаем хэштеги из текста поста и создаём PostTag-записи
        foreach (var post in posts)
        {
            var words = post.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                if (!word.StartsWith('#')) continue;
                var name = word.TrimStart('#').Trim('.', ',', '!', '?').ToLower();
                if (!tagMap.TryGetValue(name, out var tag)) continue;
                // Проверяем что такой PostTag ещё не добавлен
                if (postTagList.Any(pt => pt.PostId == post.PostId && pt.TagId == tag.TagId)) continue;
                postTagList.Add(new PostTag { PostId = post.PostId, TagId = tag.TagId });
                tag.UsageCount++;
            }
        }
        db.PostTags.AddRange(postTagList);
        db.SaveChanges();

        // ── Взаимодействия (лайки и reribb) ──────────────────────────────────
        var interactions = new List<Interaction>();
        foreach (var post in posts)
        {
            // Каждый пост получает 2–6 случайных лайков от разных пользователей
            var likers = users.Where(u => u.UserId != post.UserId)
                              .OrderBy(_ => rnd.Next())
                              .Take(rnd.Next(2, 7))
                              .ToList();
            foreach (var liker in likers)
                interactions.Add(new Interaction
                {
                    UserId = liker.UserId, PostId = post.PostId,
                    InteractionType = "like", CreatedAt = post.CreatedAt.AddMinutes(rnd.Next(5, 300))
                });
        }
        // Добавляем несколько reribb и comment
        interactions.Add(new Interaction { UserId = users[2].UserId, PostId = posts[0].PostId,  InteractionType = "reribb",  CreatedAt = now.AddDays(-58) });
        interactions.Add(new Interaction { UserId = users[5].UserId, PostId = posts[7].PostId,  InteractionType = "reribb",  CreatedAt = now.AddDays(-51) });
        interactions.Add(new Interaction { UserId = users[0].UserId, PostId = posts[14].PostId, InteractionType = "reribb",  CreatedAt = now.AddDays(-44) });
        interactions.Add(new Interaction { UserId = users[1].UserId, PostId = posts[0].PostId,  InteractionType = "comment", Content = "So excited to be here! 🐸",   CreatedAt = now.AddDays(-59) });
        interactions.Add(new Interaction { UserId = users[3].UserId, PostId = posts[7].PostId,  InteractionType = "comment", Content = "Я буду выступать!",          CreatedAt = now.AddDays(-52) });
        interactions.Add(new Interaction { UserId = users[9].UserId, PostId = posts[29].PostId, InteractionType = "comment", Content = "Аэродинамика рулит",             CreatedAt = now.AddDays(-29) });

        db.Interactions.AddRange(interactions);
        db.SaveChanges();

        // ── Тестовые события (Лаб 5: Events в ленте) ─────────────────────────
        // Создаём посты-события — у каждого есть связанная запись в таблице events.
        // В ленте они отображаются с датой, местом и счётчиком RSVP.
        var eventPost1 = new Post
        {
            UserId    = users[7].UserId,
            Content   = "Фестиваль музыки Swamp — живые выступления весь вечер, не пропустите! #events #campus #music",
            PostType  = "text",
            CreatedAt = DateTime.UtcNow.AddHours(-12)
        };
        var eventPost2 = new Post
        {
            UserId    = users[4].UserId,
            Content   = "Прогулка по болотам в эту субботу утром. Приглашаются все! #events #nature #campus",
            PostType  = "text",
            CreatedAt = DateTime.UtcNow.AddHours(-3)
        };
        db.Posts.AddRange(eventPost1, eventPost2);
        db.SaveChanges();

        // Привязываем теги к постам-событиям
        var tagMap2 = tags.ToDictionary(t => t.TagName);
        db.PostTags.AddRange(
            new PostTag { PostId = eventPost1.PostId, TagId = tagMap2["events"].TagId  },
            new PostTag { PostId = eventPost1.PostId, TagId = tagMap2["campus"].TagId  },
            new PostTag { PostId = eventPost1.PostId, TagId = tagMap2["music"].TagId   },
            new PostTag { PostId = eventPost2.PostId, TagId = tagMap2["events"].TagId  },
            new PostTag { PostId = eventPost2.PostId, TagId = tagMap2["nature"].TagId  },
            new PostTag { PostId = eventPost2.PostId, TagId = tagMap2["campus"].TagId  }
        );
        tagMap2["events"].UsageCount += 2;
        tagMap2["campus"].UsageCount += 2;
        tagMap2["music"].UsageCount++;
        tagMap2["nature"].UsageCount++;

        // Записи в таблице events — дополнительные данные к постам
        db.Events.AddRange(
            new Event
            {
                PostId      = eventPost1.PostId,
                EventTime   = DateTime.UtcNow.AddDays(3).Date.AddHours(19),
                Location    = "Главная сцена Swamp Hall",
                HostOrg     = "Музыкальный клуб Greenswamp",
                RsvpCount   = 47,
                MaxCapacity = 200
            },
            new Event
            {
                PostId      = eventPost2.PostId,
                EventTime   = DateTime.UtcNow.AddDays(5).Date.AddHours(9),
                Location    = "Заповедник Северных болот, Ворота 2",
                HostOrg     = "Ботанический факультет",
                RsvpCount   = 12,
                MaxCapacity = 30
            }
        );
        db.SaveChanges();
    }
}

// ── Middleware ────────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Request: {Method} {Path}", context.Request.Method, context.Request.Path);
    await next();
});

app.UseStatusCodePagesWithReExecute("/404");

// Swagger доступен только в Development (или всегда — для демо)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "GreenSwamp API v1");
    c.RoutePrefix     = "swagger";  // доступен по /swagger
    c.DocumentTitle   = "GreenSwamp REST API";
});

app.UseAuthentication();

// Middleware: проверяем IsActive при каждом запросе страниц.
// Если пользователь удалён через API (IsActive=false), его Cookie инвалидируется
// и он получает редирект на логин — даже если Cookie ещё не истекла.
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true
        && !context.Request.Path.StartsWithSegments("/api")   // API-запросы не трогаем
        && !context.Request.Path.StartsWithSegments("/account/logout"))
    {
        var username = context.User.Identity.Name;
        var db = context.RequestServices.GetRequiredService<GreenSwamp.Data.ApplicationDbContext>();
        var isActive = await db.Users
            .Where(u => u.Username == username)
            .Select(u => (bool?)u.IsActive)
            .FirstOrDefaultAsync();

        if (isActive != true)
        {
            await context.SignOutAsync(
                Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/account/login");
            return;
        }
    }
    await next();
});

app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

// Регистрируем SignalR-хаб по адресу /chats
app.MapHub<GreenSwamp.Hubs.ChatHub>("/chats");

app.Run();
