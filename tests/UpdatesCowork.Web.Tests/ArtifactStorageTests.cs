using System.Net;
using Microsoft.Extensions.Options;
using UpdatesCowork.Web.Artifacts;
using UpdatesCowork.Web.Configuration;
using UpdatesCowork.Web.Endpoints;
using UpdatesCowork.Web.Registry;
using Xunit;

namespace UpdatesCowork.Web.Tests;

public sealed class ArtifactStorageTests : IDisposable
{
    private const string ArtifactId = "art_01JCZ7A8B9C0D1E2F3G4H5J6K7";

    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "artifact-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("local", ArtifactId + "/report.docx", true)]
    [InlineData("local", "2026/11/11/" + ArtifactId + "/report.docx", true)]
    [InlineData("local", "2026/11/11/" + ArtifactId + "/report.docx\n", false)]
    [InlineData("local", "2026/11/11/" + ArtifactId + "/../report.docx", false)]
    [InlineData("local", ArtifactId + "/../secret.docx", false)]
    [InlineData("local", ArtifactId + "/..", false)]
    [InlineData("local", "../../etc/passwd", false)]
    [InlineData("local", "/" + ArtifactId + "/report.docx", false)]
    [InlineData("local", ArtifactId + "\\report.docx", false)]
    [InlineData("local", ArtifactId + "/報告書.docx", false)]
    [InlineData("blob", "2026/11/11/" + ArtifactId + "/deck.pptx", false)]
    [InlineData("blob", ArtifactId + "/deck.pptx", false)]
    [InlineData("session", "artifacts/" + ArtifactId + "/deck.pptx", true)]
    [InlineData("other", ArtifactId + "/deck.pptx", false)]
    [InlineData("local", "art_01JCZ7A8B9C0D1E2F3G4H5J6KU/report.docx", false)]
    public void LocationPattern(string storage, string location, bool expected)
    {
        Assert.Equal(expected, ArtifactLocationValidator.IsValid(storage, location));
    }

    [Fact]
    public async Task LocalReader_ReadsFileInsideRoot()
    {
        Directory.CreateDirectory(Path.Combine(_root, ArtifactId));
        await File.WriteAllBytesAsync(Path.Combine(_root, ArtifactId, "report.docx"), [1, 2, 3], TestContext.Current.CancellationToken);
        var reader = new LocalArtifactReader(_root);

        var stream = await reader.OpenReadAsync(Record("local", ArtifactId + "/report.docx"), TestContext.Current.CancellationToken);

        Assert.NotNull(stream);
        await using (stream)
        {
            Assert.Equal(3, stream.Length);
        }
    }

    [Theory]
    [InlineData("local", "../outside.docx")]
    [InlineData("local", ArtifactId + "/../../outside.docx")]
    [InlineData("blob", ArtifactId + "/report.docx")]
    [InlineData("local", ArtifactId + "/missing.docx")]
    public async Task LocalReader_RejectsTraversalAndOtherStorage(string storage, string location)
    {
        var root = Path.Combine(_root, "root");
        Directory.CreateDirectory(Path.Combine(root, ArtifactId));
        await File.WriteAllBytesAsync(Path.Combine(_root, "outside.docx"), [1], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(root, ArtifactId, "report.docx"), [1], TestContext.Current.CancellationToken);
        var reader = new LocalArtifactReader(root);

        var stream = await reader.OpenReadAsync(Record(storage, location), TestContext.Current.CancellationToken);

        Assert.Null(stream);
    }

    [Fact]
    public async Task SessionFilesReader_UsesOwnedSessionAndRelativeFilePath()
    {
        using var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3]),
        });
        using var http = new HttpClient(handler);
        var reader = SessionReader(http);

        var stream = await reader.OpenReadAsync(
            Record("session", "artifacts/" + ArtifactId + "/report.docx") with { SessionId = "session_test-1" },
            TestContext.Current.CancellationToken);

        Assert.NotNull(stream);
        await using (stream)
        {
            using var contents = new MemoryStream();
            await stream.CopyToAsync(contents, TestContext.Current.CancellationToken);
            Assert.Equal(new byte[] { 1, 2, 3 }, contents.ToArray());
        }

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            $"https://foundry.example/api/projects/test/agents/agent/endpoint/sessions/session_test-1/files/content?api-version=v1&path=artifacts%2F{ArtifactId}%2Freport.docx",
            request.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task SessionFilesReader_RejectsInvalidSessionIdWithoutRequest()
    {
        using var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = new HttpClient(handler);
        var reader = SessionReader(http);

        var stream = await reader.OpenReadAsync(
            Record("session", "artifacts/" + ArtifactId + "/report.docx") with { SessionId = "../other" },
            TestContext.Current.CancellationToken);

        Assert.Null(stream);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SessionFilesReader_DeletesSessionThroughAgentEndpoint()
    {
        using var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var http = new HttpClient(handler);
        var reader = SessionReader(http);

        await reader.DeleteSessionAsync("session_test-1", TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal(
            "https://foundry.example/api/projects/test/agents/agent/endpoint/sessions/session_test-1?api-version=v1",
            request.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task InMemoryRegistry_DeletingThreadAlsoDeletesItsArtifacts()
    {
        var registry = new InMemoryChatRegistry(TimeProvider.System);
        var thread = await registry.CreateThreadAsync("user-key", TestContext.Current.CancellationToken);
        var artifact = Record("session", "artifacts/" + ArtifactId + "/report.docx") with
        {
            SessionId = "session_test-1",
            ThreadId = thread.ThreadId,
        };
        await registry.AddArtifactAsync("user-key", artifact, TestContext.Current.CancellationToken);

        Assert.Equal(thread, await registry.DeleteThreadAsync("user-key", thread.ThreadId, TestContext.Current.CancellationToken));

        Assert.Null(await registry.GetThreadAsync("user-key", thread.ThreadId, TestContext.Current.CancellationToken));
        Assert.Null(await registry.GetArtifactAsync("user-key", ArtifactId, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("report.docx", "report.docx")]
    [InlineData("../../evil\".docx", "evil.docx")]
    [InlineData("報告書 2026.docx", "報告書 2026.docx")]
    [InlineData("...", ArtifactId + ".docx")]
    public void DownloadFileName_IsSanitized(string fileName, string expected)
    {
        Assert.Equal(expected, ArtifactEndpoints.GetDownloadFileName(Record("local", ArtifactId + "/x.docx") with { FileName = fileName }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static ArtifactRecord Record(string storage, string location) => new()
    {
        ArtifactId = ArtifactId,
        ThreadId = "th_01JCZ6V3S1Y4M5K8Q2W7E9R0TA",
        FileName = "report.docx",
        ContentType = "application/octet-stream",
        Kind = "docx",
        Storage = storage,
        Location = location,
    };

    private static SessionFilesArtifactReader SessionReader(HttpClient http) =>
        new(http, Options.Create(new AgentOptions
        {
            BaseUrl = "https://foundry.example/api/projects/test/agents/agent/endpoint/protocols/openai",
            ApiVersion = "v1",
        }));
}
