using CompanyCodeAgent.Application;

namespace CompanyCodeAgent.UnitTests;

public sealed class AgentStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "company-agent-storage-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Checkpoint_Restores_Previous_File_Content()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "Sample.cs");
        var database = Path.Combine(_root, "state.db");
        File.WriteAllText(file, "before");
        var storage = new AgentStorage(database);
        var checkpoint = storage.CreateCheckpoint("session", _root, [file]);
        File.WriteAllText(file, "after");
        var restored = storage.RestoreCheckpoint(checkpoint);
        Assert.Contains(file, restored);
        Assert.Equal("before", File.ReadAllText(file));
    }

    [Fact]
    public void Checkpoint_Removes_File_That_Did_Not_Exist()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "Created.cs");
        var storage = new AgentStorage(Path.Combine(_root, "state.db"));
        var checkpoint = storage.CreateCheckpoint("session", _root, [file]);
        File.WriteAllText(file, "new");
        storage.RestoreCheckpoint(checkpoint);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Session_Messages_Are_Persisted_In_Order()
    {
        Directory.CreateDirectory(_root);
        var storage = new AgentStorage(Path.Combine(_root, "state.db"));
        storage.SaveMessage("session", "user", "first");
        storage.SaveMessage("session", "assistant", "second");
        var messages = storage.ReadMessages("session");
        Assert.Collection(messages,
            first => { Assert.Equal("user", first.Role); Assert.Equal("first", first.Content); },
            second => { Assert.Equal("assistant", second.Role); Assert.Equal("second", second.Content); });
    }

    [Fact]
    public void Checkpoints_Can_Be_Listed_And_Compared()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "Sample.cs");
        File.WriteAllText(file, "before");
        var storage = new AgentStorage(Path.Combine(_root, "state.db"));
        var checkpoint = storage.CreateCheckpoint("session", _root, [file]);
        File.WriteAllText(file, "after");

        var checkpoints = storage.ListCheckpoints("session");
        var differences = storage.CompareCheckpoint(checkpoint);

        Assert.Contains(checkpoints, item => item.Id == checkpoint && item.FileCount == 1);
        Assert.Contains(differences, item => item.Path == file && item.Status == "modified");
    }

    [Fact]
    public void Tasks_Are_Scoped_To_Session_And_Track_Status()
    {
        Directory.CreateDirectory(_root);
        var storage = new AgentStorage(Path.Combine(_root, "state.db"));
        var task = storage.CreateTask("session-a", "Build solution");
        storage.UpdateTask("session-a", task, "in_progress");
        storage.CreateTask("session-b", "Other task");

        var tasks = storage.ListTasks("session-a");

        var item = Assert.Single(tasks);
        Assert.Equal("Build solution", item.Title);
        Assert.Equal("in_progress", item.Status);
    }

    [Fact]
    public void Audit_Events_Are_Scoped_And_Secrets_Are_Redacted()
    {
        Directory.CreateDirectory(_root);
        var storage = new AgentStorage(Path.Combine(_root, "state.db"));
        storage.WriteAudit("session-a", "tool_executed", "api_key=super-secret");
        storage.WriteAudit("session-b", "tool_failed", "other");

        var events = storage.ReadAuditEvents("session-a");

        var item = Assert.Single(events);
        Assert.Equal("tool_executed", item.EventType);
        Assert.Contains("[REDACTED]", item.Detail);
        Assert.DoesNotContain("super-secret", item.Detail);
    }

    [Fact]
    public void Usage_Is_Persisted_Per_Session()
    {
        Directory.CreateDirectory(_root);
        var storage = new AgentStorage(Path.Combine(_root, "state.db"));
        storage.RecordUsage("one", "model-a", 12);
        storage.RecordUsage("two", "model-b", 9);

        var usage = storage.ReadUsage("one");

        var item = Assert.Single(usage);
        Assert.Equal("model-a", item.Model);
        Assert.Equal(12, item.Tokens);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
