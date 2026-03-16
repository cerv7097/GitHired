namespace CareerCoach.Services;

public class AuthService
{
    private readonly Db _db;

    public AuthService(Db db)
    {
        _db = db;
    }

    public async Task<(string Token, UserRecord User)> RegisterAsync(
        string email, string password, string firstName, string lastName)
    {
        var existing = await _db.GetUserByEmailAsync(email);
        if (existing != null)
            throw new InvalidOperationException("An account with that email already exists.");

        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var userId = await _db.CreateUserAsync(email, hash, firstName, lastName);
        var user = new UserRecord(userId, email.ToLowerInvariant(), firstName, lastName);

        var token = await IssueTokenAsync(userId);
        return (token, user);
    }

    public async Task<(string Token, UserRecord User)> LoginAsync(string email, string password)
    {
        var row = await _db.GetUserByEmailAsync(email);
        if (row == null || !BCrypt.Net.BCrypt.Verify(password, row.Value.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");

        var user = new UserRecord(row.Value.Id, email.ToLowerInvariant(), row.Value.FirstName, row.Value.LastName);
        var token = await IssueTokenAsync(row.Value.Id);
        return (token, user);
    }

    public Task<UserRecord?> ValidateTokenAsync(string token) =>
        _db.GetUserByTokenAsync(token);

    private async Task<string> IssueTokenAsync(string userId)
    {
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        await _db.CreateTokenAsync(token, userId, DateTime.UtcNow.AddDays(7));
        return token;
    }
}
