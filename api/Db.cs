using System;
using System.Threading.Tasks;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Microsoft.Extensions.Configuration;

public class Db {
  private readonly string _cs;
  public Db(IConfiguration cfg) {
    var host = cfg["PGHOST"]; var port = cfg["PGPORT"];
    var db = cfg["PGDATABASE"]; var user = cfg["PGUSER"]; var pw = cfg["PGPASSWORD"];
    var ssl = cfg["PGSSLmode"] ?? "require";
    _cs = $"Host={host};Port={port};Database={db};Username={user};Password={pw};SSL Mode={ssl};Trust Server Certificate=true";
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