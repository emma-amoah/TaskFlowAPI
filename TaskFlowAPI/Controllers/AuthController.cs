using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using TaskFlowAPI.Contexts;
using TaskFlowAPI.DTOs;
using TaskFlowAPI.Models;
using TaskFlowAPI.Services;

namespace TaskFlowAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly TokenService _tokenService;
    private readonly AppDbContext _context;
    private readonly TaskFlowAPI.Services.Email.IEmailSender _emailSender;

    public AuthController(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        TokenService tokenService,
        AppDbContext context,
        TaskFlowAPI.Services.Email.IEmailSender emailSender)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _context = context;
        _emailSender = emailSender;
    }

    // POST: api/auth/register
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        if (await _userManager.FindByEmailAsync(dto.Email) != null)
        {
            return BadRequest("A user with this email address already exists.");
        }

        if (await _userManager.FindByNameAsync(dto.Username) != null)
        {
            return BadRequest("This username is already taken.");
        }

        var user = new User
        {
            UserName = dto.Username,
            Email = dto.Email,
            IsActive = true
        };

        var result = await _userManager.CreateAsync(user, dto.Password);

        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        // GENERATE EMAIL CONFIRMATION TOKEN
        var emailToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);

        // Build a mock verification hyperlink payload
        var mockConfirmationLink = $"https://localhost:3000/confirm-email?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(emailToken)}";
        var emailBody = $"<h3>Welcome to TaskFlow!</h3><p>Please click the link below to verify your account registration request:</p><a href='{mockConfirmationLink}'>Verify Email Address</a><br/><br/><p>Token value for API testing tools:<br/><code>{emailToken}</code></p>";

        // Dispatch simulated payload straight to the server console logger
        await _emailSender.SendEmailAsync(user.Email!, "TaskFlow API - Complete Your Registration", emailBody);

        return Ok(new { Message = "User account registered successfully. Please extract your verification token from the console server logs to verify your account before logging in." });
    }

    // POST: api/auth/login
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null)
        {
            return Unauthorized("Invalid credentials.");
        }

        // Checks Password, Lockout status, and custom ApplicationSignInManager.IsActive flag
        var result = await _signInManager.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return StatusCode(StatusCodes.Status423Locked, "Account is temporarily locked due to multiple failed access attempts.");
        }

        if (result.IsNotAllowed)
        {
            if (!user.IsActive)
            {
                return BadRequest("Your account has been deactivated by an administrator.");
            }
            return BadRequest("Login is not allowed at this time.");
        }

        if (!result.Succeeded)
        {
            return Unauthorized("Invalid credentials.");
        }

        // Generate Access JWT and secure random Refresh Token pairs
        var accessToken = await _tokenService.GenerateAccessToken(user);
        var (plainTextRefreshToken, hashedRefreshToken) = _tokenService.GenerateRefreshTokenPair();

        // Capture client contextual data securely
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

        var refreshTokenRecord = new RefreshToken
        {
            TokenHash = hashedRefreshToken,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7), // Refresh Token valid for 7 days
            CreatedByIp = ipAddress
        };

        _context.RefreshTokens.Add(refreshTokenRecord);
        await _context.SaveChangesAsync();

        // Expose only the unhashed raw plain text token value back to the client
        return Ok(new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = plainTextRefreshToken,
            TokenExpiration = DateTime.UtcNow.AddMinutes(60)
        });
    }

    // POST: api/auth/refresh
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseDto>> RefreshToken([FromBody] TokenRequestDto dto)
    {
        var incomingHash = _tokenService.HashToken(dto.RefreshToken);

        // Locate the target token record from storage tracking history
        var storedToken = await _context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == incomingHash);

        if (storedToken == null)
        {
            return Unauthorized("Invalid refresh token.");
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

        // CRITICAL REUSE DETECTION: Breach Protection
        if (!storedToken.IsActive)
        {
            // If an unexpired token is not active but has a replacement hash link, token reuse is occurring!
            if (storedToken.ReplacedByTokenHash != null)
            {
                // Breach detected! Compromised account safety protocol: Revoke all valid refresh sessions immediately
                var activeUserSessions = await _context.RefreshTokens
                    .Where(rt => rt.UserId == storedToken.UserId && rt.RevokedAt == null)
                    .ToListAsync();

                foreach (var session in activeUserSessions)
                {
                    session.RevokedAt = DateTime.UtcNow;
                    session.RevokedByIp = $"{ipAddress} (Breach Compromise Isolation)";
                }

                await _context.SaveChangesAsync();

                // FIXED (Line 242): Clean 403 Forbidden structured response instead of broken native Forbid() helper
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    Message = "Security Breach Alert: Token reuse detected. All active device security contexts have been forcefully dropped for user safety."
                });
            }

            return Unauthorized("Refresh token has been revoked or expired.");
        }

        var user = storedToken.User;
        if (user == null || !user.IsActive)
        {
            return Unauthorized("Associated user account is inactive or missing.");
        }

        // Generate brand new rotation pairs
        var newAccessToken = await _tokenService.GenerateAccessToken(user);
        var (newPlainTextRefreshToken, newHashedRefreshToken) = _tokenService.GenerateRefreshTokenPair();

        // Rotate and invalidate the old token link securely
        storedToken.RevokedAt = DateTime.UtcNow;
        storedToken.RevokedByIp = ipAddress;
        storedToken.ReplacedByTokenHash = newHashedRefreshToken; // Point historical link to replacement reference

        var nextTokenRecord = new RefreshToken
        {
            TokenHash = newHashedRefreshToken,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByIp = ipAddress
        };

        _context.RefreshTokens.Add(nextTokenRecord);
        await _context.SaveChangesAsync();

        return Ok(new AuthResponseDto
        {
            AccessToken = newAccessToken,
            RefreshToken = newPlainTextRefreshToken,
            TokenExpiration = DateTime.UtcNow.AddMinutes(60)
        });
    }



    // POST: api/auth/logout

    [HttpPost("logout")]
    [Authorize] // Requires a valid JWT token to recognize who is requesting a sign-out
    public async Task<IActionResult> Logout([FromBody] LogoutDto dto)
    {
        // 1. Hash the incoming plaintext refresh token to locate it in our database
        var tokenHash = _tokenService.HashToken(dto.RefreshToken);
        var storedToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

        if (storedToken == null)
        {
            return BadRequest("Invalid or missing refresh token context.");
        }

        // 2. Security validation: Ensure the token belongs to the currently logged-in user
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (storedToken.UserId != userId)
        {
            return Forbid("Access Denied: You cannot revoke another user's session tokens.");
        }

        // 3. Explicitly revoke the token state
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        storedToken.RevokedAt = DateTime.UtcNow;
        storedToken.RevokedByIp = ipAddress;

        _context.RefreshTokens.Update(storedToken);
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Logout successful. Security session token revoked." });
    }


    // POST: api/auth/change-password

    [HttpPost("change-password")]
    [Authorize] // Demands an active authenticated claims context session
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        // 1. Extract the current user's ID securely from the JWT bearer token claims
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("Unable to resolve user identity from active security context.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive)
        {
            return Unauthorized("User account is inactive or missing.");
        }

        // 2. Execute password mutation natively via Identity's UserManager
        var result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);

        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        // 3. Security Hardening Option: Force logout everywhere else by nuking all existing refresh records
        var activeSessions = await _context.RefreshTokens
            .Where(rt => rt.UserId == user.Id && rt.RevokedAt == null)
            .ToListAsync();

        foreach (var session in activeSessions)
        {
            session.RevokedAt = DateTime.UtcNow;
            session.RevokedByIp = "Password Modification Security Push";
        }

        await _context.SaveChangesAsync();

        return Ok(new { Message = "Your password has been successfully updated. All other active devices have been logged out." });
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous] // Public endpoint allowing access to signed-out or locked-out users
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);

        // Anti-Enumeration Security Hardening: If user doesn't exist, return Ok immediately.
        // This prevents attackers from guessing valid email addresses on your system.
        if (user == null || !user.IsActive)
        {
            return Ok(new { Message = "If the account exists, a secure password recovery token has been dispatched to your inbox." });
        }

        // Generate a cryptographically secure token using native Identity providers
        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);

        // Build the password payload template link
        var mockResetLink = $"https://localhost:3000/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(resetToken)}";
        var emailBody = $"<h3>Password Reset Request</h3><p>Click the link below to safely reset your credentials:</p><a href='{mockResetLink}'>Reset Password</a><br/><br/><p>Token value for raw API post tools:<br/><code>{resetToken}</code></p>";

        // Forward to our mock output logging logger
        await _emailSender.SendEmailAsync(user.Email!, "TaskFlow API - Account Recovery Request", emailBody);

        return Ok(new { Message = "If the account exists, a secure password recovery token has been dispatched to your inbox." });
    }


    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null || !user.IsActive)
        {
            return BadRequest("Invalid account recovery parameters provided.");
        }

        // Natively validates the token and updates the password hash if valid
        var result = await _userManager.ResetPasswordAsync(user, dto.Token, dto.NewPassword);

        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        // Force clear all active device tokens immediately following validation updates
        var activeSessions = await _context.RefreshTokens
            .Where(rt => rt.UserId == user.Id && rt.RevokedAt == null)
            .ToListAsync();

        foreach (var session in activeSessions)
        {
            session.RevokedAt = DateTime.UtcNow;
            session.RevokedByIp = "Account Reset Recovery Event Push";
        }
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Your password has been successfully updated. All active security contexts have been wiped clean." });
    }

    [HttpPost("confirm-email")]
    [AllowAnonymous] // Public access path so unverified accounts can submit tokens
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null)
        {
            return BadRequest("Invalid account verification parameters provided.");
        }

        // Natively validates the cryptographically secure token and flags EmailConfirmed = true if valid
        var result = await _userManager.ConfirmEmailAsync(user, dto.Token);

        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        return Ok(new { Message = "Email address confirmed successfully! Your account is now fully verified and ready for login transactions." });
    }

}
