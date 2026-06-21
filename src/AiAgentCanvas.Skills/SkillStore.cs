using Microsoft.Data.Sqlite;

namespace AiAgentCanvas.Skills;

public class SkillRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PromptTemplate { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class SkillStore : IDisposable
{
    private readonly SqliteConnection _connection;

    public SkillStore(string connectionString)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        Initialize();
    }

    private void Initialize()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS skills (
                id TEXT PRIMARY KEY,
                name TEXT UNIQUE NOT NULL,
                description TEXT NOT NULL,
                prompt_template TEXT NOT NULL,
                created_at TEXT DEFAULT (datetime('now'))
            )
            """;
        cmd.ExecuteNonQuery();
    }

    public void SaveSkill(SkillRecord skill)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO skills (id, name, description, prompt_template, created_at)
            VALUES (@id, @name, @description, @prompt_template, @created_at)
            """;
        cmd.Parameters.AddWithValue("@id", skill.Id);
        cmd.Parameters.AddWithValue("@name", skill.Name);
        cmd.Parameters.AddWithValue("@description", skill.Description);
        cmd.Parameters.AddWithValue("@prompt_template", skill.PromptTemplate);
        cmd.Parameters.AddWithValue("@created_at",
            string.IsNullOrEmpty(skill.CreatedAt) ? DateTime.UtcNow.ToString("o") : skill.CreatedAt);
        cmd.ExecuteNonQuery();
    }

    public List<SkillRecord> ListSkills()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, description, prompt_template, created_at FROM skills ORDER BY name";

        var skills = new List<SkillRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            skills.Add(new SkillRecord
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                PromptTemplate = reader.GetString(3),
                CreatedAt = reader.GetString(4),
            });
        }
        return skills;
    }

    public SkillRecord? GetSkill(string name)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, description, prompt_template, created_at FROM skills WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", name);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new SkillRecord
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2),
            PromptTemplate = reader.GetString(3),
            CreatedAt = reader.GetString(4),
        };
    }

    public bool RemoveSkill(string name)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM skills WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", name);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
