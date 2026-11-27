using DAP.Core.Shared.Catalog;
using DAP.Core.Shared.Contracts;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 验证采集服务器类型目录的解析、校验与默认值合并行为。
/// </summary>
public sealed class AcquisitionServerCatalogTests
{
    [Fact]
    public void Find_ReturnsDescriptorForRegisteredType()
    {
        AcquisitionServerTypeDescriptor? descriptor = AcquisitionServerCatalog.Find(AcquisitionServerCatalog.Historian);

        Assert.NotNull(descriptor);
        Assert.Equal("GE Historian", descriptor.DisplayName);
        Assert.True(descriptor.SupportsApiProbe);
    }

    [Fact]
    public void Find_IsCaseInsensitiveAndTrimsInput()
    {
        Assert.NotNull(AcquisitionServerCatalog.Find("  historian api  "));
    }

    [Fact]
    public void Find_ReturnsNullForUnknownType()
    {
        Assert.Null(AcquisitionServerCatalog.Find("Unknown Server"));
        Assert.Null(AcquisitionServerCatalog.Find(null));
        Assert.Null(AcquisitionServerCatalog.Find("   "));
    }

    [Fact]
    public void Descriptors_IncludeInfluxDbAndHttpApi()
    {
        Assert.NotNull(AcquisitionServerCatalog.Find(AcquisitionServerCatalog.InfluxDb));
        Assert.NotNull(AcquisitionServerCatalog.Find(AcquisitionServerCatalog.HttpApi));
        Assert.True(AcquisitionServerCatalog.IsKnownType(AcquisitionServerCatalog.Modbus));
    }

    [Fact]
    public void ValidateNode_RequiresIdentifierOnly()
    {
        // Historian 节点的标识是 Tag 名称，其余字段均有默认值，不应报必填错误。
        IReadOnlyCollection<string> errors = AcquisitionServerCatalog.ValidateNode(
            AcquisitionServerCatalog.Historian,
            "P1P2DCS-MS-/ASSETS/BROKE/LIS4108.MES",
            new Dictionary<string, string>());

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateNode_ReportsMissingIdentifier()
    {
        IReadOnlyCollection<string> errors = AcquisitionServerCatalog.ValidateNode(
            AcquisitionServerCatalog.Historian,
            "   ",
            new Dictionary<string, string>());

        Assert.Single(errors);
        Assert.Contains("Tag 名称", errors.First());
    }

    [Fact]
    public void ValidateNode_ReportsMissingRequiredNodeField()
    {
        // InfluxDB 节点除 Measurement 外还必须提供 Bucket。
        IReadOnlyCollection<string> errors = AcquisitionServerCatalog.ValidateNode(
            AcquisitionServerCatalog.InfluxDb,
            "steam_flow",
            new Dictionary<string, string>());

        Assert.Contains(errors, error => error.Contains("Bucket"));
    }

    [Fact]
    public void ValidateNode_PassesWhenRequiredNodeFieldProvided()
    {
        IReadOnlyCollection<string> errors = AcquisitionServerCatalog.ValidateNode(
            AcquisitionServerCatalog.InfluxDb,
            "steam_flow",
            new Dictionary<string, string> { ["bucket"] = "factory" });

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateServer_PassesWithoutCredentialFields()
    {
        // ClientId/ClientSecret 已从表单移除（GE 默认对由运行时兜底），
        // Historian 服务器级不再有必填凭据字段。
        IReadOnlyCollection<string> errors = AcquisitionServerCatalog.ValidateServer(
            AcquisitionServerCatalog.Historian,
            "http://10.9.3.54:8080",
            new Dictionary<string, string>());

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateServer_RejectsAddressContainingApiPath()
    {
        // 地址若填入接口路径，运行时会与接口路径重复拼接，这里应直接拦截。
        IReadOnlyCollection<string> errors = AcquisitionServerCatalog.ValidateServer(
            AcquisitionServerCatalog.Historian,
            "http://10.9.3.54:8080/historian-rest-api",
            new Dictionary<string, string> { ["clientId"] = "admin", ["clientSecret"] = "Pa55word" });

        Assert.Contains(errors, error => error.Contains("不要包含接口路径"));
    }

    [Fact]
    public void ValidateServer_AcceptsAddressWithSingleContextSegment()
    {
        IReadOnlyCollection<string> errors = AcquisitionServerCatalog.ValidateServer(
            AcquisitionServerCatalog.HttpApi,
            "http://api.local:8080/ge",
            new Dictionary<string, string>());

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateServer_RejectsUnsupportedScheme()
    {
        IReadOnlyCollection<string> errors = AcquisitionServerCatalog.ValidateServer(
            AcquisitionServerCatalog.HttpApi,
            "ftp://api.local:8080",
            new Dictionary<string, string>());

        Assert.Contains(errors, error => error.Contains("仅支持"));
    }

    [Fact]
    public void ValidateServer_RejectsUnknownType()
    {
        IReadOnlyCollection<string> errors = AcquisitionServerCatalog.ValidateServer(
            "Unsupported",
            "http://10.9.3.54:8080",
            new Dictionary<string, string>());

        Assert.Single(errors);
    }

    [Fact]
    public void MergeWithDefaults_SeedsDescriptorDefaults()
    {
        Dictionary<string, string> merged = AcquisitionServerCatalog.MergeWithDefaults(
            AcquisitionServerCatalog.Historian,
            new Dictionary<string, string>());

        // ClientId/ClientSecret 为可选字段：默认值播种 GE 默认对，密钥不预置。
        Assert.Equal(AcquisitionServerCatalog.HistorianDefaultClientId, merged["clientId"]);
        Assert.False(merged.ContainsKey("clientSecret"));

        // 表单精简：Token 与业务接口路径、Mock 开关不再进表单，由代理内置常量兜底。
        Assert.False(merged.ContainsKey("oauthTokenPath"));
        Assert.False(merged.ContainsKey("currentValuePath"));
        Assert.False(merged.ContainsKey("rawDataPathPrefix"));
        Assert.False(merged.ContainsKey("useMockResponses"));
    }

    [Fact]
    public void MergeWithDefaults_KeepsLegacyConfiguredPaths()
    {
        // 历史记录里存过接口路径的旧数据仍然透传，运行时按存储值优先。
        Dictionary<string, string> merged = AcquisitionServerCatalog.MergeWithDefaults(
            AcquisitionServerCatalog.Historian,
            new Dictionary<string, string>
            {
                ["currentValuePath"] = "/custom/value",
                ["oauthTokenPath"] = "/custom/token"
            });

        Assert.Equal("/custom/value", merged["currentValuePath"]);
        Assert.Equal("/custom/token", merged["oauthTokenPath"]);
    }

    [Fact]
    public void IsServerSecret_OnlyMatchesPasswordFields()
    {
        Assert.True(AcquisitionServerCatalog.IsServerSecret(
            AcquisitionServerCatalog.Historian, "oauthPassword"));
        Assert.True(AcquisitionServerCatalog.IsServerSecret(
            AcquisitionServerCatalog.Historian, "clientSecret"));
        Assert.False(AcquisitionServerCatalog.IsServerSecret(
            AcquisitionServerCatalog.Historian, "oauthUsername"));
        Assert.False(AcquisitionServerCatalog.IsServerSecret(
            AcquisitionServerCatalog.Historian, "clientId"));
    }

    [Fact]
    public void ResolveHistorianClientCredentials_KeepsLegacyAdminAsConfigured()
    {
        // 管理员提供的现场 Client 可能就叫 admin（如本项目现场 admin/Pa55word）：
        // 不做任何「旧默认值」特殊化，存储值原样使用。
        (string clientId, string clientSecret) = AcquisitionServerCatalog.ResolveHistorianClientCredentials(
            "admin", "Pa55word");

        Assert.Equal("admin", clientId);
        Assert.Equal("Pa55word", clientSecret);
    }

    [Fact]
    public void ResolveHistorianClientCredentials_FallsBackToDefaultsWhenBlank()
    {
        (string clientId, string clientSecret) = AcquisitionServerCatalog.ResolveHistorianClientCredentials(
            "  ", null);

        Assert.Equal(AcquisitionServerCatalog.HistorianDefaultClientId, clientId);
        Assert.Equal(AcquisitionServerCatalog.HistorianDefaultClientSecret, clientSecret);
    }

    [Fact]
    public void ResolveHistorianClientCredentials_KeepsCustomClient()
    {
        (string clientId, string clientSecret) = AcquisitionServerCatalog.ResolveHistorianClientCredentials(
            "custom-client", "custom-secret");

        Assert.Equal("custom-client", clientId);
        Assert.Equal("custom-secret", clientSecret);
    }

    [Fact]
    public void ResolveHistorianClientCredentials_CustomClient_BlankSecretFallsBackToDefault()
    {
        (string clientId, string clientSecret) = AcquisitionServerCatalog.ResolveHistorianClientCredentials(
            "custom-client", " ");

        Assert.Equal("custom-client", clientId);
        Assert.Equal(AcquisitionServerCatalog.HistorianDefaultClientSecret, clientSecret);
    }

    [Fact]
    public void NormalizeAddress_TrimsTrailingSlash()
    {
        Assert.Equal("http://10.9.3.54:8080",
            AcquisitionServerCatalog.NormalizeAddress("http://10.9.3.54:8080/"));
        Assert.Equal(string.Empty, AcquisitionServerCatalog.NormalizeAddress(null));
    }
}
