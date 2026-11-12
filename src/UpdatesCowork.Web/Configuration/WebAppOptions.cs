namespace UpdatesCowork.Web.Configuration;

/// <summary>エージェントの呼び出し方(<c>Agent:Mode</c>)。</summary>
internal enum AgentMode
{
    /// <summary>Foundry の Hosted Agent をトークン付きで呼ぶ。</summary>
    Foundry,

    /// <summary>ローカルで動かしたエージェント(http://localhost:8088)をトークンなしで呼ぶ。</summary>
    Local,

    /// <summary>録画した SSE を再生する(画面の確認用。エージェントもモデルも使わない)。</summary>
    Fake,
}

/// <summary>会話の続け方(<c>Agent:ConversationMode</c>)。</summary>
internal enum ConversationMode
{
    /// <summary>会話 API で会話を作り、<c>conversation</c> を指定する。</summary>
    Conversations,

    /// <summary>台帳に保存した最後の応答 ID を <c>previous_response_id</c> に入れる。</summary>
    PreviousResponseId,
}

/// <summary><c>Agent</c> 節。</summary>
internal sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public AgentMode Mode { get; set; } = AgentMode.Foundry;

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>要求の URL に付ける <c>api-version</c>。空なら付けない。</summary>
    public string ApiVersion { get; set; } = "v1";

    public ConversationMode ConversationMode { get; set; } = ConversationMode.Conversations;

    public bool SendUserIdentity { get; set; }

    public string VersionOverride { get; set; } = string.Empty;

    public int TurnTimeoutSeconds { get; set; } = 200;

    /// <summary>指定すると、エージェントから受け取った SSE をこのフォルダーに保存する(テストの録画用)。</summary>
    public string RecordTo { get; set; } = string.Empty;

    /// <summary><c>Mode=Fake</c> のときに再生する録画ファイル(コンテンツルートからの相対パスも可)。</summary>
    public string FakeRecording { get; set; } = "../../tests/recordings/fake-word.sse";

    /// <summary><c>Mode=Fake</c> のときのイベント間の待ち時間(ミリ秒)。</summary>
    public int FakeDelayMilliseconds { get; set; } = 60;
}

internal enum ArtifactReaderMode
{
    SessionFiles,
    Local,
}

/// <summary><c>Artifacts</c> 節(成果物の読み出し)。</summary>
internal sealed class ArtifactOptions
{
    public const string SectionName = "Artifacts";

    public ArtifactReaderMode Mode { get; set; } = ArtifactReaderMode.SessionFiles;

    public string LocalDir { get; set; } = string.Empty;
}

/// <summary><c>Web</c> 節(画面の設定)。</summary>
internal sealed class WebOptions
{
    public const string SectionName = "Web";

    public WebFeatureOptions Features { get; set; } = new();
}

internal sealed class WebFeatureOptions
{
    public bool Approvals { get; set; }

    public bool LearningMode { get; set; } = true;
}

internal enum AuthMode
{
    Anonymous,
    EntraId,
}

/// <summary><c>Auth</c> 節。</summary>
internal sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public AuthMode Mode { get; set; } = AuthMode.Anonymous;
}
