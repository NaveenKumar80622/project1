using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Controllers.Public
{
    [ApiController]
    [Route("api/auth/passkey")]
    public class PasskeyAuthController : ControllerBase
    {
        private readonly IPasskeyService _passkeyService;

        public PasskeyAuthController(IPasskeyService passkeyService)
        {
            _passkeyService = passkeyService;
        }

        private int? GetCurrentUserId()
        {
            var rawId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? User.FindFirst("sub")?.Value
                        ?? User.FindFirst("userId")?.Value;

            if (int.TryParse(rawId, out int id))
                return id;

            return null;
        }

        [HttpPost("register-options")]
        [Authorize]
        public async Task<IActionResult> RegisterOptions([FromBody] PasskeyRegisterOptionsRequest? request)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized(new { message = "User not identified." });

            try
            {
                var options = await _passkeyService.GetRegisterOptionsAsync(userId.Value, request?.DeviceName);
                return Ok(options);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("register-complete")]
        [Authorize]
        public async Task<IActionResult> RegisterComplete([FromBody] PasskeyRegisterCompleteRequest request)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized(new { message = "User not identified." });

            try
            {
                var passkey = await _passkeyService.CompleteRegistrationAsync(userId.Value, request);
                return Ok(new
                {
                    success = true,
                    message = "Passkey enrolled successfully.",
                    passkey
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("login-options")]
        [AllowAnonymous]
        public async Task<IActionResult> LoginOptions([FromBody] PasskeyLoginOptionsRequest? request)
        {
            try
            {
                var response = await _passkeyService.GetLoginOptionsAsync(request?.Email);
                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("login-complete")]
        [AllowAnonymous]
        public async Task<IActionResult> LoginComplete([FromBody] PasskeyLoginCompleteRequest request)
        {
            try
            {
                var guestId = Request.Headers["X-Guest-Id"].ToString();
                var (token, userId, role, userData) = await _passkeyService.CompleteLoginAsync(request, guestId);

                return Ok(new
                {
                    success = true,
                    token,
                    userId,
                    role,
                    user = userData
                });
            }
            catch (Exception ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> ListPasskeys()
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized(new { message = "User not identified." });

            var passkeys = await _passkeyService.GetUserPasskeysAsync(userId.Value);
            return Ok(passkeys);
        }

        [HttpDelete("{id:int}")]
        [Authorize]
        public async Task<IActionResult> DeletePasskey(int id)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized(new { message = "User not identified." });

            bool deleted = await _passkeyService.DeletePasskeyAsync(userId.Value, id);
            if (!deleted)
                return NotFound(new { message = "Passkey not found or unauthorized." });

            return Ok(new { success = true, message = "Passkey deleted." });
        }

        [HttpPut("{id:int}")]
        [Authorize]
        public async Task<IActionResult> RenamePasskey(int id, [FromBody] PasskeyRenameRequest request)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized(new { message = "User not identified." });

            if (string.IsNullOrWhiteSpace(request?.DeviceName))
                return BadRequest(new { message = "Device name is required." });

            bool updated = await _passkeyService.RenamePasskeyAsync(userId.Value, id, request.DeviceName);
            if (!updated)
                return NotFound(new { message = "Passkey not found or unauthorized." });

            return Ok(new { success = true, message = "Passkey renamed." });
        }
    }
}
