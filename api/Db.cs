using System;
using System.Threading.Tasks;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Microsoft.Extensions.Configuration;

public record UserRecord(string Id, string Email, string FirstName, string LastName, bool EmailVerified);

public record AuthUserRow(
    string Id,
    string PasswordHash,
    string FirstName,
    string LastName,
    bool EmailVerified
);

public record EmailVerificationRow(
    string UserId,
    string Email,
    string FirstName,
    string LastName,
    bool EmailVerified,
    string CodeHash,
    DateTime ExpiresAt
);

public record PasswordResetRow(string UserId, string CodeHash, DateTime ExpiresAt);
public record EmailChangeRow(string UserId, string NewEmail, string CodeHash, DateTime ExpiresAt);

public record UserProfileRecord(
    string UserId,
    string[] Skills,
    string[] Tools,
    string[] Roles,
    string? ExperienceLevel,
    string? Summary,
    int? AtsScore,
    string AssessmentScores,  // raw JSONB string: { "role": score }
    string SearchHistory,      // raw JSONB string: [{ "query": "...", "timestamp": "..." }]
    DateTime UpdatedAt,
    string FirstName,
    string LastName,
    string Email,
    string? ResumeText,
    string Education  // raw JSONB string: [{"degree":"...","institution":"...","year":"..."}]
);

public class Db {
  private readonly string _cs;
  public Db(IConfiguration cfg) {
    var host = cfg["PGHOST"]; var port = cfg["PGPORT"];
    var db = cfg["PGDATABASE"]; var user = cfg["PGUSER"]; var pw = cfg["PGPASSWORD"];
    var ssl = cfg["PGSSLmode"] ?? "require";
    _cs = $"Host={host};Port={port};Database={db};Username={user};Password={pw};SSL Mode={ssl};Trust Server Certificate=true";
  }

  public async Task EnsureAuthTablesAsync()
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = """
      CREATE TABLE IF NOT EXISTS users (
        id TEXT PRIMARY KEY,
        email TEXT UNIQUE NOT NULL,
        password_hash TEXT NOT NULL,
        first_name TEXT NOT NULL,
        last_name TEXT NOT NULL,
        email_verified_at TIMESTAMPTZ,
        created_at TIMESTAMPTZ DEFAULT NOW()
      );
      CREATE TABLE IF NOT EXISTS auth_tokens (
        token TEXT PRIMARY KEY,
        user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
        expires_at TIMESTAMPTZ NOT NULL,
        created_at TIMESTAMPTZ DEFAULT NOW()
      );
      CREATE TABLE IF NOT EXISTS sessions (
        id TEXT PRIMARY KEY,
        user_id TEXT NOT NULL,
        type TEXT NOT NULL,
        payload JSONB,
        created_at TIMESTAMPTZ DEFAULT NOW()
      );
      CREATE TABLE IF NOT EXISTS email_verification_codes (
        user_id TEXT PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
        code_hash TEXT NOT NULL,
        expires_at TIMESTAMPTZ NOT NULL,
        created_at TIMESTAMPTZ DEFAULT NOW()
      );
      CREATE TABLE IF NOT EXISTS password_reset_codes (
        user_id TEXT PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
        code_hash TEXT NOT NULL,
        expires_at TIMESTAMPTZ NOT NULL,
        created_at TIMESTAMPTZ DEFAULT NOW()
      );
      CREATE TABLE IF NOT EXISTS email_change_requests (
        user_id TEXT PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
        new_email TEXT NOT NULL,
        code_hash TEXT NOT NULL,
        expires_at TIMESTAMPTZ NOT NULL,
        created_at TIMESTAMPTZ DEFAULT NOW()
      );
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    await cmd.ExecuteNonQueryAsync();

    var migrate = """
      ALTER TABLE users ADD COLUMN IF NOT EXISTS email_verified_at TIMESTAMPTZ;
      """;
    await using var migrateCmd = new NpgsqlCommand(migrate, con);
    await migrateCmd.ExecuteNonQueryAsync();
  }

  public async Task<string> CreateUserAsync(string email, string passwordHash, string firstName, string lastName)
  {
    var id = Guid.NewGuid().ToString("N");
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = "INSERT INTO users (id, email, password_hash, first_name, last_name) VALUES (@id, @email, @hash, @fn, @ln)";
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("id", id);
    cmd.Parameters.AddWithValue("email", email.ToLowerInvariant());
    cmd.Parameters.AddWithValue("hash", passwordHash);
    cmd.Parameters.AddWithValue("fn", firstName);
    cmd.Parameters.AddWithValue("ln", lastName);
    await cmd.ExecuteNonQueryAsync();
    return id;
  }

  public async Task<AuthUserRow?> GetAuthUserByIdAsync(string userId)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = """
      SELECT id, password_hash, first_name, last_name, email_verified_at IS NOT NULL
      FROM users
      WHERE id = @uid
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("uid", userId);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return null;
    return new AuthUserRow(
      Id: reader.GetString(0),
      PasswordHash: reader.GetString(1),
      FirstName: reader.GetString(2),
      LastName: reader.GetString(3),
      EmailVerified: reader.GetBoolean(4)
    );
  }

  public async Task<AuthUserRow?> GetUserByEmailAsync(string email)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = """
      SELECT id, password_hash, first_name, last_name, email_verified_at IS NOT NULL
      FROM users
      WHERE email = @email
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("email", email.ToLowerInvariant());
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return null;
    return new AuthUserRow(
      Id: reader.GetString(0),
      PasswordHash: reader.GetString(1),
      FirstName: reader.GetString(2),
      LastName: reader.GetString(3),
      EmailVerified: reader.GetBoolean(4)
    );
  }

  public async Task CreateTokenAsync(string token, string userId, DateTime expiresAt)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = "INSERT INTO auth_tokens (token, user_id, expires_at) VALUES (@token, @uid, @exp)";
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("token", token);
    cmd.Parameters.AddWithValue("uid", userId);
    cmd.Parameters.AddWithValue("exp", expiresAt);
    await cmd.ExecuteNonQueryAsync();
  }

  public async Task<UserRecord?> GetUserByTokenAsync(string token)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = """
      SELECT u.id, u.email, u.first_name, u.last_name, u.email_verified_at IS NOT NULL
      FROM auth_tokens t
      JOIN users u ON u.id = t.user_id
      WHERE t.token = @token AND t.expires_at > NOW()
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("token", token);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return null;
    return new UserRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4));
  }

  public async Task SaveEmailVerificationCodeAsync(string userId, string codeHash, DateTime expiresAt)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = """
      INSERT INTO email_verification_codes (user_id, code_hash, expires_at, created_at)
      VALUES (@uid, @hash, @exp, NOW())
      ON CONFLICT (user_id) DO UPDATE SET
        code_hash = EXCLUDED.code_hash,
        expires_at = EXCLUDED.expires_at,
        created_at = NOW();
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("uid", userId);
    cmd.Parameters.AddWithValue("hash", codeHash);
    cmd.Parameters.AddWithValue("exp", expiresAt);
    await cmd.ExecuteNonQueryAsync();
  }

  public async Task<EmailVerificationRow?> GetEmailVerificationByEmailAsync(string email)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = """
      SELECT u.id, u.email, u.first_name, u.last_name, u.email_verified_at IS NOT NULL,
             v.code_hash, v.expires_at
      FROM users u
      LEFT JOIN email_verification_codes v ON v.user_id = u.id
      WHERE u.email = @email
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("email", email.ToLowerInvariant());
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return null;
    if (reader.IsDBNull(5) || reader.IsDBNull(6)) return null;

    return new EmailVerificationRow(
      UserId: reader.GetString(0),
      Email: reader.GetString(1),
      FirstName: reader.GetString(2),
      LastName: reader.GetString(3),
      EmailVerified: reader.GetBoolean(4),
      CodeHash: reader.GetString(5),
      ExpiresAt: reader.GetDateTime(6)
    );
  }

  public async Task MarkEmailVerifiedAsync(string userId)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = """
      UPDATE users
      SET email_verified_at = COALESCE(email_verified_at, NOW())
      WHERE id = @uid;

      DELETE FROM email_verification_codes
      WHERE user_id = @uid;
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("uid", userId);
    await cmd.ExecuteNonQueryAsync();
  }

  public async Task EnsureProfileTablesAsync()
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    // No FK to users — EnsureAuthTablesAsync drops/recreates users each startup,
    // which would break a FK constraint. user_id is a soft reference.
    var sql = """
      CREATE TABLE IF NOT EXISTS user_profiles (
        user_id TEXT PRIMARY KEY,
        skills TEXT[] NOT NULL DEFAULT '{}',
        tools TEXT[] NOT NULL DEFAULT '{}',
        roles TEXT[] NOT NULL DEFAULT '{}',
        experience_level TEXT,
        summary TEXT,
        ats_score INT,
        assessment_scores JSONB NOT NULL DEFAULT '{}',
        search_history JSONB NOT NULL DEFAULT '[]',
        updated_at TIMESTAMPTZ DEFAULT NOW()
      );
      """;

    await using var cmd = new NpgsqlCommand(sql, con);
    await cmd.ExecuteNonQueryAsync();

    // Add columns introduced after initial schema (safe on existing DBs)
    var migrate = """
      ALTER TABLE user_profiles ADD COLUMN IF NOT EXISTS resume_text TEXT;
      ALTER TABLE user_profiles ADD COLUMN IF NOT EXISTS education JSONB NOT NULL DEFAULT '[]';
      """;
    await using var migrate_cmd = new NpgsqlCommand(migrate, con);
    await migrate_cmd.ExecuteNonQueryAsync();
  }

  public async Task UpsertUserProfileAsync(
    string userId,
    string[] skills,
    string[] tools,
    string[] roles,
    string? experienceLevel,
    string? summary,
    int? atsScore,
    string? resumeText = null,
    string? education = null,
    bool replaceResumeData = false)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = replaceResumeData
      ? """
      INSERT INTO user_profiles (user_id, skills, tools, roles, experience_level, summary, ats_score, resume_text, education, updated_at)
      VALUES (@uid, @skills, @tools, @roles, @exp, @summary, @ats, @resumeText, @education::jsonb, NOW())
      ON CONFLICT (user_id) DO UPDATE SET
        skills = EXCLUDED.skills,
        tools = EXCLUDED.tools,
        roles = EXCLUDED.roles,
        experience_level = EXCLUDED.experience_level,
        summary = EXCLUDED.summary,
        ats_score = COALESCE(EXCLUDED.ats_score, user_profiles.ats_score),
        resume_text = EXCLUDED.resume_text,
        education = EXCLUDED.education,
        updated_at = NOW();
      """
      : """
      INSERT INTO user_profiles (user_id, skills, tools, roles, experience_level, summary, ats_score, resume_text, education, updated_at)
      VALUES (@uid, @skills, @tools, @roles, @exp, @summary, @ats, @resumeText, @education::jsonb, NOW())
      ON CONFLICT (user_id) DO UPDATE SET
        skills = EXCLUDED.skills,
        tools = EXCLUDED.tools,
        roles = EXCLUDED.roles,
        experience_level = COALESCE(EXCLUDED.experience_level, user_profiles.experience_level),
        summary = COALESCE(EXCLUDED.summary, user_profiles.summary),
        ats_score = COALESCE(EXCLUDED.ats_score, user_profiles.ats_score),
        resume_text = COALESCE(EXCLUDED.resume_text, user_profiles.resume_text),
        education = CASE WHEN EXCLUDED.education = '[]'::jsonb THEN user_profiles.education ELSE EXCLUDED.education END,
        updated_at = NOW();
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("uid", userId);
    cmd.Parameters.Add(new NpgsqlParameter("skills", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = skills });
    cmd.Parameters.Add(new NpgsqlParameter("tools", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = tools });
    cmd.Parameters.Add(new NpgsqlParameter("roles", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = roles });
    cmd.Parameters.AddWithValue("exp", (object?)experienceLevel ?? DBNull.Value);
    cmd.Parameters.AddWithValue("summary", (object?)summary ?? DBNull.Value);
    cmd.Parameters.AddWithValue("ats", (object?)atsScore ?? DBNull.Value);
    cmd.Parameters.AddWithValue("resumeText", (object?)resumeText ?? DBNull.Value);
    cmd.Parameters.Add(new NpgsqlParameter("education", NpgsqlDbType.Jsonb) { Value = education ?? "[]" });
    await cmd.ExecuteNonQueryAsync();
  }

  public async Task UpdateAtsScoreAsync(string userId, int atsScore)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = """
      INSERT INTO user_profiles (user_id, skills, tools, roles, ats_score, updated_at)
      VALUES (@uid, '{}', '{}', '{}', @ats, NOW())
      ON CONFLICT (user_id) DO UPDATE SET
        ats_score = EXCLUDED.ats_score,
        updated_at = NOW();
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("uid", userId);
    cmd.Parameters.AddWithValue("ats", atsScore);
    await cmd.ExecuteNonQueryAsync();
  }

  public async Task<UserProfileRecord?> GetUserProfileAsync(string userId)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = """
      SELECT p.skills, p.tools, p.roles, p.experience_level, p.summary, p.ats_score,
             p.assessment_scores, p.search_history, p.updated_at,
             u.first_name, u.last_name, u.email,
             p.resume_text, COALESCE(p.education::text, '[]')
      FROM user_profiles p
      JOIN users u ON u.id = p.user_id
      WHERE p.user_id = @uid
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("uid", userId);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return null;

    return new UserProfileRecord(
      UserId: userId,
      Skills: reader.GetFieldValue<string[]>(0),
      Tools: reader.GetFieldValue<string[]>(1),
      Roles: reader.GetFieldValue<string[]>(2),
      ExperienceLevel: reader.IsDBNull(3) ? null : reader.GetString(3),
      Summary: reader.IsDBNull(4) ? null : reader.GetString(4),
      AtsScore: reader.IsDBNull(5) ? null : reader.GetInt32(5),
      AssessmentScores: reader.GetString(6),
      SearchHistory: reader.GetString(7),
      UpdatedAt: reader.GetDateTime(8),
      FirstName: reader.GetString(9),
      LastName: reader.GetString(10),
      Email: reader.GetString(11),
      ResumeText: reader.IsDBNull(12) ? null : reader.GetString(12),
      Education: reader.GetString(13)
    );
  }

  public async Task SaveAssessmentScoreAsync(string userId, string role, int score)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = """
      INSERT INTO user_profiles (user_id, assessment_scores, updated_at)
      VALUES (@uid, jsonb_build_object(@role::text, @score::int), NOW())
      ON CONFLICT (user_id) DO UPDATE SET
        assessment_scores = user_profiles.assessment_scores || jsonb_build_object(@role::text, @score::int),
        updated_at = NOW();
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("uid", userId);
    cmd.Parameters.AddWithValue("role", role);
    cmd.Parameters.AddWithValue("score", score);
    await cmd.ExecuteNonQueryAsync();
  }

  public async Task LogSearchQueryAsync(string userId, string query)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var entry = JsonSerializer.Serialize(new { query, timestamp = DateTime.UtcNow.ToString("O") });
    var sql = """
      INSERT INTO user_profiles (user_id, search_history, updated_at)
      VALUES (@uid, jsonb_build_array(@entry::jsonb), NOW())
      ON CONFLICT (user_id) DO UPDATE SET
        search_history = user_profiles.search_history || jsonb_build_array(@entry::jsonb),
        updated_at = NOW();
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("uid", userId);
    cmd.Parameters.AddWithValue("entry", entry);
    await cmd.ExecuteNonQueryAsync();
  }

  public async Task SavePasswordResetCodeAsync(string userId, string codeHash, DateTime expiresAt)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = """
      INSERT INTO password_reset_codes (user_id, code_hash, expires_at, created_at)
      VALUES (@uid, @hash, @exp, NOW())
      ON CONFLICT (user_id) DO UPDATE SET
        code_hash = EXCLUDED.code_hash,
        expires_at = EXCLUDED.expires_at,
        created_at = NOW();
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("uid", userId);
    cmd.Parameters.AddWithValue("hash", codeHash);
    cmd.Parameters.AddWithValue("exp", expiresAt);
    await cmd.ExecuteNonQueryAsync();
  }

  public async Task<PasswordResetRow?> GetPasswordResetByEmailAsync(string email)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = """
      SELECT r.user_id, r.code_hash, r.expires_at
      FROM password_reset_codes r
      JOIN users u ON u.id = r.user_id
      WHERE u.email = @email
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("email", email.ToLowerInvariant());
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return null;
    return new PasswordResetRow(reader.GetString(0), reader.GetString(1), reader.GetDateTime(2));
  }

  public async Task UpdatePasswordHashAsync(string userId, string newHash)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = """
      UPDATE users SET password_hash = @hash WHERE id = @uid;
      DELETE FROM password_reset_codes WHERE user_id = @uid;
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("uid", userId);
    cmd.Parameters.AddWithValue("hash", newHash);
    await cmd.ExecuteNonQueryAsync();
  }

  public async Task SaveEmailChangeRequestAsync(string userId, string newEmail, string codeHash, DateTime expiresAt)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = """
      INSERT INTO email_change_requests (user_id, new_email, code_hash, expires_at, created_at)
      VALUES (@uid, @newEmail, @hash, @exp, NOW())
      ON CONFLICT (user_id) DO UPDATE SET
        new_email = EXCLUDED.new_email,
        code_hash = EXCLUDED.code_hash,
        expires_at = EXCLUDED.expires_at,
        created_at = NOW();
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("uid", userId);
    cmd.Parameters.AddWithValue("newEmail", newEmail.ToLowerInvariant());
    cmd.Parameters.AddWithValue("hash", codeHash);
    cmd.Parameters.AddWithValue("exp", expiresAt);
    await cmd.ExecuteNonQueryAsync();
  }

  public async Task<EmailChangeRow?> GetEmailChangeRequestByUserIdAsync(string userId)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = "SELECT user_id, new_email, code_hash, expires_at FROM email_change_requests WHERE user_id = @uid";
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("uid", userId);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return null;
    return new EmailChangeRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDateTime(3));
  }

  public async Task UpdateEmailAsync(string userId, string newEmail)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = """
      UPDATE users SET email = @email WHERE id = @uid;
      DELETE FROM email_change_requests WHERE user_id = @uid;
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("uid", userId);
    cmd.Parameters.AddWithValue("email", newEmail.ToLowerInvariant());
    await cmd.ExecuteNonQueryAsync();
  }

  public async Task SaveSessionAsync(string userId, string type, string raw)
{
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();

    // Ensure we store valid JSON in the JSONB column
    object jsonValue;
    try
    {
        // If 'raw' is valid JSON, keep it as-is
        JsonDocument.Parse(raw);
        jsonValue = raw;
    }
    catch
    {
        // If it's not valid JSON, wrap it so JSONB stays valid
        jsonValue = JsonSerializer.Serialize(new { raw });
    }

    var sql = "INSERT INTO sessions (id, user_id, type, payload) VALUES (@id, @uid, @t, @p)";
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("id", $"sess-{Guid.NewGuid()}");
    cmd.Parameters.AddWithValue("uid", userId);
    cmd.Parameters.AddWithValue("t", type);

    // Tell Npgsql this parameter is JSONB
    var p = cmd.Parameters.Add("p", NpgsqlDbType.Jsonb);
    p.Value = jsonValue;

    await cmd.ExecuteNonQueryAsync();
}
}
