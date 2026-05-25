using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using GreenSwamp.Data;
using Microsoft.EntityFrameworkCore;

namespace GreenSwamp.Hubs
{
    // ChatHub — концентратор SignalR для группового чата.
    // SignalR держит постоянное WebSocket-соединение между клиентом и сервером.
    // Hub — это класс, методы которого клиент может вызывать напрямую из JS.
    //
    // Группы
    //   Каждый чат — это именованная группа SignalR.
    //   При переключении чата клиент покидает старую группу и вступает в новую.
    //   Сообщения отправляются только в текущую группу пользователя.
    //
    // Авторизация: [Authorize] — только залогиненные пользователи могут подключиться.
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ApplicationDbContext _db;

        // Статический словарь: ConnectionId => (Username, DisplayName, AvatarUrl, CurrentRoom)
        // Храним в памяти — по условию задания сохранять историю не нужно.
        private static readonly Dictionary<string, ConnectedUser> _connectedUsers = new();
        private static readonly object _lock = new();

        // Предопределённые комнаты (отображаются в левой панели)
        public static readonly List<ChatRoom> Rooms = new()
        {
            new("general",  "🌿 General",    "Общий чат для всех обитателей болота"),
            new("campus",   "🏫 Campus",     "Новости и события кампуса"),
            new("study",    "📚 Study Hall", "Вопросы по учёбе и домашним заданиям"),
            new("offtopic", "🐸 Off-Topic",  "Всё остальное"),
        };

        public ChatHub(ApplicationDbContext db)
        {
            _db = db;
        }

        // Вызывается автоматически при подключении клиента
        public override async Task OnConnectedAsync()
        {
            // Получаем данные авторизованного пользователя из Cookie-Claims
            var username    = Context.User?.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
            var displayName = Context.User?.FindFirstValue("display_name")  ?? username;

            // Загружаем аватарку из БД; если не задана — генерируем через DiceBear
            var dbAvatar = await _db.Users
                .Where(u => u.Username == username)
                .Select(u => u.AvatarUrl)
                .FirstOrDefaultAsync();

            var avatarUrl = !string.IsNullOrEmpty(dbAvatar)
                ? dbAvatar
                : $"https://api.dicebear.com/9.x/adventurer/svg?seed={Uri.EscapeDataString(username)}&backgroundColor=b6e3f4,c0aede,d1d4f9,ffd5dc,ffdfbf";

            // Регистрируем соединение
            lock (_lock)
            {
                _connectedUsers[Context.ConnectionId] = new ConnectedUser(
                    username, displayName, avatarUrl, Rooms[0].Id
                );
            }

            // Автоматически вступаем в первую комнату (general)
            await Groups.AddToGroupAsync(Context.ConnectionId, Rooms[0].Id);

            // Сообщаем всем в general что пришёл новый пользователь
            await Clients.Group(Rooms[0].Id).SendAsync("UserJoined", new
            {
                username,
                displayName,
                avatarUrl,
                room    = Rooms[0].Id,
                message = $"{displayName} присоединился к чату 🐸"
            });

            // Отправляем новому пользователю список комнат и онлайн-пользователей
            await Clients.Caller.SendAsync("Init", new
            {
                rooms = Rooms,
                currentRoom = Rooms[0].Id,
                onlineUsers = GetOnlineUsers(),
                yourUsername = username
            });

            // ✅ ИСПРАВЛЕНИЕ: рассылаем обновлённый список онлайн всем остальным подключённым
            await Clients.Others.SendAsync("OnlineUsers", GetOnlineUsers());

            await base.OnConnectedAsync();
        }

        // Вызывается автоматически при отключении клиента
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            ConnectedUser? user;
            lock (_lock)
            {
                _connectedUsers.TryGetValue(Context.ConnectionId, out user);
                _connectedUsers.Remove(Context.ConnectionId);
            }

            if (user != null)
            {
                // Уведомляем комнату об уходе пользователя
                await Clients.Group(user.CurrentRoom).SendAsync("UserLeft", new
                {
                    username    = user.Username,
                    displayName = user.DisplayName,
                    message     = $"{user.DisplayName} покинул чат"
                });
            }

            // Обновляем список онлайн у всех
            await Clients.All.SendAsync("OnlineUsers", GetOnlineUsers());

            await base.OnDisconnectedAsync(exception);
        }

        // Клиент вызывает этот метод чтобы отправить сообщение в текущую комнату.
        // Clients.Group(room) — разошлём только участникам этой группы.
        public async Task SendMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            ConnectedUser? user;
            lock (_lock) { _connectedUsers.TryGetValue(Context.ConnectionId, out user); }

            if (user == null) return;

            var time = DateTime.Now.ToString("HH:mm");

            await Clients.Group(user.CurrentRoom).SendAsync("ReceiveMessage", new
            {
                username    = user.Username,
                displayName = user.DisplayName,
                avatarUrl   = user.AvatarUrl,
                message     = message.Trim(),
                time,
                room        = user.CurrentRoom
            });
        }

        // Клиент вызывает этот метод чтобы переключиться в другую комнату.
        // Механизм Групп SignalR: покидаем старую группу, вступаем в новую.
        public async Task JoinRoom(string roomId)
        {
            // Проверяем что комната существует
            if (!Rooms.Any(r => r.Id == roomId)) return;

            ConnectedUser? user;
            lock (_lock) { _connectedUsers.TryGetValue(Context.ConnectionId, out user); }

            if (user == null || user.CurrentRoom == roomId) return;

            var oldRoom = user.CurrentRoom;

            // Покидаем старую группу
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, oldRoom);

            // Обновляем текущую комнату в словаре
            lock (_lock)
            {
                _connectedUsers[Context.ConnectionId] = user with { CurrentRoom = roomId };
            }

            // Вступаем в новую группу
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

            // Уведомляем старую комнату
            await Clients.Group(oldRoom).SendAsync("UserLeft", new
            {
                username    = user.Username,
                displayName = user.DisplayName,
                message     = $"{user.DisplayName} перешёл в другой чат"
            });

            // Уведомляем новую комнату
            await Clients.Group(roomId).SendAsync("UserJoined", new
            {
                username    = user.Username,
                displayName = user.DisplayName,
                avatarUrl   = user.AvatarUrl,
                room        = roomId,
                message     = $"{user.DisplayName} присоединился 🐸"
            });

            // Говорим клиенту что переключение прошло успешно
            await Clients.Caller.SendAsync("RoomSwitched", roomId);
        }

        // Уведомление о наборе текста — рассылается всем в комнате кроме отправителя
        public async Task Typing()
        {
            ConnectedUser? user;
            lock (_lock) { _connectedUsers.TryGetValue(Context.ConnectionId, out user); }

            if (user == null) return;

            await Clients.OthersInGroup(user.CurrentRoom).SendAsync("UserTyping", user.DisplayName);
        }

        // Вспомогательный метод: список онлайн-пользователей (уникальные username)
        private static List<object> GetOnlineUsers()
        {
            lock (_lock)
            {
                return _connectedUsers.Values
                    .GroupBy(u => u.Username)
                    .Select(g => g.First())
                    .Select(u => (object)new { u.Username, u.DisplayName, u.AvatarUrl, u.CurrentRoom })
                    .ToList();
            }
        }
    }

    // Вспомогательные записи (records — неизменяемые классы данных)
    public record ConnectedUser(string Username, string DisplayName, string AvatarUrl, string CurrentRoom);
    public record ChatRoom(string Id, string Name, string Description);
}
