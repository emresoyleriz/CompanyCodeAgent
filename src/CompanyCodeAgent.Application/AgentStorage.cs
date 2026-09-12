using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace CompanyCodeAgent.Application;

public sealed class AgentStorage
{
    private readonly string _connectionString;

    public AgentStorage(string? databasePath = null)
    {
        var path = databasePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CompanyCodeAgent", "agent.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        Initialize();
    }

    public void WriteAudit(string sessionId, string eventType, string detail)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO audit_events (session_id, event_type, detail, created_at) VALUES ($session, $type, $detail, $created);";
        command.Parameters.AddWithValue("$session", sessionId);
        command.Parameters.AddWithValue("$type", eventType);
        command.Parameters.AddWithValue("$detail", Redact(detail));
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void SaveMessage(string sessionId, string role, string content)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO session_messages (session_id, role, content, created_at) VALUES ($session, $role, $content, $created);";
        command.Parameters.AddWithValue("$session", sessionId);
        command.Parameters.AddWithValue("$role", role);
        command.Parameters.AddWithValue("$content", Redact(content));
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<(string Role, string Content)> ReadMessages(string sessionId, int limit = 100)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT role, content FROM session_messages WHERE session_id = $session ORDER BY id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$session", sessionId);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var items = new List<(string Role, string Content)>();
        while (reader.Read()) items.Add((reader.GetString(0), reader.GetString(1)));
        items.Reverse();
        return items;
    }

    public string CreateCheckpoint(string sessionId, string workspacePath, IEnumerable<string> paths)
    {
        var checkpointId = Guid.NewGuid().ToString("N");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(path);
            var existed = File.Exists(fullPath);
            var content = existed ? File.ReadAllBytes(fullPath) : Array.Empty<byte>();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO checkpoint_files (checkpoint_id, session_id, path, existed, content, sha256, created_at) VALUES ($id, $session, $path, $existed, $content, $hash, $created);";
            command.Parameters.AddWithValue("$id", checkpointId);
            command.Parameters.AddWithValue("$session", sessionId);
            command.Parameters.AddWithValue("$path", fullPath);
            command.Parameters.AddWithValue("$existed", existed ? 1 : 0);
            command.Parameters.AddWithValue("$content", content);
            command.Parameters.AddWithValue("$hash", Convert.ToHexString(SHA256.HashData(content)));
            command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        WriteAudit(sessionId, "checkpoint_created", checkpointId);
        return checkpointId;
    }

    public IReadOnlyList<string> RestoreCheckpoint(string checkpointId)
    {
        using var connection = Open();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT path, existed, content FROM checkpoint_files WHERE checkpoint_id = $id ORDER BY id;";
        query.Parameters.AddWithValue("$id", checkpointId);
        using var reader = query.ExecuteReader();
        var restored = new List<string>();
        while (reader.Read())
        {
            var path = reader.GetString(0);
            var existed = reader.GetInt64(1) == 1;
            if (existed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, (byte[])reader[2]);
            }
            else if (File.Exists(path)) File.Delete(path);
            restored.Add(path);
        }
        if (restored.Count == 0) throw new InvalidOperationException("Checkpoint bulunamadı.");
        return restored;
    }

    public IReadOnlyList<CheckpointSummary> ListCheckpoints(string sessionId, int limit = 50)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT checkpoint_id, MIN(created_at), COUNT(*) FROM checkpoint_files WHERE session_id = $session GROUP BY checkpoint_id ORDER BY MIN(id) DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$session", sessionId);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var checkpoints = new List<CheckpointSummary>();
        while (reader.Read()) checkpoints.Add(new CheckpointSummary(reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1)), reader.GetInt32(2)));
        return checkpoints;
    }

    public IReadOnlyList<CheckpointDifference> CompareCheckpoint(string checkpointId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT path, existed, content, sha256 FROM checkpoint_files WHERE checkpoint_id = $id ORDER BY path;";
        command.Parameters.AddWithValue("$id", checkpointId);
        using var reader = command.ExecuteReader();
        var differences = new List<CheckpointDifference>();
        while (reader.Read())
        {
            var path = reader.GetString(0);
            var existed = reader.GetInt64(1) == 1;
            var before = (byte[])reader[2];
            var currentExists = File.Exists(path);
            var current = currentExists ? File.ReadAllBytes(path) : Array.Empty<byte>();
            var same = existed == currentExists && SHA256.HashData(before).SequenceEqual(SHA256.HashData(current));
            differences.Add(new CheckpointDifference(path, existed, currentExists, same ? "unchanged" : existed && !currentExists ? "deleted" : !existed && currentExists ? "created" : "modified"));
        }
        if (differences.Count == 0) throw new InvalidOperationException("Checkpoint bulunamadı.");
        return differences;
    }

    public long CreateTask(string sessionId, string title, string status = "pending")
    {
        ValidateTaskStatus(status);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO task_items (session_id, title, status, created_at, updated_at) VALUES ($session, $title, $status, $created, $updated); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$session", sessionId);
        command.Parameters.AddWithValue("$title", Redact(title));
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        return (long)command.ExecuteScalar()!;
    }

    public IReadOnlyList<AgentTaskItem> ListTasks(string sessionId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, status, created_at, updated_at FROM task_items WHERE session_id = $session ORDER BY id;";
        command.Parameters.AddWithValue("$session", sessionId);
        using var reader = command.ExecuteReader();
        var tasks = new List<AgentTaskItem>();
        while (reader.Read()) tasks.Add(new AgentTaskItem(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3)), DateTimeOffset.Parse(reader.GetString(4))));
        return tasks;
    }

    public void UpdateTask(string sessionId, long id, string status)
    {
        ValidateTaskStatus(status);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_items SET status = $status, updated_at = $updated WHERE id = $id AND session_id = $session;";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$session", sessionId);
        if (command.ExecuteNonQuery() != 1) throw new KeyNotFoundException("Görev bulunamadı.");
    }

    public IReadOnlyList<AuditEventItem> ReadAuditEvents(string sessionId, int limit = 100)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT event_type, detail, created_at FROM audit_events WHERE session_id = $session ORDER BY id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$session", sessionId);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var events = new List<AuditEventItem>();
        while (reader.Read()) events.Add(new AuditEventItem(reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2))));
        return events;
    }

    public void RecordUsage(string sessionId, string model, int tokens)
    {
        if (tokens < 0) throw new ArgumentOutOfRangeException(nameof(tokens));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO usage_events (session_id, model, tokens, created_at) VALUES ($session, $model, $tokens, $created);";
        command.Parameters.AddWithValue("$session", sessionId);
        command.Parameters.AddWithValue("$model", Redact(model));
        command.Parameters.AddWithValue("$tokens", tokens);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<UsageItem> ReadUsage(string sessionId, int limit = 100)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT model, tokens, created_at FROM usage_events WHERE session_id = $session ORDER BY id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$session", sessionId);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var items = new List<UsageItem>();
        while (reader.Read()) items.Add(new UsageItem(reader.GetString(0), reader.GetInt32(1), DateTimeOffset.Parse(reader.GetString(2))));
        return items;
    }

    private void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS audit_events (id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, event_type TEXT NOT NULL, detail TEXT NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS session_messages (id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, role TEXT NOT NULL, content TEXT NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS checkpoint_files (id INTEGER PRIMARY KEY AUTOINCREMENT, checkpoint_id TEXT NOT NULL, session_id TEXT NOT NULL, path TEXT NOT NULL, existed INTEGER NOT NULL, content BLOB NOT NULL, sha256 TEXT NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS task_items (id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, title TEXT NOT NULL, status TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS usage_events (id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, model TEXT NOT NULL, tokens INTEGER NOT NULL, created_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_checkpoints ON checkpoint_files(checkpoint_id);
            CREATE INDEX IF NOT EXISTS ix_session_messages ON session_messages(session_id, id);
            CREATE INDEX IF NOT EXISTS ix_task_items ON task_items(session_id, id);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static string Redact(string value)
    {
        return System.Text.RegularExpressions.Regex.Replace(value, "(?i)(api[_-]?key|token|password)\\s*[:=]\\s*[^\\s,;]+", "$1=[REDACTED]");
    }

    private static void ValidateTaskStatus(string status)
    {
        if (!new[] { "pending", "in_progress", "completed", "blocked" }.Contains(status, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Geçersiz görev durumu: " + status);
    }
}

public sealed record CheckpointSummary(string Id, DateTimeOffset CreatedAt, int FileCount);
public sealed record CheckpointDifference(string Path, bool ExistedAtCheckpoint, bool ExistsNow, string Status);
public sealed record AgentTaskItem(long Id, string Title, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record AuditEventItem(string EventType, string Detail, DateTimeOffset CreatedAt);
public sealed record UsageItem(string Model, int Tokens, DateTimeOffset CreatedAt);
