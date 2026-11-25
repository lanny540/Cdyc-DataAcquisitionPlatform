using DAP.Core.Domain.Entities;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Presentation.BlazorWeb.Security.Auditing;
using Microsoft.EntityFrameworkCore;

namespace DAP.Presentation.BlazorWeb.Security;

/// <summary>
/// 大屏导航枢纽页（根路由）跳转卡片的管理服务。
///
/// 读取入口 <see cref="GetVisibleCardsAsync"/> 供根路由页面渲染（只返回启用卡），
/// 其余变更为超管在「域与页面配置」页操作，全部写审计留痕。
/// 卡片数据独立于 domain_pages：枢纽页本身不参与域授权，对所有登录用户开放。
/// </summary>
public sealed class ScreenHubAdminService(
    DataAcquisitionPlatformDbContext db,
    AuditLogWriter audit,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider)
{
    /// <summary>
    /// 获取启用中的卡片，按显示顺序排列（根路由枢纽页渲染用）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>启用中的卡片列表。</returns>
    public async Task<IReadOnlyList<ScreenHubCardDto>> GetVisibleCardsAsync(
        CancellationToken cancellationToken = default)
    {
        return await db.ScreenHubCards
            .Where(card => card.IsEnabled)
            .OrderBy(card => card.SortOrder)
            .Select(card => ToDto(card))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 获取全部卡片（含停用），管理列表用。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全部卡片列表。</returns>
    public async Task<IReadOnlyList<ScreenHubCardDto>> GetAdminCardsAsync(
        CancellationToken cancellationToken = default)
    {
        return await db.ScreenHubCards
            .OrderBy(card => card.SortOrder)
            .Select(card => ToDto(card))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 新建卡片，排序追加到末尾。
    /// </summary>
    /// <param name="request">新建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果，失败时带中文原因。</returns>
    public async Task<AuthOperationResultDto> CreateCardAsync(
        ScreenHubCardUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        string? validationError = Validate(request, out string title, out string description, out string route,
            out string iconKey, out string accent);
        if (validationError is not null)
        {
            return new AuthOperationResultDto(false, validationError);
        }

        CurrentUser? actor = await currentUser.GetUserAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();

        int maxSortOrder = await db.ScreenHubCards.AnyAsync(cancellationToken)
            ? await db.ScreenHubCards.MaxAsync(card => card.SortOrder, cancellationToken)
            : 0;

        ScreenHubCard card = new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            Description = description,
            Route = route,
            IconKey = iconKey,
            Accent = accent,
            SortOrder = maxSortOrder + 1,
            IsEnabled = request.IsEnabled,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.ScreenHubCards.Add(card);
        await db.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(actor, "screen-hub-card.create", "screen-hub-card", card.Id.ToString(), title, true,
            new { card.Route, card.IconKey, card.Accent, card.SortOrder, card.IsEnabled });
        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 修改卡片内容。
    /// </summary>
    /// <param name="cardId">卡片标识。</param>
    /// <param name="request">修改请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果，失败时带中文原因。</returns>
    public async Task<AuthOperationResultDto> UpdateCardAsync(
        Guid cardId,
        ScreenHubCardUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        string? validationError = Validate(request, out string title, out string description, out string route,
            out string iconKey, out string accent);
        if (validationError is not null)
        {
            return new AuthOperationResultDto(false, validationError);
        }

        ScreenHubCard? card = await db.ScreenHubCards
            .FirstOrDefaultAsync(item => item.Id == cardId, cancellationToken);

        if (card is null)
        {
            return new AuthOperationResultDto(false, "大屏导航卡不存在，可能已被删除。");
        }

        CurrentUser? actor = await currentUser.GetUserAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();

        card.Title = title;
        card.Description = description;
        card.Route = route;
        card.IconKey = iconKey;
        card.Accent = accent;
        card.IsEnabled = request.IsEnabled;
        card.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(actor, "screen-hub-card.update", "screen-hub-card", card.Id.ToString(), title, true,
            new { card.Route, card.IconKey, card.Accent, card.SortOrder, card.IsEnabled });
        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 删除卡片。
    /// </summary>
    /// <param name="cardId">卡片标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果，失败时带中文原因。</returns>
    public async Task<AuthOperationResultDto> DeleteCardAsync(
        Guid cardId,
        CancellationToken cancellationToken = default)
    {
        ScreenHubCard? card = await db.ScreenHubCards
            .FirstOrDefaultAsync(item => item.Id == cardId, cancellationToken);

        if (card is null)
        {
            return new AuthOperationResultDto(false, "大屏导航卡不存在，可能已被删除。");
        }

        CurrentUser? actor = await currentUser.GetUserAsync(cancellationToken);

        db.ScreenHubCards.Remove(card);
        await db.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(actor, "screen-hub-card.delete", "screen-hub-card", cardId.ToString(), card.Title,
            true, new { card.Route });
        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 上移 / 下移卡片。先把全部卡片按当前显示顺序规范化编号，再与相邻卡交换位置，
    /// 避免历史数据 SortOrder 重复或断档时移动失效。
    /// </summary>
    /// <param name="cardId">卡片标识。</param>
    /// <param name="direction">移动方向：up 上移 / down 下移。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果，失败时带中文原因。</returns>
    public async Task<AuthOperationResultDto> MoveCardAsync(
        Guid cardId,
        string direction,
        CancellationToken cancellationToken = default)
    {
        bool moveUp = direction.Trim().Equals("up", StringComparison.OrdinalIgnoreCase);
        if (!moveUp && !direction.Trim().Equals("down", StringComparison.OrdinalIgnoreCase))
        {
            return new AuthOperationResultDto(false, "移动方向只支持 up（上移）或 down（下移）。");
        }

        List<ScreenHubCard> cards = await db.ScreenHubCards
            .OrderBy(card => card.SortOrder)
            .ToListAsync(cancellationToken);

        int index = cards.FindIndex(card => card.Id == cardId);
        if (index < 0)
        {
            return new AuthOperationResultDto(false, "大屏导航卡不存在，可能已被删除。");
        }

        int targetIndex = moveUp ? index - 1 : index + 1;
        if (targetIndex < 0 || targetIndex >= cards.Count)
        {
            return new AuthOperationResultDto(false, moveUp ? "卡片已经在第一位。" : "卡片已经在最后一位。");
        }

        CurrentUser? actor = await currentUser.GetUserAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();

        // 规范化编号后交换：即使库里 SortOrder 有重复或断档，移动结果仍然稳定。
        for (int i = 0; i < cards.Count; i++)
        {
            cards[i].SortOrder = i + 1;
        }

        (cards[index].SortOrder, cards[targetIndex].SortOrder) = (cards[targetIndex].SortOrder, cards[index].SortOrder);

        foreach (ScreenHubCard card in cards)
        {
            card.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(actor, "screen-hub-card.move", "screen-hub-card", cardId.ToString(),
            cards[index].Title, true, new { direction });
        return new AuthOperationResultDto(true, null);
    }

    /// <summary>
    /// 校验请求取值；通过时输出归一化字段，失败时返回第一条中文原因。
    /// </summary>
    private static string? Validate(
        ScreenHubCardUpsertRequest request,
        out string title,
        out string description,
        out string route,
        out string iconKey,
        out string accent)
    {
        List<string> errors =
        [
            ScreenHubCardRules.ValidateTitle(request.Title, out title),
            ScreenHubCardRules.ValidateDescription(request.Description, out description),
            ScreenHubCardRules.ValidateRoute(request.Route, out route),
            ScreenHubCardRules.ValidateIconKey(request.IconKey, out iconKey),
            ScreenHubCardRules.ValidateAccent(request.Accent, out accent)
        ];

        return errors.FirstOrDefault(error => error.Length > 0) is { } firstError ? firstError : null;
    }

    private static ScreenHubCardDto ToDto(ScreenHubCard card)
    {
        return new ScreenHubCardDto(
            card.Id,
            card.Title,
            card.Description,
            card.Route,
            card.IconKey,
            card.Accent,
            card.SortOrder,
            card.IsEnabled);
    }
}
