namespace CareerCoach.Services;

public class AuthService
{
    private readonly Db _db;
    private readonly EmailSender _emailSender;
    private readonly ILogger<AuthService> _logger;
    private const int VerificationCodeLength = 6;
    private static readonly TimeSpan VerificationCodeLifetime = TimeSpan.FromMinutes(15);

    public AuthService(Db db, EmailSender emailSender, ILogger<AuthService> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task<UserRecord> RegisterAsync(
        string email, string password, string firstName, string lastName)
    {
        var existing = await _db.GetUserByEmailAsync(email);
        if (existing != null)
        {
            if (existing.EmailVerified)
                throw new InvalidOperationException("An account with that email already exists.");

            var existingUser = new UserRecord(
                existing.Id,
                email.ToLowerInvariant(),
                existing.FirstName,
                existing.LastName,
                false
            );
            await SendVerificationCodeAsync(existingUser);
            return existingUser;
        }

        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var userId = await _db.CreateUserAsync(email, hash, firstName, lastName);
        var user = new UserRecord(userId, email.ToLowerInvariant(), firstName, lastName, false);

        await SendVerificationCodeAsync(user);
        return user;
    }

    public async Task<(string Token, UserRecord User)> LoginAsync(string email, string password)
    {
        var row = await _db.GetUserByEmailAsync(email);
        if (row == null || !BCrypt.Net.BCrypt.Verify(password, row.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");
        if (!row.EmailVerified)
        {
            var unverifiedUser = new UserRecord(row.Id, email.ToLowerInvariant(), row.FirstName, row.LastName, false);
            await SendVerificationCodeAsync(unverifiedUser);
            throw new InvalidOperationException("Email address has not been verified. A new verification code was sent.");
        }

        var user = new UserRecord(row.Id, email.ToLowerInvariant(), row.FirstName, row.LastName, row.EmailVerified);
        var token = await IssueTokenAsync(row.Id);
        return (token, user);
    }

    public Task<UserRecord?> ValidateTokenAsync(string token) =>
        _db.GetUserByTokenAsync(token);

    public async Task<(string Token, UserRecord User)> VerifyEmailAsync(string email, string code)
    {
        var userRow = await _db.GetUserByEmailAsync(email);
        if (userRow == null)
            throw new UnauthorizedAccessException("Invalid or expired verification code.");
        if (userRow.EmailVerified)
            throw new InvalidOperationException("Email address has already been verified.");

        var verification = await _db.GetEmailVerificationByEmailAsync(email);
        if (verification == null)
            throw new UnauthorizedAccessException("Invalid or expired verification code.");
        if (verification.ExpiresAt <= DateTime.UtcNow)
            throw new UnauthorizedAccessException("Verification code has expired.");
        if (!BCrypt.Net.BCrypt.Verify(code, verification.CodeHash))
            throw new UnauthorizedAccessException("Invalid or expired verification code.");

        await _db.MarkEmailVerifiedAsync(verification.UserId);

        var user = new UserRecord(
            verification.UserId,
            verification.Email,
            verification.FirstName,
            verification.LastName,
            true
        );

        var token = await IssueTokenAsync(verification.UserId);
        return (token, user);
    }

    public async Task RequestPasswordResetAsync(string email)
    {
        var row = await _db.GetUserByEmailAsync(email);
        // Return silently if account not found (security — don't reveal existence)
        if (row == null || !row.EmailVerified) return;

        var code = GenerateVerificationCode();
        var expiresAt = DateTime.UtcNow.Add(VerificationCodeLifetime);
        var codeHash = BCrypt.Net.BCrypt.HashPassword(code);

        await _db.SavePasswordResetCodeAsync(row.Id, codeHash, expiresAt);
        await _emailSender.SendPasswordResetCodeAsync(email.ToLowerInvariant(), row.FirstName, code, expiresAt);
        _logger.LogInformation("Issued password reset code for user {UserId}", row.Id);
    }

    public async Task ResetPasswordAsync(string email, string code, string newPassword)
    {
        var row = await _db.GetUserByEmailAsync(email);
        if (row == null)
            throw new UnauthorizedAccessException("Invalid or expired reset code.");

        var resetRow = await _db.GetPasswordResetByEmailAsync(email);
        if (resetRow == null)
            throw new UnauthorizedAccessException("Invalid or expired reset code.");
        if (resetRow.ExpiresAt <= DateTime.UtcNow)
            throw new UnauthorizedAccessException("Reset code has expired.");
        if (!BCrypt.Net.BCrypt.Verify(code, resetRow.CodeHash))
            throw new UnauthorizedAccessException("Invalid or expired reset code.");

        var newHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _db.UpdatePasswordHashAsync(row.Id, newHash);
        _logger.LogInformation("Password reset completed for user {UserId}", row.Id);
    }

    public async Task ChangePasswordAsync(string userId, string currentPassword, string newPassword)
    {
        var row = await _db.GetAuthUserByIdAsync(userId);
        if (row == null)
            throw new UnauthorizedAccessException("User not found.");
        if (!BCrypt.Net.BCrypt.Verify(currentPassword, row.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect.");

        var newHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _db.UpdatePasswordHashAsync(userId, newHash);
        _logger.LogInformation("Password changed for user {UserId}", userId);
    }

    public async Task RequestEmailChangeAsync(string userId, string newEmail, string firstName)
    {
        var existing = await _db.GetUserByEmailAsync(newEmail);
        if (existing != null)
            throw new InvalidOperationException("That email address is already in use.");

        var code = GenerateVerificationCode();
        var expiresAt = DateTime.UtcNow.Add(VerificationCodeLifetime);
        var codeHash = BCrypt.Net.BCrypt.HashPassword(code);

        await _db.SaveEmailChangeRequestAsync(userId, newEmail, codeHash, expiresAt);
        await _emailSender.SendEmailChangeCodeAsync(newEmail, firstName, newEmail, code, expiresAt);
        _logger.LogInformation("Issued email change code for user {UserId}", userId);
    }

    public async Task<string> ConfirmEmailChangeAsync(string userId, string code)
    {
        var changeRow = await _db.GetEmailChangeRequestByUserIdAsync(userId);
        if (changeRow == null)
            throw new UnauthorizedAccessException("No pending email change request.");
        if (changeRow.ExpiresAt <= DateTime.UtcNow)
            throw new UnauthorizedAccessException("Email change code has expired.");
        if (!BCrypt.Net.BCrypt.Verify(code, changeRow.CodeHash))
            throw new UnauthorizedAccessException("Invalid confirmation code.");

        // Ensure the new email is still unclaimed
        var conflict = await _db.GetUserByEmailAsync(changeRow.NewEmail);
        if (conflict != null && conflict.Id != userId)
            throw new InvalidOperationException("That email address is already in use.");

        await _db.UpdateEmailAsync(userId, changeRow.NewEmail);
        _logger.LogInformation("Email changed for user {UserId}", userId);
        return changeRow.NewEmail;
    }

    public async Task ResendVerificationCodeAsync(string email)
    {
        var row = await _db.GetUserByEmailAsync(email);
        if (row == null)
            throw new KeyNotFoundException("Account not found.");
        if (row.EmailVerified)
            throw new InvalidOperationException("Email address has already been verified.");

        var user = new UserRecord(row.Id, email.ToLowerInvariant(), row.FirstName, row.LastName, row.EmailVerified);
        await SendVerificationCodeAsync(user);
    }

    private async Task<string> IssueTokenAsync(string userId)
    {
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        await _db.CreateTokenAsync(token, userId, DateTime.UtcNow.AddDays(7));
        return token;
    }

    private async Task SendVerificationCodeAsync(UserRecord user)
    {
        var code = GenerateVerificationCode();
        var expiresAt = DateTime.UtcNow.Add(VerificationCodeLifetime);
        var codeHash = BCrypt.Net.BCrypt.HashPassword(code);

        await _db.SaveEmailVerificationCodeAsync(user.Id, codeHash, expiresAt);
        await _emailSender.SendVerificationCodeAsync(user.Email, user.FirstName, code, expiresAt);
        _logger.LogInformation("Issued email verification code for user {UserId}", user.Id);
    }

    private static string GenerateVerificationCode()
    {
        var maxValue = (int)Math.Pow(10, VerificationCodeLength);
        var value = Random.Shared.Next(0, maxValue);
        return value.ToString($"D{VerificationCodeLength}");
    }
}
