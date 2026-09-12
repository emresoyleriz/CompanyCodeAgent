using CompanyCodeAgent.Llm;
using CompanyCodeAgent.Domain;
using CompanyCodeAgent.Protocol;

namespace CompanyCodeAgent.Application;

public sealed class AgentOrchestrator(OpenAiCompatibleClient llm)
{
    private const string SystemPrompt = """
        Sen Company Code Agent'sın. Bir Visual Studio solution'ında güvenli kod yardımcısısın.
        Yalnızca sağlanan bağlama dayan. Araç çağırman gerektiğinde yalnızca şu JSON'u döndür:
        {"type":"tool_call","id":"benzersiz-id","tool":"ReadFile","arguments":{"path":"göreli/yol"}}.
        Geçerli araçlar ListFiles, SearchFiles, SearchText, ReadFile, ReadMultipleFiles, WriteFile, ApplyPatch, DeleteFile, RunCommand, BuildSolution, RunTests, GetGitDiff, GetGitStatus ve RestoreCheckpoint'tir.
        WriteFile, ApplyPatch, DeleteFile, RunCommand, BuildSolution, RunTests ve RestoreCheckpoint kullanıcı onayı gerektirir.
        Araç sonucu verildiğinde onu değerlendir ve gerekiyorsa bir sonraki aracı çağır. Gizli bilgileri cevapta yeniden üretme.
        """;

    public async IAsyncEnumerable<AgentEvent> RunAsync(AgentSettings settings, ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return AgentEvent.Status("Bağlam hazırlanıyor…");
        var messages = await BuildMessagesAsync(settings, request, cancellationToken);
        yield return AgentEvent.Status($"{settings.Model} ile bağlantı kuruluyor…");
        var fullResponse = new System.Text.StringBuilder();
        await foreach (var token in llm.StreamChatAsync(settings, messages, cancellationToken))
        {
            fullResponse.Append(token);
            yield return AgentEvent.Delta(token);
        }
        yield return AgentEvent.Complete(fullResponse.ToString());
    }

    public async IAsyncEnumerable<AgentEvent> RunWithToolsAsync(
        AgentSettings settings,
        ChatRequest request,
        Func<ToolCall, CancellationToken, Task<ToolResult>> executeAfterApproval,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var messages = (await BuildMessagesAsync(settings, request, cancellationToken)).ToList();
        for (var step = 1; step <= settings.MaxAgentSteps; step++)
        {
            yield return AgentEvent.Status($"Agent adımı {step}/{settings.MaxAgentSteps}…");
            var response = new System.Text.StringBuilder();
            await foreach (var token in llm.StreamChatAsync(settings, messages, cancellationToken))
            {
                response.Append(token);
                yield return AgentEvent.Delta(token);
            }
            var assistant = response.ToString();
            if (!ToolCallParser.TryParse(assistant, out var call) || call is null)
            {
                yield return AgentEvent.Complete(assistant);
                yield break;
            }
            yield return AgentEvent.Status($"Araç önerisi: {call.Kind}. {(call.RequiresApproval ? "Onay bekleniyor." : "Çalıştırılıyor.")}");
            var result = await executeAfterApproval(call, cancellationToken);
            yield return AgentEvent.Status(result.Success ? $"{call.Kind} tamamlandı." : $"{call.Kind} başarısız: {result.Output}");
            messages.Add(new ChatMessage("assistant", assistant));
            messages.Add(new ChatMessage("user", $"Araç sonucu ({call.Kind}): {(result.Success ? "başarılı" : "başarısız")}\n{SecretRedactor.Redact(result.Output)}"));
            if (!result.Success && !call.RequiresApproval)
            {
                yield return AgentEvent.Complete(result.Output);
                yield break;
            }
        }
        yield return AgentEvent.Error("Maksimum agent adımı aşıldı; işlem güvenlik nedeniyle durduruldu.");
    }

    private static async Task<IReadOnlyList<ChatMessage>> BuildMessagesAsync(AgentSettings settings, ChatRequest request, CancellationToken cancellationToken)
    {
        var context = request.Context;
        var contextText = $"""
            Solution: {context.SolutionPath ?? "bilinmiyor"}
            Aktif dosya: {context.ActiveDocumentPath ?? "yok"}
            Seçili kod:
            {(SecretRedactor.Redact(context.SelectedText) is { Length: > 0 } selected ? selected : "yok")}
            """;
        var rules = await new ProjectRulesLoader(new WorkspaceBoundary(settings.WorkspacePath)).LoadAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(rules)
            ? [new("system", SystemPrompt), new("system", contextText), new("user", request.UserMessage)]
            : [new("system", SystemPrompt), new("system", "Proje kuralları:\n" + SecretRedactor.Redact(rules)), new("system", contextText), new("user", request.UserMessage)];
    }
}
