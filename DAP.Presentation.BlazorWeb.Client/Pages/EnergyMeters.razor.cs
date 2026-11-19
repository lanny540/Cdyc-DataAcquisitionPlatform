using DAP.Clients;
using DAP.Core.Shared.Contracts;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DAP.Presentation.BlazorWeb.Client.Pages;

/// <summary>
/// 能源计量点台账。
///
/// 这是整个能源板块的地基：一块表同时挂介质、组织、用途、系统四个维度，
/// 同一份读数才能按用途看构成、按组织看排名、按系统看分项。
/// 少了任何一个维度，这块表的读数就会在对应报表里静默消失。
///
/// 正式环境的读数由能源平台按 <c>POST /api/park-energy/readings</c> 推送；
/// 本页只维护台账，不录入读数。
/// </summary>
public partial class EnergyMeters
{
    [Inject] private IPlatformApiClient PlatformApiClient { get; set; } = null!;

    private IReadOnlyList<EnergyMeterDto> _meters = [];
    private EnergyDictionaryDto? _dictionary;
    private bool _isLoading = true;
    private bool _isSaving;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;

    private bool _isEditorOpen;
    private Guid? _editingId;
    private string _editorCode = string.Empty;
    private string _editorName = string.Empty;
    private Guid? _editorMediumId;
    private Guid? _editorOrgUnitId;
    private Guid? _editorUsageTypeId;
    private Guid? _editorSystemTypeId;
    private Guid? _editorParentId;
    private bool _editorCumulative = true;
    private decimal _editorRatio = 1m;
    private bool _editorEnabled = true;
    private string _editorRemark = string.Empty;

    private bool _isConfirmOpen;
    private EnergyMeterDto? _pendingDelete;

    private List<MediumFactorRow> _factorRows = [];

    private bool _isOrgEditorOpen;
    private Guid? _orgParentId;
    private string _orgLevel = "department";
    private string _orgCode = string.Empty;
    private string _orgName = string.Empty;

    /// <summary>
    /// 是否处于空态。加载中不算，避免首屏闪一下空状态。
    /// </summary>
    private bool IsEmpty => !_isLoading && _meters.Count == 0;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _isLoading = true;

        try
        {
            _dictionary = await PlatformApiClient.GetEnergyDictionaryAsync();
            _meters = await PlatformApiClient.GetEnergyMetersAsync();
            BuildFactorRows();
        }
        catch (Exception ex)
        {
            _message = $"计量点台账加载失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OpenCreate()
    {
        _editingId = null;
        _editorCode = string.Empty;
        _editorName = string.Empty;
        _editorMediumId = _dictionary?.Media.FirstOrDefault()?.Id;
        _editorOrgUnitId = null;
        _editorUsageTypeId = _dictionary?.UsageTypes.FirstOrDefault()?.Id;
        _editorSystemTypeId = _dictionary?.Systems.FirstOrDefault()?.Id;
        _editorParentId = null;
        _editorCumulative = true;
        _editorRatio = 1m;
        _editorEnabled = true;
        _editorRemark = string.Empty;
        _isEditorOpen = true;
    }

    private void OpenEdit(EnergyMeterDto meter)
    {
        _editingId = meter.Id;
        _editorCode = meter.Code;
        _editorName = meter.Name;
        // 用标识而不是名称回填：字典项改名之后，按名称反查会匹配不上，
        // 界面表现为「四个下拉全空」，用户只能重选一遍。
        _editorMediumId = meter.MediumId;
        _editorOrgUnitId = meter.OrgUnitId;
        _editorUsageTypeId = meter.UsageTypeId;
        _editorSystemTypeId = meter.SystemTypeId;
        _editorParentId = meter.ParentMeterCode is null
            ? null
            : _meters.FirstOrDefault(item => item.Code == meter.ParentMeterCode)?.Id;
        _editorCumulative = meter.IsCumulative;
        _editorRatio = meter.Ratio;
        _editorEnabled = meter.IsEnabled;
        _editorRemark = meter.Remark ?? string.Empty;
        _isEditorOpen = true;
    }

    private async Task SaveAsync()
    {
        if (_isSaving) return;

        if (string.IsNullOrWhiteSpace(_editorCode))
        {
            _message = "计量点编码不能为空。";
            _messageSeverity = Severity.Error;
            return;
        }

        if (_editorMediumId is not { } mediumId ||
            _editorOrgUnitId is not { } orgUnitId ||
            _editorUsageTypeId is not { } usageId ||
            _editorSystemTypeId is not { } systemId)
        {
            // 四个维度缺一个，这块表的读数就会在对应报表里消失且不报错。
            _message = "介质、组织、用途、设备系统四项都必须选择。";
            _messageSeverity = Severity.Error;
            return;
        }

        _isSaving = true;

        try
        {
            EnergyMeterUpsertRequest request = new(
                _editingId,
                _editorCode.Trim(),
                _editorName.Trim(),
                mediumId,
                orgUnitId,
                usageId,
                systemId,
                _editorParentId,
                _editorCumulative,
                _editorRatio,
                _editorEnabled,
                string.IsNullOrWhiteSpace(_editorRemark) ? null : _editorRemark.Trim());

            await PlatformApiClient.UpsertEnergyMeterAsync(request);
            _isEditorOpen = false;
            _message = "计量点已保存。";
            _messageSeverity = Severity.Success;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _message = $"保存失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isSaving = false;
        }
    }

    private void ConfirmDelete(EnergyMeterDto meter)
    {
        _pendingDelete = meter;
        _isConfirmOpen = true;
    }

    private async Task DeleteAsync()
    {
        if (_pendingDelete is not { } meter) return;

        _isConfirmOpen = false;

        try
        {
            await PlatformApiClient.DeleteEnergyMeterAsync(meter.Id);
            _message = $"计量点 {meter.Name} 已删除。";
            _messageSeverity = Severity.Success;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            // 已有读数的表不允许删除，服务端给的是中文原因，直接展示。
            _message = $"删除失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _pendingDelete = null;
        }
    }

    private void ClearMessage()
    {
        _message = null;
    }

    /// <summary>
    /// 介质系数草稿。编辑时不直接改字典对象：一旦改了但又没保存，
    /// 页面其余部分（下拉框、提示文案）会读到未保存的值。
    /// </summary>
    private void BuildFactorRows()
    {
        _factorRows = (_dictionary?.Media ?? [])
            .Select(medium => new MediumFactorRow {Medium = medium, CoalFactor = medium.CoalFactor})
            .ToList();
    }

    private async Task SaveFactorAsync(MediumFactorRow row)
    {
        try
        {
            EnergyMediumDto saved = await PlatformApiClient.UpdateEnergyMediumFactorAsync(
                row.Medium.Id,
                row.CoalFactor);

            _message = saved.CoalFactor is { } factor
                ? $"{saved.Name}折标煤系数已更新为 {factor:0.####} kgce/{saved.Unit}。"
                : $"{saved.Name}的折标煤系数已清除，该介质不再计入综合能耗。";
            _messageSeverity = Severity.Success;

            // 重新拉字典：综合能耗、构成占比都会随系数变化，页面上的其它读数必须同步刷新。
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _message = $"保存失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
    }

    private void OpenOrgCreateRoot()
    {
        EnergyOrgUnitDto? park = _dictionary?.OrgUnits.FirstOrDefault(unit => unit.Level == "park");
        if (park is null)
        {
            _message = "尚未初始化园区节点，无法新增部门。";
            _messageSeverity = Severity.Error;
            return;
        }

        OpenOrgCreate(park);
    }

    private void OpenOrgCreate(EnergyOrgUnitDto parent)
    {
        _orgParentId = parent.Id;
        _orgLevel = parent.Level == "park" ? "department" : "process";
        _orgCode = string.Empty;
        _orgName = string.Empty;
        _isOrgEditorOpen = true;
    }

    private string _orgParentLabel
    {
        get
        {
            EnergyOrgUnitDto? parent = _dictionary?.OrgUnits.FirstOrDefault(unit => unit.Id == _orgParentId);
            return parent is null ? "—" : $"{parent.Name}（{parent.Code}）";
        }
    }

    private async Task SaveOrgUnitAsync()
    {
        if (_isSaving) return;

        _isSaving = true;

        try
        {
            await PlatformApiClient.UpsertEnergyOrgUnitAsync(
                new EnergyOrgUnitUpsertRequest(null, _orgCode.Trim(), _orgName.Trim(), _orgLevel, _orgParentId));

            _isOrgEditorOpen = false;
            _message = "组织节点已保存。";
            _messageSeverity = Severity.Success;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _message = $"保存失败：{UserFacingErrors.Describe(ex)}";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isSaving = false;
        }
    }

    /// <summary>
    /// 组织节点按层级排序。后端已按排序号返回，这里再按深度稳定排一次：
    /// 排序号一旦被人工调整成「部门 10、园区 20」，树会显示成子节点在父节点上面。
    /// </summary>
    private IReadOnlyList<EnergyOrgUnitDto> OrderedOrgUnits =>
        (_dictionary?.OrgUnits ?? [])
        .OrderBy(unit => unit.Depth)
        .ToArray();

    /// <summary>
    /// 组织节点的缩进。内联样式走方法返回字符串——写在属性里的嵌套插值
    /// 在 Razor 中不一定求值，会原样输出到页面上。
    /// </summary>
    private static string IndentStyle(int depth)
    {
        return $"width: {Math.Max(depth, 0) * 18}px";
    }

    private static string MediumLabel(EnergyMediumDto medium)
    {
        string suffix = medium.IsPrimaryEnergy
            ? medium.CoalFactor is { } factor ? $"折标 {factor:0.####}" : "未配置折标"
            : "不计入综合能耗";

        return $"{medium.Name}（{medium.Unit}）· {suffix}";
    }

    private static string LevelLabel(string level)
    {
        return level switch
        {
            "park" => "园区",
            "department" => "部门",
            "process" => "工序",
            _ => level
        };
    }

    private static Color LevelColor(string level)
    {
        return level switch
        {
            "park" => Color.Secondary,
            "department" => Color.Info,
            _ => Color.Default
        };
    }

    /// <summary>
    /// 介质系数编辑行。
    /// </summary>
    private sealed class MediumFactorRow
    {
        /// <summary>获取介质。</summary>
        public required EnergyMediumDto Medium { get; init; }

        /// <summary>获取或设置折标煤系数草稿值。</summary>
        public decimal? CoalFactor { get; set; }
    }

    private string OrgLabel(EnergyOrgUnitDto unit)
    {
        return new string('　', unit.Depth) + unit.Name;
    }

    private static Color StatusColor(bool enabled)
    {
        return enabled ? Color.Success : Color.Warning;
    }
}