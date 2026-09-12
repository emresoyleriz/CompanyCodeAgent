using System.Net;
using System.Text;
using CompanyCodeAgent.Llm;
using CompanyCodeAgent.Protocol;

namespace CompanyCodeAgent.UnitTests;

public sealed class OpenAiCompatibleClientTests
{
    [Fact]
    public async Task Reads_Model_List()
    {
        using var client = new HttpClient(new StubHandler(_ => Json("{\"data\":[{\"id\":\"company-model\",\"name\":\"Company Model\"}]}"))) { BaseAddress = new Uri("https://agent.test/") };
        var subject = new OpenAiCompatibleClient(client);
        var models = await subject.GetModelsAsync(Settings(), CancellationToken.None);
        var model = Assert.Single(models);
        Assert.Equal("company-model", model.Id);
        Assert.Equal("Company Model", model.DisplayName);
    }

    [Fact]
    public async Task Streams_Sse_Content_Deltas()
    {
        const string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"Merhaba \"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"dünya\"}}]}\n\ndata: [DONE]\n";
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") }));
        var subject = new OpenAiCompatibleClient(client);
        var values = new List<string>();
        await foreach (var delta in subject.StreamChatAsync(Settings(), [new ChatMessage("user", "selam")], CancellationToken.None)) values.Add(delta);
        Assert.Equal("Merhaba dünya", string.Concat(values));
    }

    private static AgentSettings Settings() => new(new Uri("https://agent.test/"), "secret", "company-model", Path.GetTempPath());
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(callback(request));
    }
}
