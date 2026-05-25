using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GreenSwamp.Hubs;

namespace GreenSwamp.Controllers
{
    // Контроллер для страницы чатов /messages
    // Требует авторизации — незалогиненный пользователь будет перенаправлен на /account/login
    [Authorize]
    [Route("messages")]
    public class MessagesController : Controller
    {
        // GET /messages — страница чата
        [HttpGet]
        public IActionResult Index()
        {
            // Передаём список комнат во View (они определены статически в ChatHub)
            ViewBag.Rooms = ChatHub.Rooms;
            return View();
        }
    }
}
