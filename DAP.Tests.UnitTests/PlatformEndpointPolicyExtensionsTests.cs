using DAP.Core.Shared.Catalog;
using DAP.Presentation.BlazorWeb.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace DAP.Tests.UnitTests;

/// <summary>
/// <see cref="DAP.Presentation.BlazorWeb.Security.PlatformEndpointPolicyExtensions"/> 策略名挂载测试。
///
/// 策略名集中收敛在这些扩展里，端点文件不再各自拼字符串；
/// 拼错策略名会让端点静默失去授权（默认策略兜底前先丢一层）。
/// 这里用记录式端点构建器钉死每个扩展挂载的策略名。
/// </summary>
public class PlatformEndpointPolicyExtensionsTests
{
    /// <summary>记录 Add 的约定，供断言检查最终写入端点的授权元数据。</summary>
    private sealed class RecordingEndpointConventionBuilder : IEndpointConventionBuilder
    {
        public List<Action<EndpointBuilder>> Conventions { get; } = [];

        public void Add(Action<EndpointBuilder> convention)
        {
            Conventions.Add(convention);
        }
    }

    private sealed class TestEndpointBuilder : EndpointBuilder
    {
        public override Endpoint Build()
        {
            throw new NotSupportedException("测试构建器不产出端点。");
        }
    }

    /// <summary>对扩展挂载的约定求值，返回写入端点的授权数据集合。</summary>
    private static IReadOnlyList<string> AppliedPolicies(
        Func<IEndpointConventionBuilder, IEndpointConventionBuilder> apply)
    {
        RecordingEndpointConventionBuilder recording = new();
        apply(recording);

        TestEndpointBuilder endpoint = new();
        foreach (Action<EndpointBuilder> convention in recording.Conventions)
            convention(endpoint);

        return endpoint.Metadata.OfType<IAuthorizeData>()
            .Select(data => data.Policy ?? string.Empty)
            .Where(policy => policy.Length > 0)
            .ToList();
    }

    [Fact]
    public void 钞纸域扩展_应挂载钞纸域策略()
    {
        IReadOnlyList<string> policies = AppliedPolicies(builder => builder.RequireBanknotePaperDomain());

        Assert.Equal([AuthorizationCatalog.PolicyNames.Domain(AuthorizationCatalog.BanknotePaperDomain)], policies);
    }

    [Fact]
    public void 配置等级扩展_应挂载配置策略()
    {
        IReadOnlyList<string> policies = AppliedPolicies(builder => builder.RequireConfigureAccess());

        Assert.Equal([AuthorizationCatalog.PolicyNames.Configure], policies);
    }

    [Fact]
    public void 平台管理扩展_应挂载平台管理策略()
    {
        IReadOnlyList<string> policies = AppliedPolicies(builder => builder.RequirePlatformAdmin());

        Assert.Equal([AuthorizationCatalog.PolicyNames.PlatformAdmin], policies);
    }

    [Fact]
    public void 权限点扩展_应挂载权限点策略名()
    {
        IReadOnlyList<string> policies = AppliedPolicies(
            builder => builder.RequirePermission(AuthorizationCatalog.ShiftManagePermission));

        Assert.Equal([AuthorizationCatalog.ShiftManagePermission], policies);
    }
}
