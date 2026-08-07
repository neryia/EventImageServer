using EventImageServer.Contexts;
using EventImageServer.Models;
using EventImageServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Twilio.Security;

namespace EventImageServer.Controllers
{
    // Public webhooks called by Twilio: message status callbacks and inbound
    // messages (used to detect STOP/START opt-out/opt-in replies).
    [Route("[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class TwilioController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly TwilioOptions _options;

        public TwilioController(AppDbContext dbContext, IOptions<TwilioOptions> options)
        {
            _dbContext = dbContext;
            _options = options.Value;
        }

        // Validates that this request genuinely came from Twilio using the
        // X-Twilio-Signature header and the shared auth token.
        private bool IsValidTwilioRequest()
        {
            if (string.IsNullOrWhiteSpace(_options.AuthToken))
            {
                // No auth token configured (e.g. local dev) — skip validation.
                return true;
            }

            if (!Request.Headers.TryGetValue("X-Twilio-Signature", out var signature))
            {
                return false;
            }

            var validator = new RequestValidator(_options.AuthToken);
            var url = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";

            var parameters = Request.Form.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

            return validator.Validate(url, parameters, signature.ToString());
        }

        // Twilio message status callback: updates the MessageLog row matching the
        // MessageSid with the latest delivery status/error code.
        [HttpPost("status")]
        public async Task<IActionResult> Status()
        {
            if (!IsValidTwilioRequest())
            {
                return Unauthorized();
            }

            var sid = Request.Form["MessageSid"].ToString();
            var status = Request.Form["MessageStatus"].ToString();
            var errorCode = Request.Form["ErrorCode"].ToString();

            if (string.IsNullOrEmpty(sid))
            {
                return BadRequest();
            }

            var log = _dbContext.MessageLogs.FirstOrDefault(m => m.TwilioSid == sid);
            if (log != null)
            {
                log.Status = string.IsNullOrEmpty(status) ? log.Status : status;
                log.ErrorCode = string.IsNullOrEmpty(errorCode) ? log.ErrorCode : errorCode;
                log.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }

            return Ok();
        }

        // Inbound SMS/WhatsApp webhook: STOP/UNSUBSCRIBE opts the sending phone
        // number out of future messages; START/UNSTOP opts it back in.
        [HttpPost("inbound")]
        public async Task<IActionResult> Inbound()
        {
            if (!IsValidTwilioRequest())
            {
                return Unauthorized();
            }

            var from = Request.Form["From"].ToString();
            var body = (Request.Form["Body"].ToString() ?? string.Empty).Trim().ToUpperInvariant();

            if (string.IsNullOrEmpty(from))
            {
                return BadRequest();
            }

            var normalizedFrom = from.Replace("whatsapp:", string.Empty);

            var optOutKeywords = new[] { "STOP", "STOPALL", "UNSUBSCRIBE", "CANCEL", "END", "QUIT" };
            var optInKeywords = new[] { "START", "UNSTOP", "YES" };

            var guests = _dbContext.Guests.Where(g => g.Phone == normalizedFrom).ToList();

            if (optOutKeywords.Contains(body))
            {
                foreach (var guest in guests)
                {
                    guest.OptedOut = true;
                }
                await _dbContext.SaveChangesAsync();
            }
            else if (optInKeywords.Contains(body))
            {
                foreach (var guest in guests)
                {
                    guest.OptedOut = false;
                }
                await _dbContext.SaveChangesAsync();
            }

            return Ok();
        }
    }
}
