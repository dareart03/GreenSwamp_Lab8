using GreenSwamp.Pages;
using GreenSwamp.Services;
using Microsoft.AspNetCore.Mvc;

namespace GreenSwamp.Controllers
{
    [Route("subscribe")]
    public class SubscribeController : Controller
    {
        private readonly ISubscribeService _subscribeService;
        private readonly IEmailService _emailService;

        public SubscribeController(ISubscribeService subscribeService, IEmailService emailService)
        {
            _subscribeService = subscribeService;
            _emailService = emailService;
        }

        [HttpPost]
        public async Task<IActionResult> Subscribe([FromForm] EmailRequest data)
        {
            string referer = Request.Headers["Referer"].ToString();
            if (string.IsNullOrEmpty(referer)) referer = "/";

            if (!ModelState.IsValid)
            {
                TempData["SubscribeError"] = "Please enter a valid email address";
                return Redirect(referer);
            }

            // Save subscriber to CSV
            await _subscribeService.SaveSubscriberAsync(data.Email);

            // Send confirmation email
            string welcomeBody = $@"
                <h2>Welcome to the Swamp! 🐸</h2>
                <p>Thank you for subscribing with <strong>{data.Email}</strong>.</p>
                <p>You'll now receive the freshest ribbits straight to your inbox.</p>
                <p>See you in the swamp!</p>";

            await _emailService.SendEmailAsync(
                data.Email,
                "Welcome to Greenswamp!",
                welcomeBody);

            TempData["SubscribeSuccess"] = "Successfully subscribed! Check your email 🐸";
            return Redirect(referer);
        }
    }
}
