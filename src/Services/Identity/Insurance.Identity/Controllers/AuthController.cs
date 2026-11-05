using System.Security.Claims;
using Insurance.Identity.Contracts;
using Insurance.Identity.DTOs;
using Insurance.Identity.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Insurance.Identity.Controllers;

[ApiController]                      // Automatic 400 responses when DTO validation fails
[Route("api/[controller]")]          // /api/auth
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IEmailVerificationService _verification;

    public AuthController(IAuthService auth, IEmailVerificationService verification)
    {
        _auth = auth;
        _verification = verification;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        // Duplicate email throws DuplicateEmailException, 
        // which ApiExceptionHandler turns into a 409.
        var result = await _auth.RegisterAsync(request);
        return CreatedAtAction(nameof(Me), result);
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        AuthResponse? result;
        try
        {
            result = await _auth.LoginAsync(request);
        }
        catch (Exceptions.OneTimePasswordExpiredException ex)
        {
            return Unauthorized(new ProblemDetails { Title = ex.Message, Status = 401 });
        }
        catch (Exceptions.AccountDeactivatedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails { Title = ex.Message, Status = 403 });
        }
        if (result is null)
            return Unauthorized(new ProblemDetails { Title = "Invalid email or password.", Status = 401 });
        return Ok(result);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me()
    {
        var profile = await _auth.GetProfileAsync(CurrentUserId);
        if (profile is null) return NotFound();
        return Ok(profile);
    }

    // ---------------- Email verification ----------------

    // Opened from the link in the email, possibly in a browser where nobody is signed in: no token needed.
    // 400 if the link is invalid, expired or used.
    [HttpPost("verify-email")]
    [ProducesResponseType(typeof(VerifyEmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<VerifyEmailResponse>> VerifyEmail(VerifyEmailRequest request) =>
        Ok(await _verification.VerifyAsync(request.Token));

    // ---------------- Changing the email and the password ----------------

    // Takes effect at once; the new address must then be verified, and the old one is told.
    // 400 invalid or same address, 409 address taken, 429 too many emails.
    [Authorize]
    [HttpPost("change-email")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<UserDto>> ChangeEmail(ChangeEmailRequest request) =>
        Ok(await _verification.ChangeEmailAsync(CurrentUserId, request));

    // 400 if the current password is wrong, the new one breaks the rules, or the confirmation doesn't match.
    [Authorize]
    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        await _auth.ChangePasswordAsync(CurrentUserId, request);
        return NoContent();
    }

    // After an admin reset: the session signed in with the one-time password picks the new password. 400 if the rules
    // aren't met or the one-time password has expired, 403 for a session that didn't sign in with it.
    [Authorize]
    [HttpPost("choose-new-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ChooseNewPassword(ChooseNewPasswordRequest request)
    {
        var withOneTimePassword = User.HasClaim(AuthClaims.SignedInWithOneTimePassword, "true");
        await _auth.ChooseNewPasswordAsync(CurrentUserId, withOneTimePassword, request);
        return NoContent();
    }

    // Sends a fresh link. 409 if already verified, 429 when asked too often.
    [Authorize]
    [HttpPost("resend-verification")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendVerification()
    {
        await _verification.ResendAsync(CurrentUserId);
        return NoContent();
    }
}
