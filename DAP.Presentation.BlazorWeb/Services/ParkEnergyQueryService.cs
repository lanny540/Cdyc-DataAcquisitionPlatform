using DAP.Core.Domain.Entities;
using DAP.Core.Domain.Services;
using DAP.Core.Shared.Contracts;
using DAP.Infrastructure.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 定义园区能源板块对外提供的查询能力。
/// </summary>
public interface IParkEnergyQueryService
{
    /// <summary>
    /// 获取园区能源总览。
    /// </summary>
    /// <param name="period">统计期间，取值为 month 或 year。</param>
    /// <param name="demo">
    /// 是否返回示例数据。仅用于版式预览，返回值的数据状态会标记为 sample，
    /// 页面必须显式标注，不能与实测数据同一套样式。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>总览快照。</returns>
    Task<ParkEnergyOverviewDto> GetOverviewAsync(
        string period,
        bool demo = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取能源字典，供采集点打标时选择。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>字典清单。</returns>
    Task<EnergyDictionaryDto> GetDictionaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取能源计量点台账。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>计量点清单。</returns>
    Task<IReadOnlyList<EnergyMeterDto>> GetMetersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 新建或更新能源计量点。
    /// </summary>
    /// <param name="request">计量点信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>保存后的计量点。</returns>
    Task<EnergyMeterDto> UpsertMeterAsync(
        EnergyMeterUpsertRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除能源计量点。已有读数或下级表时拒绝，避免留下孤儿数据。
    /// </summary>
    /// <param name="id">计量点标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时返回 true。</returns>
    Task<bool> DeleteMeterAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 新建或更新能源组织节点。
    /// </summary>
    /// <param name="request">节点信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>保存后的节点。</returns>
    Task<EnergyOrgUnitDto> UpsertOrgUnitAsync(
        EnergyOrgUnitUpsertRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 修改介质折标煤系数。
    /// </summary>
    /// <param name="mediumId">介质标识。</param>
    /// <param name="coalFactor">折标煤系数，传空表示清除。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>保存后的字典项。</returns>
    Task<EnergyMediumDto> UpdateMediumFactorAsync(
        Guid mediumId,
        decimal? coalFactor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量写入读数。正式环境的能源平台按此接口推送。
    /// </summary>
    /// <param name="request">读数条目。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>写入结果。</returns>
    Task<IngestEnergyReadingsResultDto> IngestReadingsAsync(
        IngestEnergyReadingsRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 基于数据库的园区能源查询服务。
///
/// 页面不直接读表，全部经过本服务：口径（差分、折标、一次能源）只在一个地方实现，
/// 页面、报表、接口三条路径才不会各算一套、各自都「看起来对」。
/// </summary>
public sealed class ParkEnergyQueryService(
    DataAcquisitionPlatformDbContext dbContext,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<ParkEnergyQueryService> logger) : IParkEnergyQueryService
{
    /// <summary>
    /// 单次批量写入允许的最大条数。
    ///
    /// 外部平台可能一次推一整天的分钟级数据，几万条会让一个请求占住数据库连接几分钟；
    /// 超过上限直接拒绝，由接入方分批。
    /// </summary>
    private const int MaxIngestBatch = 5000;

    /// <summary>
    /// 拒绝原因最多回传的条数。
    /// </summary>
    private const int MaxIngestMessages = 20;

    /// <inheritdoc />
    public async Task<ParkEnergyOverviewDto> GetOverviewAsync(
        string period,
        bool demo = false,
        CancellationToken cancellationToken = default)
    {
        var normalized = string.Equals(period, "year", StringComparison.OrdinalIgnoreCase) ? "year" : "month";
        var offset = ResolveOffset();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var periodRange = ResolvePeriod(normalized, today);

        // 示例数据只在开发环境提供。生产环境即使手工拼 ?demo=true 也拿不到，
        // 否则一张带「示例」标记的截图流出后，没人会注意那行小字。
        var allowSample = environment.IsDevelopment() ||
                          configuration.GetValue("Energy:AllowSamplePreview", false);

        if (demo && allowSample)
        {
            return ParkEnergySampleProvider.Create(normalized, periodRange, offset);
        }

        if (demo)
        {
            return Empty(normalized, periodRange, "当前环境未开启示例数据预览。");
        }

        var meters = await LoadMetersAsync(null, cancellationToken);
        if (meters.Count == 0)
        {
            // 没有计量点等于还没有能源台账。这与「有表但没有读数」是两件事，
            // 页面上的提示不同：前者要去建台账，后者要去查采集链路。
            return Empty(normalized, periodRange, "尚未配置能源计量点，无法汇总。");
        }

        var current = await AggregateAsync(meters, periodRange, offset, cancellationToken);
        var previousRange = ResolvePreviousPeriod(normalized, periodRange, today);
        var previous = await AggregateAsync(meters, previousRange, offset, cancellationToken);

        if (!current.HasReadings)
        {
            return Empty(
                normalized,
                periodRange,
                "统计期间内没有能源读数。请确认正式环境的能源平台已按接口推送数据，或通过批量导入补录。");
        }

        var loadDay = current.DataThrough is { } through
            ? DateOnly.FromDateTime(through.ToOffset(offset).Date)
            : today;

        var loadMeters = meters.Where(meter => string.Equals(meter.MediumCode, "ELEC", StringComparison.Ordinal)).ToArray();
        var dayLoad = loadMeters.Length == 0
            ? new decimal?[24]
            : await BuildLoadAsync(loadMeters, loadDay, offset, cancellationToken);

        var declared = ResolveDeclaredDemand();

        return new ParkEnergyOverviewDto(
            normalized,
            periodRange.Start.Year == periodRange.EndInclusive.Year && normalized == "year"
                ? $"{periodRange.Start:yyyy} 年"
                : $"{periodRange.Start:yyyy 年 M 月}",
            periodRange.Start,
            periodRange.EndInclusive,
            DateTimeOffset.Now.ToOffset(offset),
            current.DataThrough,
            ParkEnergyDataStatus.Live,
            current.Warnings,
            BuildKpis(current, previous, normalized),
            Enumerable.Range(0, 24)
                .Where(hour => dayLoad[hour].HasValue)
                .Select(hour => new ParkEnergyLoadPointDto(hour, dayLoad[hour]!.Value))
                .ToArray(),
            declared,
            BuildPrimaryShares(current),
            BuildSecondaryShares(current),
            BuildOrgRows(current, previous));
    }

    /// <inheritdoc />
    public async Task<EnergyDictionaryDto> GetDictionaryAsync(CancellationToken cancellationToken = default)
    {
        var media = await dbContext.EnergyMedia.AsNoTracking()
            .Where(medium => medium.IsEnabled)
            .OrderBy(medium => medium.SortOrder)
            .Select(medium => new
            {
                medium.Id,
                medium.Code,
                medium.Name,
                medium.Unit,
                medium.CoalFactor,
                medium.IsPrimaryEnergy
            })
            .ToListAsync(cancellationToken);

        var usages = await dbContext.EnergyUsageTypes.AsNoTracking()
            .Where(usage => usage.IsEnabled)
            .OrderBy(usage => usage.SortOrder)
            .Select(usage => new { usage.Id, usage.Code, usage.Name })
            .ToListAsync(cancellationToken);

        var systems = await dbContext.EnergySystems.AsNoTracking()
            .Where(system => system.IsEnabled)
            .OrderBy(system => system.SortOrder)
            .Select(system => new { system.Id, system.Code, system.Name })
            .ToListAsync(cancellationToken);

        var orgUnits = await dbContext.EnergyOrgUnits.AsNoTracking()
            .Where(unit => unit.IsEnabled)
            .OrderBy(unit => unit.SortOrder)
            .Select(unit => new { unit.Id, unit.Code, unit.Name, unit.Level, unit.ParentId })
            .ToListAsync(cancellationToken);

        // 层级深度在内存里算：只有园区、部门、工序三级，没必要为它写递归 CTE，
        // 但必须算——界面上把工序和部门平铺成一个下拉框，打标时必然选错层级。
        var parents = orgUnits.ToDictionary(unit => unit.Id, unit => unit.ParentId);
        var depthOf = new Dictionary<Guid, int>();

        int ResolveDepth(Guid id)
        {
            if (depthOf.TryGetValue(id, out var cached))
            {
                return cached;
            }

            var depth = 0;
            var cursor = id;
            var guard = 0;

            while (parents.TryGetValue(cursor, out var parent) && parent.HasValue && guard++ < 8)
            {
                depth++;
                cursor = parent.Value;
            }

            depthOf[id] = depth;
            return depth;
        }

        return new EnergyDictionaryDto(
            media.Select(item => new EnergyMediumDto(
                item.Id,
                item.Code,
                item.Name,
                item.Unit,
                item.IsPrimaryEnergy,
                item.CoalFactor))
                .ToArray(),
            usages.Select(item => new EnergyDictionaryItemDto(item.Id, item.Code, item.Name, null)).ToArray(),
            systems.Select(item => new EnergyDictionaryItemDto(item.Id, item.Code, item.Name, null)).ToArray(),
            orgUnits.Select(item => new EnergyOrgUnitDto(
                item.Id, item.Code, item.Name, item.Level, item.ParentId, ResolveDepth(item.Id))).ToArray());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EnergyMeterDto>> GetMetersAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.EnergyMeters.AsNoTracking()
            .OrderBy(meter => meter.Code)
            .Select(meter => new EnergyMeterDto(
                meter.Id,
                meter.Code,
                meter.Name,
                meter.MediumId,
                meter.Medium!.Code,
                meter.Medium.Name,
                meter.Medium.Unit,
                meter.OrgUnitId,
                meter.OrgUnit!.Name,
                meter.UsageTypeId,
                meter.UsageType!.Name,
                meter.SystemTypeId,
                meter.SystemType!.Name,
                meter.IsCumulative,
                meter.IsEnabled,
                meter.Ratio,
                meter.ParentMeter == null ? null : meter.ParentMeter.Code,
                meter.Remark))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EnergyMeterDto> UpsertMeterAsync(
        EnergyMeterUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var code = request.Code.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("计量点编码不能为空。");
        }

        if (request.Ratio <= 0)
        {
            throw new ArgumentException("倍率必须大于 0，否则读数会被算成 0。");
        }

        var currentId = request.Id ?? Guid.Empty;
        var duplicated = await dbContext.EnergyMeters
            .AnyAsync(meter => meter.Code == code && meter.Id != currentId, cancellationToken);

        if (duplicated)
        {
            throw new ArgumentException($"计量点编码 {code} 已存在。");
        }

        if (request.ParentMeterId == currentId && request.Id.HasValue)
        {
            throw new ArgumentException("上级计量点不能是自身。");
        }

        if (request.ParentMeterId is { } parentId &&
            !await dbContext.EnergyMeters.AnyAsync(meter => meter.Id == parentId, cancellationToken))
        {
            throw new ArgumentException("上级计量点不存在。");
        }

        // 四个维度逐个校验存在性。交给外键约束也能拦住，但报出来的是英文约束名，
        // 用户看不出「是哪个下拉框没选」。
        if (!await dbContext.EnergyMedia.AnyAsync(item => item.Id == request.MediumId, cancellationToken))
        {
            throw new ArgumentException("介质不存在。");
        }

        if (!await dbContext.EnergyOrgUnits.AnyAsync(item => item.Id == request.OrgUnitId, cancellationToken))
        {
            throw new ArgumentException("组织节点不存在。");
        }

        if (!await dbContext.EnergyUsageTypes.AnyAsync(item => item.Id == request.UsageTypeId, cancellationToken))
        {
            throw new ArgumentException("用能用途不存在。");
        }

        if (!await dbContext.EnergySystems.AnyAsync(item => item.Id == request.SystemTypeId, cancellationToken))
        {
            throw new ArgumentException("设备系统不存在。");
        }

        EnergyMeter entity;

        if (request.Id is { } id)
        {
            entity = await dbContext.EnergyMeters.FirstOrDefaultAsync(meter => meter.Id == id, cancellationToken)
                     ?? throw new ArgumentException("计量点不存在，可能已被其他人删除。");
        }
        else
        {
            entity = new EnergyMeter { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
            dbContext.EnergyMeters.Add(entity);
        }

        entity.Code = code;
        entity.Name = string.IsNullOrWhiteSpace(request.Name) ? code : request.Name.Trim();
        entity.MediumId = request.MediumId;
        entity.OrgUnitId = request.OrgUnitId;
        entity.UsageTypeId = request.UsageTypeId;
        entity.SystemTypeId = request.SystemTypeId;
        entity.ParentMeterId = request.ParentMeterId;
        entity.IsCumulative = request.IsCumulative;
        entity.Ratio = request.Ratio;
        entity.IsEnabled = request.IsEnabled;
        entity.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // 应用层查重挡不住并发：两个人同时建同一个编码，都会通过 AnyAsync，
            // 最终由唯一索引拦下。这里翻译成中文，否则用户看到的是一串约束名。
            throw new ArgumentException("保存失败：计量点编码可能已被占用，或引用的字典项已被删除。", ex);
        }

        return await dbContext.EnergyMeters.AsNoTracking()
            .Where(meter => meter.Id == entity.Id)
            .Select(meter => new EnergyMeterDto(
                meter.Id,
                meter.Code,
                meter.Name,
                meter.MediumId,
                meter.Medium!.Code,
                meter.Medium.Name,
                meter.Medium.Unit,
                meter.OrgUnitId,
                meter.OrgUnit!.Name,
                meter.UsageTypeId,
                meter.UsageType!.Name,
                meter.SystemTypeId,
                meter.SystemType!.Name,
                meter.IsCumulative,
                meter.IsEnabled,
                meter.Ratio,
                meter.ParentMeter == null ? null : meter.ParentMeter.Code,
                meter.Remark))
            .SingleAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteMeterAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var meter = await dbContext.EnergyMeters
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (meter is null)
        {
            return false;
        }

        // 有读数就不能删：历史报表按 meter_id 聚合，删掉表之后这些读数
        // 会变成孤儿，既不进任何汇总，也查不到出处。正确做法是停用。
        if (await dbContext.EnergyReadings.AnyAsync(reading => reading.MeterId == id, cancellationToken))
        {
            throw new InvalidOperationException("该计量点已有读数，删除会让历史数据失去归属；请改为停用。");
        }

        if (await dbContext.EnergyMeters.AnyAsync(item => item.ParentMeterId == id, cancellationToken))
        {
            throw new InvalidOperationException("该计量点下还有下级表，请先调整下级表的上级关系。");
        }

        dbContext.EnergyMeters.Remove(meter);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<EnergyOrgUnitDto> UpsertOrgUnitAsync(
        EnergyOrgUnitUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var code = request.Code.Trim();
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("节点编码与名称都不能为空。");
        }

        var level = request.Level switch
        {
            EnergyOrgUnit.ParkLevel => EnergyOrgUnit.ParkLevel,
            EnergyOrgUnit.DepartmentLevel => EnergyOrgUnit.DepartmentLevel,
            EnergyOrgUnit.ProcessLevel => EnergyOrgUnit.ProcessLevel,
            _ => throw new ArgumentException("节点层级只能是园区、部门或工序。")
        };

        // 园区必须无上级，其余必须有上级。少了这条校验，上卷汇总会悄悄漏掉整棵子树。
        if (level == EnergyOrgUnit.ParkLevel && request.ParentId is not null)
        {
            throw new ArgumentException("园区级节点不能有上级。");
        }

        if (level != EnergyOrgUnit.ParkLevel && request.ParentId is null)
        {
            throw new ArgumentException("部门与工序级节点必须选择上级节点。");
        }

        var currentId = request.Id ?? Guid.Empty;
        if (await dbContext.EnergyOrgUnits.AnyAsync(
                unit => unit.Code == code && unit.Id != currentId, cancellationToken))
        {
            throw new ArgumentException($"节点编码 {code} 已存在。");
        }

        if (request.ParentId is { } parentId)
        {
            var parent = await dbContext.EnergyOrgUnits.AsNoTracking()
                             .FirstOrDefaultAsync(unit => unit.Id == parentId, cancellationToken)
                         ?? throw new ArgumentException("上级节点不存在。");

            var expected = level switch
            {
                EnergyOrgUnit.DepartmentLevel => EnergyOrgUnit.ParkLevel,
                EnergyOrgUnit.ProcessLevel => EnergyOrgUnit.DepartmentLevel,
                _ => null
            };

            if (expected is not null && parent.Level != expected)
            {
                throw new ArgumentException(
                    level == EnergyOrgUnit.DepartmentLevel
                        ? "部门级节点的上级必须是园区。"
                        : "工序级节点的上级必须是部门。");
            }

            // 防成环：把上级设成自己的后代，整棵树从根上就断了，而且不会有任何报错。
            var cursor = parentId;
            var guard = 0;

            while (guard++ < 16)
            {
                if (cursor == currentId && request.Id.HasValue)
                {
                    throw new ArgumentException("上级节点不能是自己或自己的下级。");
                }

                var next = await dbContext.EnergyOrgUnits.AsNoTracking()
                    .Where(unit => unit.Id == cursor)
                    .Select(unit => unit.ParentId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (next is null)
                {
                    break;
                }

                cursor = next.Value;
            }
        }

        EnergyOrgUnit entity;

        if (request.Id is { } id)
        {
            entity = await dbContext.EnergyOrgUnits.FirstOrDefaultAsync(unit => unit.Id == id, cancellationToken)
                     ?? throw new ArgumentException("节点不存在，可能已被其他人删除。");
        }
        else
        {
            entity = new EnergyOrgUnit { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
            dbContext.EnergyOrgUnits.Add(entity);
        }

        entity.Code = code;
        entity.Name = name;
        entity.Level = level;
        entity.ParentId = request.ParentId;
        entity.IsEnabled = true;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new ArgumentException("保存失败：节点编码可能已被占用，或上级节点已被删除。", ex);
        }

        var depth = 0;
        var walk = entity.ParentId;

        while (walk is { } ancestorId && depth < 8)
        {
            depth++;
            walk = await dbContext.EnergyOrgUnits.AsNoTracking()
                .Where(unit => unit.Id == ancestorId)
                .Select(unit => unit.ParentId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new EnergyOrgUnitDto(entity.Id, entity.Code, entity.Name, entity.Level, entity.ParentId, depth);
    }

    /// <inheritdoc />
    public async Task<EnergyMediumDto> UpdateMediumFactorAsync(
        Guid mediumId,
        decimal? coalFactor,
        CancellationToken cancellationToken = default)
    {
        var medium = await dbContext.EnergyMedia
                         .FirstOrDefaultAsync(item => item.Id == mediumId, cancellationToken)
                     ?? throw new ArgumentException("介质不存在。");

        if (coalFactor is < 0)
        {
            throw new ArgumentException("折标煤系数不能为负数。");
        }

        if (!medium.IsPrimaryEnergy && coalFactor.HasValue)
        {
            // 二次能源的折标煤系数没有意义：它的能量已经算在电或蒸汽里了，
            // 再配一个系数只会让人以为它进了综合能耗。
            throw new ArgumentException($"{medium.Name}不是一次能源，不使用折标煤系数；它的折算电耗系数属于另一个口径。");
        }

        medium.CoalFactor = coalFactor;
        medium.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new EnergyMediumDto(
            medium.Id,
            medium.Code,
            medium.Name,
            medium.Unit,
            medium.IsPrimaryEnergy,
            medium.CoalFactor);
    }

    /// <inheritdoc />
    public async Task<IngestEnergyReadingsResultDto> IngestReadingsAsync(
        IngestEnergyReadingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        var rejected = 0;

        if (request.Readings.Count == 0)
        {
            return new IngestEnergyReadingsResultDto(0, 0, 0, messages);
        }

        if (request.Readings.Count > MaxIngestBatch)
        {
            throw new ArgumentException($"单次最多写入 {MaxIngestBatch} 条读数，请分批推送。");
        }

        var codes = request.Readings
            .Select(item => item.MeterCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var meterIds = await dbContext.EnergyMeters.AsNoTracking()
            .Where(meter => codes.Contains(meter.Code))
            .Select(meter => new { meter.Id, meter.Code })
            .ToDictionaryAsync(meter => meter.Code, meter => meter.Id, StringComparer.Ordinal, cancellationToken);

        var accepted = 0;
        var updated = 0;

        foreach (var item in request.Readings)
        {
            if (string.IsNullOrWhiteSpace(item.MeterCode) || !meterIds.TryGetValue(item.MeterCode, out var meterId))
            {
                rejected++;
                if (messages.Count < MaxIngestMessages)
                {
                    messages.Add($"计量点编码 {item.MeterCode} 不存在，已跳过。");
                }

                continue;
            }

            if (item.Value < 0)
            {
                rejected++;
                if (messages.Count < MaxIngestMessages)
                {
                    messages.Add($"计量点 {item.MeterCode} 在 {item.ReadAt:yyyy-MM-dd HH:mm} 的读数为负值，已拒绝。");
                }

                continue;
            }

            var readAt = item.ReadAt.ToUniversalTime();
            var source = string.IsNullOrWhiteSpace(item.Source) ? EnergyReading.CollectionSource : item.Source;

            var existing = await dbContext.EnergyReadings
                .FirstOrDefaultAsync(
                    reading => reading.MeterId == meterId && reading.ReadAt == readAt,
                    cancellationToken);

            if (existing is null)
            {
                dbContext.EnergyReadings.Add(new EnergyReading
                {
                    Id = Guid.NewGuid(),
                    MeterId = meterId,
                    ReadAt = readAt,
                    Value = item.Value,
                    Source = source,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                accepted++;
            }
            else
            {
                // 同一抄见时刻重复推送按覆盖处理，而不是再插一条：
                // 重复行会让累计表的差分量凭空翻倍，而报表上完全看不出异常。
                existing.Value = item.Value;
                existing.Source = source;
                updated++;
            }
        }

        if (accepted + updated > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "能源读数写入：新增 {Accepted} 条，覆盖 {Updated} 条，拒绝 {Rejected} 条。",
            accepted,
            updated,
            rejected);

        return new IngestEnergyReadingsResultDto(accepted, updated, rejected, messages);
    }

    private async Task<List<ParkEnergyMeterInput>> LoadMetersAsync(
        Guid? orgUnitId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.EnergyMeters.AsNoTracking().Where(meter => meter.IsEnabled);
        if (orgUnitId.HasValue)
        {
            query = query.Where(meter => meter.OrgUnitId == orgUnitId.Value);
        }

        var rows = await query
            .Select(meter => new
            {
                meter.Id,
                meter.Code,
                meter.Name,
                meter.IsCumulative,
                meter.Ratio,
                meter.OrgUnitId,
                OrgUnitName = meter.OrgUnit!.Name,
                MediumCode = meter.Medium!.Code,
                MediumName = meter.Medium.Name,
                MediumUnit = meter.Medium.Unit,
                meter.Medium.IsPrimaryEnergy,
                meter.Medium.CoalFactor
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new ParkEnergyMeterInput(
            row.Id,
            row.Code,
            row.Name,
            row.MediumCode,
            row.MediumName,
            row.MediumUnit,
            row.IsPrimaryEnergy,
            row.CoalFactor,
            row.IsCumulative,
            row.Ratio,
            row.OrgUnitId,
            row.OrgUnitName)).ToList();
    }

    private async Task<ParkEnergyAggregate> AggregateAsync(
        IReadOnlyCollection<ParkEnergyMeterInput> meters,
        ParkEnergyPeriod period,
        TimeSpan offset,
        CancellationToken cancellationToken)
    {
        var start = period.StartInstant(offset).ToUniversalTime();
        var end = period.EndInstant(offset).ToUniversalTime();

        var readings = await LoadReadingsAsync(meters, start, end, cancellationToken);

        return ParkEnergyAggregator.Aggregate(meters, readings, period, offset);
    }

    private async Task<decimal?[]> BuildLoadAsync(
        IReadOnlyCollection<ParkEnergyMeterInput> meters,
        DateOnly day,
        TimeSpan offset,
        CancellationToken cancellationToken)
    {
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), offset).ToUniversalTime();
        var end = start.AddDays(1);

        // 首小时需要前一小时的最后一条读数来定区间，因此窗口向前多取一天。
        var readings = await LoadReadingsAsync(meters, start, end, cancellationToken, start.AddDays(-1));

        return ParkEnergyAggregator.BuildHourlyLoad(meters, readings, day, offset);
    }

    private async Task<IReadOnlyCollection<ParkEnergyReadingInput>> LoadReadingsAsync(
        IReadOnlyCollection<ParkEnergyMeterInput> meters,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken,
        DateTimeOffset? windowStart = null)
    {
        var ids = meters.Select(meter => meter.Id).ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var result = new List<ParkEnergyReadingInput>();

        // 负荷曲线要从当天零点算起，零点那一段的用量依赖前一天最后一条读数，
        // 因此窗口可以前移；期间汇总则不需要，累计表已单独取基线。
        var queryFrom = windowStart ?? start;

        var inPeriod = await dbContext.EnergyReadings.AsNoTracking()
            .Where(reading => ids.Contains(reading.MeterId) &&
                              reading.ReadAt >= queryFrom && reading.ReadAt < end)
            .Select(reading => new { reading.MeterId, reading.ReadAt, reading.Value })
            .ToListAsync(cancellationToken);

        result.AddRange(inPeriod.Select(row => new ParkEnergyReadingInput(row.MeterId, row.ReadAt, row.Value)));

        if (windowStart.HasValue)
        {
            return result;
        }

        // 累计表的用量要靠期间起点之前的最后一次抄见做基线，否则每月首日的报表
        // 会把整月前半段的用量整段丢掉。这里先按表取最大抄见时刻，再取回对应记录。
        var lastReads = await dbContext.EnergyReadings.AsNoTracking()
            .Where(reading => ids.Contains(reading.MeterId) && reading.ReadAt < start)
            .GroupBy(reading => reading.MeterId)
            .Select(group => new { MeterId = group.Key, ReadAt = group.Max(reading => reading.ReadAt) })
            .ToListAsync(cancellationToken);

        if (lastReads.Count == 0)
        {
            return result;
        }

        // 只取「每块表最后一次抄见」对应的那几条。早先的写法是把 start 之前的
        // 全部历史读回来再在内存里筛，运行一年之后这个查询会开始明显变慢。
        var baselineTimes = lastReads.Select(item => item.ReadAt).Distinct().ToArray();

        var baselineRows = await dbContext.EnergyReadings.AsNoTracking()
            .Where(reading => ids.Contains(reading.MeterId) && baselineTimes.Contains(reading.ReadAt))
            .Select(reading => new { reading.MeterId, reading.ReadAt, reading.Value })
            .ToListAsync(cancellationToken);

        var wanted = lastReads
            .Select(item => (item.MeterId, item.ReadAt))
            .ToHashSet();

        result.AddRange(baselineRows
            .Where(row => wanted.Contains((row.MeterId, row.ReadAt)))
            .Select(row => new ParkEnergyReadingInput(row.MeterId, row.ReadAt, row.Value)));

        return result;
    }

    private TimeSpan ResolveOffset()
    {
        var hours = configuration.GetValue("Energy:UtcOffsetHours", 8d);
        return TimeSpan.FromHours(hours);
    }

    private decimal? ResolveDeclaredDemand()
    {
        var value = configuration.GetValue<decimal?>("Energy:DeclaredDemandKw");
        return value is > 0 ? value : null;
    }

    private static ParkEnergyPeriod ResolvePeriod(string period, DateOnly today)
    {
        if (string.Equals(period, "year", StringComparison.Ordinal))
        {
            return new ParkEnergyPeriod(new DateOnly(today.Year, 1, 1), today);
        }

        var start = new DateOnly(today.Year, today.Month, 1);
        return new ParkEnergyPeriod(start, today);
    }

    private static ParkEnergyPeriod ResolvePreviousPeriod(string period, ParkEnergyPeriod current, DateOnly today)
    {
        var length = current.EndInclusive.DayNumber - current.Start.DayNumber;
        var start = string.Equals(period, "year", StringComparison.Ordinal)
            ? current.Start.AddYears(-1)
            : current.Start.AddMonths(-1);

        var end = start.AddDays(length);
        var maxEnd = start.AddMonths(1).AddDays(-1);
        if (string.Equals(period, "month", StringComparison.Ordinal) && end > maxEnd)
        {
            end = maxEnd;
        }

        return new ParkEnergyPeriod(start, end);
    }

    private ParkEnergyOverviewDto Empty(string period, ParkEnergyPeriod range, string message) =>
        new(
            period,
            period == "year" ? $"{range.Start:yyyy} 年" : $"{range.Start:yyyy 年 M 月}",
            range.Start,
            range.EndInclusive,
            DateTimeOffset.Now.ToOffset(ResolveOffset()),
            null,
            ParkEnergyDataStatus.Empty,
            [message],
            [],
            [],
            ResolveDeclaredDemand(),
            [],
            [],
            []);

    private static IReadOnlyList<ParkEnergyKpiDto> BuildKpis(
        ParkEnergyAggregate current,
        ParkEnergyAggregate previous,
        string period)
    {
        var suffix = period == "year" ? "同比" : "环比";
        var totalDelta = BuildDelta(current.TotalTce, previous.TotalTce, suffix);

        var kpis = new List<ParkEnergyKpiDto>
        {
            new(
                "totalTce",
                "综合能耗",
                FormatNumber(current.TotalTce),
                "tce",
                totalDelta,
                // 没有可比基数时不给判断：否则「上期无数据」会配上一抹红色，
                // 看起来像本月严重恶化。
                totalDelta is null || previous.TotalTce <= 0 ? null : current.TotalTce <= previous.TotalTce,
                "电、水、蒸汽折标煤合计，只汇一次能源")
        };

        var primary = current.Mediums.Where(usage => usage.IsPrimaryEnergy).ToArray();
        var total = current.TotalTce > 0 ? current.TotalTce : 0m;

        foreach (var usage in primary)
        {
            var before = previous.Mediums
                .FirstOrDefault(item => string.Equals(item.Code, usage.Code, StringComparison.Ordinal))
                ?.Quantity ?? 0m;

            var shareText = total > 0 && usage.CoalEquivalentTce.HasValue
                ? $"折标 {usage.CoalEquivalentTce:0.#} tce，占 {usage.CoalEquivalentTce.Value / total * 100m:0.#}%"
                : $"折标系数待配置，共 {usage.MeterCount} 块表";

            var delta = BuildDelta(usage.Quantity, before, suffix);

            kpis.Add(new ParkEnergyKpiDto(
                usage.Code.ToLowerInvariant(),
                KpiLabel(usage.Code, usage.Name),
                FormatNumber(usage.Quantity),
                usage.Unit,
                delta,
                delta is null || before <= 0 ? null : usage.Quantity <= before,
                shareText));
        }

        return kpis;
    }

    /// <summary>
    /// 指标卡标题。不能简单拼「{介质名}量」——「水量」不是中文说法，
    /// 而「电」拼出来是「电量」，与业务习惯的「总用电量」也不一致。
    /// </summary>
    private static string KpiLabel(string code, string name) => code switch
    {
        "ELEC" => "总用电量",
        "WATER" => "用水量",
        "STEAM" => "蒸汽量",
        _ => $"{name}用量"
    };

    private static IReadOnlyList<ParkEnergyShareDto> BuildPrimaryShares(ParkEnergyAggregate current)
    {
        var primary = current.Mediums
            .Where(usage => usage.IsPrimaryEnergy)
            .OrderByDescending(usage => usage.CoalEquivalentTce ?? -1m)
            .ToArray();

        var total = primary.Sum(usage => usage.CoalEquivalentTce ?? 0m);

        return primary.Select(usage =>
        {
            var share = total > 0 && usage.CoalEquivalentTce.HasValue
                ? Math.Round(usage.CoalEquivalentTce.Value / total, 4)
                : (decimal?)null;

            return new ParkEnergyShareDto(
                usage.Name,
                usage.CoalEquivalentTce is { } coal ? $"{coal:0.#} tce" : $"{FormatNumber(usage.Quantity)} {usage.Unit}",
                share,
                share is { } value ? $"{value * 100m:0.#}%" : "待配置折标系数");
        }).ToArray();
    }

    private static IReadOnlyList<ParkEnergyShareDto> BuildSecondaryShares(ParkEnergyAggregate current)
    {
        // 二次能源的占比相对总用电量，而不是综合能耗——分子分母口径不同不能混。
        // 折算电耗需要单独的系数（每 m³ 压缩空气折多少 kWh），字典里还没有，
        // 因此这里只给实物量，不编一个占比出来。
        return current.Mediums
            .Where(usage => !usage.IsPrimaryEnergy)
            .OrderByDescending(usage => usage.Quantity)
            .Select(usage => new ParkEnergyShareDto(
                usage.Name,
                $"{FormatNumber(usage.Quantity)} {usage.Unit}",
                null,
                "待配置折算电耗系数"))
            .ToArray();
    }

    private static IReadOnlyList<ParkEnergyOrgShareDto> BuildOrgRows(
        ParkEnergyAggregate current,
        ParkEnergyAggregate previous)
    {
        var total = current.TotalTce;
        var rows = new List<ParkEnergyOrgShareDto>();

        foreach (var org in current.Orgs)
        {
            if (org.CoalEquivalentTce is not { } coal)
            {
                continue;
            }

            var before = previous.Orgs.FirstOrDefault(item => item.OrgUnitId == org.OrgUnitId)?.CoalEquivalentTce;

            decimal? delta = before is > 0
                ? Math.Round((coal - before.Value) / before.Value * 100m, 1)
                : null;

            rows.Add(new ParkEnergyOrgShareDto(
                org.OrgUnitName,
                $"{coal:0.#} tce",
                total > 0 ? Math.Round(coal / total, 4) : 0m,
                delta));
        }

        return rows;
    }

    private static string? BuildDelta(decimal current, decimal previous, string suffix)
    {
        if (previous <= 0)
        {
            // 没有可比基数时给「新增」而不是 100%：后者会让读者以为翻了一倍。
            return current > 0 ? $"上期无数据，无法{suffix}" : null;
        }

        var percent = Math.Round((current - previous) / previous * 100m, 1);
        var arrow = percent switch
        {
            > 0 => "↑",
            < 0 => "↓",
            _ => "→"
        };

        return $"{arrow} {Math.Abs(percent):0.#}% {suffix}";
    }

    /// <summary>
    /// 格式化数值。万级用量换成「万」：园区月度电量在千万级，
    /// 卡片上写 2,864,000 kWh 既占宽度也没人愿意读。
    /// </summary>
    private static string FormatNumber(decimal value) => value switch
    {
        >= 100_000_000m => $"{value / 100_000_000m:0.##} 亿",
        >= 10_000m => $"{value / 10_000m:0.##} 万",
        >= 1_000m => $"{value:#,0.#}",
        _ => $"{value:0.##}"
    };
}
