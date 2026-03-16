using System;
using System.Threading.Tasks;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Microsoft.Extensions.Configuration;

public record UserRecord(string Id, string Email, string FirstName, string LastName);

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
      DROP TABLE IF EXISTS auth_tokens;
      DROP TABLE IF EXISTS users;
      CREATE TABLE users (
        id TEXT PRIMARY KEY,
        email TEXT UNIQUE NOT NULL,
        password_hash TEXT NOT NULL,
        first_name TEXT NOT NULL,
        last_name TEXT NOT NULL,
        created_at TIMESTAMPTZ DEFAULT NOW()
      );
      CREATE TABLE auth_tokens (
        token TEXT PRIMARY KEY,
        user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
        expires_at TIMESTAMPTZ NOT NULL,
        created_at TIMESTAMPTZ DEFAULT NOW()
      );
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    await cmd.ExecuteNonQueryAsync();
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

  public async Task<(string Id, string PasswordHash, string FirstName, string LastName)?> GetUserByEmailAsync(string email)
  {
    await using var con = new NpgsqlConnection(_cs);
    await con.OpenAsync();
    var sql = "SELECT id, password_hash, first_name, last_name FROM users WHERE email = @email";
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("email", email.ToLowerInvariant());
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return null;
    return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
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
      SELECT u.id, u.email, u.first_name, u.last_name
      FROM auth_tokens t
      JOIN users u ON u.id = t.user_id
      WHERE t.token = @token AND t.expires_at > NOW()
      """;
    await using var cmd = new NpgsqlCommand(sql, con);
    cmd.Parameters.AddWithValue("token", token);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return null;
    return new UserRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
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