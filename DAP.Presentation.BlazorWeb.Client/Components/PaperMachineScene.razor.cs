using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace DAP.Presentation.BlazorWeb.Client.Components;

/// <summary>
/// 钞纸机车间三维示意（数字孪生视图）。
///
/// 整台纸机由三维坐标里的长方体与圆筒投影成 SVG，视角固定（不旋转、不缩放）：
/// 用户要的是「一眼看出这是一条产线」，不是可以转来转去的三维模型。
///
/// 数值由 <see cref="PaperMachineSimulator"/> 以 5 秒节拍确定性生成；
/// 组件销毁时（切回数据看板或离开页面）循环立即取消，不会在后台空转。
/// </summary>
public partial class PaperMachineScene : ComponentBase, IAsyncDisposable
{
    /// <summary>标注框宽度（viewBox 单位）。</summary>
    private const double TagWidth = 124;

    /// <summary>标注框高度。</summary>
    private const double TagHeight = 40;

    /// <summary>两排标注的纵向位置：交错排布，避免相邻工位的标注叠在一起。</summary>
    private const double TagRowTop = 62;

    private const double TagRowBottom = 118;

    private static readonly IReadOnlyList<SceneShape> Scene = BuildScene();

    private static readonly TagSpec[] TagSpecs = BuildTagSpecs();

    private static readonly StageRuler[] StageRulers =
    [
        new("备浆", "投料 · 打浆 · 配浆流送", "stage-stock", 48.2, 396.8),
        new("湿部", "成形 · 压榨脱水", "stage-wet", 396.8, 687.3),
        new("干部", "干燥 · 压光 · 卷取", "stage-dry", 687.3, 1040.0)
    ];

    private CancellationTokenSource? _loopCts;

    private Task? _loopTask;

    private PaperMachineSnapshot _snapshot = PaperMachineSimulator.Snapshot(DateTimeOffset.Now);

    /// <summary>当前快照对应的标注，数值随快照每 5 秒更新。</summary>
    private IReadOnlyList<SceneTag> Tags
    {
        get
        {
            var stations = _snapshot.Stations.ToDictionary(station => station.Code);

            return TagSpecs
                .Where(spec => stations.ContainsKey(spec.Code))
                .Select(spec => SceneTag.Create(spec, stations[spec.Code]))
                .ToArray();
        }
    }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _loopCts = new CancellationTokenSource();
        _loopTask = RunAsync(_loopCts.Token);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_loopCts is null)
        {
            return;
        }

        await _loopCts.CancelAsync();

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _loopCts.Dispose();
        _loopCts = null;
        _loopTask = null;
    }

    /// <summary>
    /// 按采样节拍刷新快照。
    ///
    /// 这里是本地模拟，不走网络；接入真实工况后改成调接口，并把失败处理成
    /// 「保留上一次快照 + 顶部提示」，不要清空整条线。
    /// </summary>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(PaperMachineSimulator.Tick, cancellationToken);

                _snapshot = PaperMachineSimulator.Snapshot(DateTimeOffset.Now);

                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// 构建车间场景。绘制顺序即远近顺序：先地面，再由远及近摆设备。
    /// </summary>
    private static IReadOnlyList<SceneShape> BuildScene()
    {
        var scene = new IsoScene();

        const string Stock = "stage-stock";
        const string Wet = "stage-wet";
        const string Dry = "stage-dry";
        const string Metal = "stage-metal";
        const string Paper = "stage-paper";

        BuildFloor(scene);

        // 设备基础台：纸机本来坐在一条混凝土基础上，没有它，所有设备都像浮在地坪上。
        scene.AddBox(0.8, 118.4, 3.4, 19.4, 0, 0.5, Metal);

        BuildFeeding(scene, Stock, Metal);
        BuildRefining(scene, Stock, Metal);
        BuildStockPreparation(scene, Stock, Metal);
        BuildWireSection(scene, Wet, Metal);
        BuildPressSection(scene, Wet, Metal);
        BuildDryerSection(scene, Dry, Metal);
        BuildCalender(scene, Dry, Metal);
        BuildReel(scene, Dry, Metal, Paper);
        BuildProcessLines(scene);

        return scene.Shapes;
    }

    /// <summary>
    /// 厂房地坪：整块地坪 + 三个工艺分区色块 + 网格。
    /// 分区色块对应页脚下的三段标尺，让「哪一段属于哪个工艺」在图上就能看出来。
    /// </summary>
    private static void BuildFloor(IsoScene scene)
    {
        scene.AddFloor(0, 120, 0, 30, "scene-floor");
        scene.AddFloor(0.4, 42.5, 0.4, 29.6, "scene-zone stage-stock");
        scene.AddFloor(42.5, 77.5, 0.4, 29.6, "scene-zone stage-wet");
        scene.AddFloor(77.5, 119.6, 0.4, 29.6, "scene-zone stage-dry");

        for (var x = 8.0; x < 120; x += 8)
        {
            scene.AddPolyline(
                [IsoScene.Project(x, 0, 0), IsoScene.Project(x, 30, 0)],
                "scene-grid");
        }

        for (var y = 6.0; y < 30; y += 6)
        {
            scene.AddPolyline(
                [IsoScene.Project(0, y, 0), IsoScene.Project(120, y, 0)],
                "scene-grid");
        }

        // 地坪轮廓：一圈实线把画面框住，否则白底上的浅色地坪没有边界。
        scene.AddPolyline(
            [
                IsoScene.Project(0, 0, 0),
                IsoScene.Project(120, 0, 0),
                IsoScene.Project(120, 30, 0),
                IsoScene.Project(0, 30, 0),
                IsoScene.Project(0, 0, 0)
            ],
            "scene-floor-edge");
    }

    /// <summary>投料：浆包堆垛、链板输送带、水力碎浆机。</summary>
    private static void BuildFeeding(IsoScene scene, string stage, string metal)
    {
        // 后排先画，前排后画——否则前排浆包会被后排盖住。
        scene.AddBox(2.0, 5.6, 8.0, 11.6, 0, 2.4, stage);
        scene.AddBox(2.0, 5.6, 8.0, 11.6, 2.4, 4.8, stage);
        scene.AddBox(2.0, 5.6, 3.6, 7.2, 0, 2.4, stage);
        scene.AddBox(2.0, 5.6, 3.6, 7.2, 2.4, 4.8, stage);
        scene.AddBox(2.2, 5.4, 5.8, 9.4, 4.8, 7.2, stage);

        scene.AddBox(6.2, 9.8, 5.6, 9.2, 3.2, 3.9, metal);
        scene.AddBox(6.6, 7.6, 5.8, 6.8, 0, 3.2, metal);
        scene.AddBox(8.6, 9.6, 8.0, 9.0, 0, 3.2, metal);

        // 碎浆机：槽体 + 略大一圈的槽口法兰，法兰让顶部有厚度。
        scene.AddVerticalCylinder(13.2, 8.4, 3.4, 0, 7.0, stage);
        scene.AddVerticalCylinder(13.2, 8.4, 3.9, 7.0, 7.7, stage);
    }

    /// <summary>打浆：两台双盘磨，一前一后，各带电机的。</summary>
    private static void BuildRefining(IsoScene scene, string stage, string metal)
    {
        scene.AddBox(20.0, 24.6, 12.2, 18.2, 0, 4.4, stage);
        scene.AddBox(25.2, 28.2, 12.6, 17.8, 0, 3.1, metal);
        scene.AddBox(20.0, 24.6, 3.8, 9.8, 0, 4.4, stage);
        scene.AddBox(25.2, 28.2, 4.2, 9.4, 0, 3.1, metal);
    }

    /// <summary>配浆流送：成浆池、冲浆泵。</summary>
    private static void BuildStockPreparation(IsoScene scene, string stage, string metal)
    {
        scene.AddVerticalCylinder(33.6, 11.0, 3.6, 0, 11.8, stage);
        scene.AddVerticalCylinder(33.6, 11.0, 3.95, 11.8, 12.4, stage);
        scene.AddBox(38.2, 40.8, 9.2, 12.6, 0, 2.2, metal);
    }

    /// <summary>湿部前半段：流浆箱、成形网案、伏辊。</summary>
    private static void BuildWireSection(IsoScene scene, string stage, string metal)
    {
        scene.AddBox(45.6, 46.6, 17.6, 18.6, 0, 9.2, metal);
        scene.AddBox(45.6, 46.6, 4.4, 5.4, 0, 9.2, metal);
        scene.AddBox(45.2, 48.4, 4.4, 19.0, 8.6, 11.4, stage);
        scene.AddBox(48.0, 48.8, 5.4, 18.0, 10.5, 11.0, metal);

        scene.AddBox(49.0, 50.0, 4.8, 5.8, 0, 4.6, metal);
        scene.AddBox(60.6, 61.6, 4.8, 5.8, 0, 4.6, metal);
        scene.AddBox(48.4, 62.4, 5.0, 18.4, 4.6, 5.4, stage);

        // 成形网：比网案箱体略亮一层，读起来像一张绷紧的网。
        scene.AddBox(48.4, 62.8, 5.2, 18.2, 5.4, 5.7, metal);
        scene.AddBox(48.6, 62.6, 4.6, 5.4, 5.2, 5.9, stage);
        scene.AddBox(48.6, 62.6, 18.0, 18.8, 5.2, 5.9, stage);

        scene.AddCylinderAlongY(64.6, 5.9, 1.7, 7.6, 14.4, metal);
    }

    /// <summary>压榨：三道压区，上下各一组辊。</summary>
    private static void BuildPressSection(IsoScene scene, string stage, string metal)
    {
        scene.AddBox(66.0, 77.0, 18.0, 19.2, 0, 6.8, stage);
        scene.AddBox(66.0, 67.0, 4.2, 5.4, 0, 6.8, stage);
        scene.AddBox(76.0, 77.0, 4.2, 5.4, 0, 6.8, stage);
        scene.AddBox(67.0, 76.2, 5.0, 17.4, 0, 1.1, metal);

        scene.AddCylinderAlongY(70.1, 2.9, 1.4, 8.0, 14.0, metal, deferCap: true);
        scene.AddCylinderAlongY(73.1, 2.9, 1.4, 8.0, 14.0, metal, deferCap: true);
        scene.AddCylinderAlongY(68.6, 5.8, 1.6, 8.0, 14.0, metal, deferCap: true);
        scene.AddCylinderAlongY(71.6, 5.8, 1.6, 8.0, 14.0, metal, deferCap: true);
        scene.AddCylinderAlongY(74.6, 5.8, 1.6, 8.0, 14.0, metal, deferCap: true);
        scene.FlushDeferredCaps();
    }

    /// <summary>干部前半段：五组烘缸 + 蒸汽母管 + 烘罩。</summary>
    private static void BuildDryerSection(IsoScene scene, string stage, string metal)
    {
        // 烘罩支柱先画（在设备后面），顶板最后画（在设备上面）。
        scene.AddBox(78.6, 79.4, 18.2, 19.0, 0, 13.4, metal);
        scene.AddBox(78.6, 79.4, 3.9, 4.7, 0, 13.4, metal);
        scene.AddBox(97.8, 98.6, 18.2, 19.0, 0, 13.4, metal);
        scene.AddBox(97.8, 98.6, 3.9, 4.7, 0, 13.4, metal);

        for (var index = 0; index < 4; index++)
        {
            scene.AddCylinderAlongY(80.6 + (index * 5.0), 6.8, 2.2, 8.0, 14.0, metal, deferCap: true);
        }

        scene.FlushDeferredCaps();

        scene.AddBox(79.2, 98.4, 5.0, 6.2, 10.8, 11.6, metal);
        scene.AddBox(78.4, 98.8, 3.6, 19.2, 13.4, 14.4, stage);
    }

    /// <summary>压光：四辊压光机，辊子竖向叠放。</summary>
    private static void BuildCalender(IsoScene scene, string stage, string metal)
    {
        scene.AddBox(101.6, 107.4, 17.6, 18.8, 0, 9.6, stage);
        scene.AddBox(101.6, 102.6, 4.4, 5.6, 0, 9.6, stage);
        scene.AddBox(106.4, 107.4, 4.4, 5.6, 0, 9.6, stage);

        for (var index = 0; index < 4; index++)
        {
            scene.AddCylinderAlongY(104.4, 2.9 + (index * 1.8), 0.85, 8.0, 14.0, metal, deferCap: true);
        }

        scene.FlushDeferredCaps();
    }

    /// <summary>卷曲：卷取缸 + 正在长大的纸卷。</summary>
    private static void BuildReel(IsoScene scene, string stage, string metal, string paper)
    {
        scene.AddBox(108.4, 116.0, 17.6, 18.8, 0, 8.8, stage);
        scene.AddBox(108.4, 109.4, 4.2, 5.4, 0, 8.8, stage);

        scene.AddCylinderAlongY(110.6, 4.6, 1.9, 7.6, 14.4, metal);
        scene.AddCylinderAlongY(114.9, 5.6, 3.4, 7.2, 14.8, paper);

        // 纸卷端面的同心圆：外圈是纸边，内圈是卷芯。
        scene.AddRing(114.9, 5.6, 2.3, 7.2, "scene-ring");
        scene.AddRing(114.9, 5.6, 1.0, 7.2, "scene-ring");
    }

    /// <summary>
    /// 管线与纸幅。
    ///
    /// 纸幅是这条线上唯一「连续」的东西：从流浆箱唇口一路走到卷取，
    /// 让静止的设备之间有了物料在流动的因果关系。管线上不加动画，
    /// 否则整屏都在动，反而看不清主线。
    /// </summary>
    private static void BuildProcessLines(IsoScene scene)
    {
        // 碎浆机 → 磨前池
        scene.AddPolyline(
            [(16.6, 8.4, 2.4), (18.4, 7.4, 2.4), (20.0, 6.9, 2.4)],
            "scene-pipe");

        // 磨后 → 成浆池顶
        scene.AddPolyline(
            [(28.2, 6.8, 2.3), (30.2, 8.6, 4.2), (30.2, 10.8, 11.6), (33.0, 11.0, 12.2)],
            "scene-pipe");

        // 成浆池 → 冲浆泵
        scene.AddPolyline(
            [(36.6, 11.0, 1.4), (38.2, 10.9, 1.4)],
            "scene-pipe");

        // 冲浆泵 → 流浆箱
        scene.AddPolyline(
            [(40.8, 10.9, 1.8), (43.6, 10.9, 7.6), (45.4, 10.9, 10.6)],
            "scene-pipe");

        // 纸幅：唇口 → 网案 → 伏辊 → 压榨 → 烘缸 → 压光 → 卷取
        var web = new (double X, double Y, double Z)[]
        {
            (45.6, 11.5, 11.2),
            (48.8, 11.5, 10.9),
            (49.8, 11.5, 5.8),
            (62.6, 11.5, 5.8),
            (64.6, 11.5, 7.6),
            (66.6, 11.5, 7.4),
            (74.6, 11.5, 7.4),
            (77.4, 11.5, 7.6),
            (79.6, 11.5, 9.0),
            (96.4, 11.5, 9.0),
            (99.0, 11.5, 9.2),
            (101.8, 11.5, 9.9),
            (107.0, 11.5, 9.9),
            (109.6, 11.5, 6.6),
            (112.6, 11.5, 9.0),
            (114.9, 11.5, 9.0)
        };

        scene.AddPolyline(web, "scene-web-edge");
        scene.AddPolyline(web, "scene-web");
        scene.AddPolyline(web, "scene-web-flow");
    }

    /// <summary>
    /// 标注位置。锚点取设备顶面中点，标注框在画布上方交错排两行。
    /// </summary>
    private static TagSpec[] BuildTagSpecs()
    {
        (string Code, double CenterX, double TopZ)[] anchors =
        [
            ("FEED-01", 13.2, 7.7),
            ("REF-01", 24.0, 4.4),
            ("STK-01", 33.6, 12.4),
            ("WET-01", 46.0, 11.4),
            ("PRS-01", 71.6, 7.4),
            ("DRY-01", 88.4, 14.4),
            ("CLD-01", 104.4, 9.6),
            ("REW-01", 114.9, 9.0)
        ];

        return anchors.Select((anchor, index) =>
        {
            var point = IsoScene.Project(anchor.CenterX, 5, anchor.TopZ);
            var tagX = Math.Clamp(point.X - (TagWidth / 2), 6, IsoScene.ViewWidth - 6 - TagWidth);
            var tagY = index % 2 == 0 ? TagRowTop : TagRowBottom;

            return new TagSpec(anchor.Code, tagX, tagY, point.X, point.Y);
        }).ToArray();
    }

    private static string Svg(double value)
    {
        return value.ToString("F1", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 标注的定位信息。屏幕坐标在构造时算好，避免每次渲染重复投影。
    /// </summary>
    /// <param name="Code">设备位号。</param>
    /// <param name="TagX">标注框左边界。</param>
    /// <param name="TagY">标注框上边界。</param>
    /// <param name="AnchorX">锚点横坐标。</param>
    /// <param name="AnchorY">锚点纵坐标。</param>
    private sealed record TagSpec(string Code, double TagX, double TagY, double AnchorX, double AnchorY);

    /// <summary>
    /// 一条已经排好版的标注。所有坐标都是格式化后的字符串，
    /// 避免在德语等区域把小数点写成逗号，直接破坏 SVG 属性。
    /// </summary>
    private sealed record SceneTag(
        string Name,
        string Code,
        string MetricLabel,
        string ValueText,
        string BoxX,
        string BoxY,
        string NameX,
        string CodeX,
        string MetricX,
        string ValueX,
        string LineOneY,
        string LineTwoY,
        string LeaderPoints,
        string AnchorX,
        string AnchorY,
        string StageCss)
    {
        public static SceneTag Create(TagSpec spec, TwinStation station)
        {
            var metric = station.Metrics[0];
            var value = $"{metric.DisplayValue} {metric.Unit}";

            return new SceneTag(
                station.Name,
                station.Code,
                metric.Label,
                value,
                Svg(spec.TagX),
                Svg(spec.TagY),
                Svg(spec.TagX + 10),
                Svg(spec.TagX + TagWidth - 10),
                Svg(spec.TagX + 10),
                Svg(spec.TagX + TagWidth - 10),
                Svg(spec.TagY + 16),
                Svg(spec.TagY + 32),
                $"{Svg(spec.TagX + (TagWidth / 2))},{Svg(spec.TagY + TagHeight)} {Svg(spec.AnchorX)},{Svg(spec.AnchorY)}",
                Svg(spec.AnchorX),
                Svg(spec.AnchorY),
                station.Stage switch
                {
                    PaperMachineStage.Stock => "stage-stock",
                    PaperMachineStage.WetEnd => "stage-wet",
                    _ => "stage-dry"
                });
        }
    }

    /// <summary>
    /// 页脚工艺段标尺。
    /// </summary>
    /// <param name="Name">段名。</param>
    /// <param name="Note">段内工序。</param>
    /// <param name="CssClass">取色类。</param>
    /// <param name="X1">起始横坐标。</param>
    /// <param name="X2">结束横坐标。</param>
    private sealed record StageRuler(string Name, string Note, string CssClass, double X1, double X2)
    {
        public string X1Text => Svg(X1);

        public string X2Text => Svg(X2);

        public string CenterText => Svg((X1 + X2) / 2);
    }
}
