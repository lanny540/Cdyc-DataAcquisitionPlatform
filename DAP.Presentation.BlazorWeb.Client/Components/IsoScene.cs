using System.Globalization;

namespace DAP.Presentation.BlazorWeb.Client.Components;

/// <summary>
/// SVG 用户坐标里的一个点。
/// </summary>
/// <param name="X">横坐标。</param>
/// <param name="Y">纵坐标。</param>
public readonly record struct ScenePoint(double X, double Y);

/// <summary>
/// 场景图形基类。具体图形由 <see cref="IsoScene"/> 投影得到，页面只负责把列表画出来。
/// </summary>
public abstract record SceneShape;

/// <summary>多边形：设备的某一个面，或地面上的一块区域。</summary>
/// <param name="Points">SVG <c>points</c> 属性值。</param>
/// <param name="CssClass">取色与线型类。</param>
public sealed record ScenePolygon(string Points, string CssClass) : SceneShape;

/// <summary>折线：管线、地面网格、纸幅。</summary>
/// <param name="Points">SVG <c>points</c> 属性值。</param>
/// <param name="CssClass">取色与线型类。</param>
public sealed record ScenePolyline(string Points, string CssClass) : SceneShape;

/// <summary>圆：辊子的端面、纸卷的内圈。</summary>
/// <param name="Cx">圆心横坐标。</param>
/// <param name="Cy">圆心纵坐标。</param>
/// <param name="Radius">半径（屏幕单位）。</param>
/// <param name="CssClass">取色与线型类。</param>
public sealed record SceneEllipse(string Cx, string Cy, string Radius, string CssClass) : SceneShape;

/// <summary>
/// 等轴测（斜投影）场景构建器。
///
/// **为什么不用 WebGL**：孪生视图明确不需要旋转与缩放，引入三维引擎会带来
/// 首屏体积、离线部署与授权风险，收益却只是一个静态视角。这里把设备写成
/// 三维坐标里的长方体与圆柱，再投影成 SVG 多边形——离线可用、缩放不糊、
/// 首屏没有额外网络请求。
///
/// 投影公式（y 为进深，z 为高度）：
/// <c>X = 原点X + (x + 0.50y) · 比例</c>，<c>Y = 原点Y − (z + 0.40y) · 比例</c>。
/// 进深轴向右上方退，等价于相机站在左前方，因此每个盒子可见「正面 / 顶面 / 左侧面」。
///
/// 高度不做额外拉伸：一旦放大 z，辊子端面就会从圆变成竖椭圆，整台机器看起来像被拉长。
/// </summary>
public sealed class IsoScene
{
    /// <summary>画布宽度（viewBox 单位）。</summary>
    public const double ViewWidth = 1200;

    /// <summary>画布高度（viewBox 单位）。</summary>
    public const double ViewHeight = 460;

    /// <summary>世界坐标到屏幕坐标的比例：1 个世界单位 = 8.3 像素。</summary>
    private const double Scale = 8.3;

    private const double OriginX = 44;

    private const double OriginY = 372;

    /// <summary>进深轴在屏幕上的横向分量。</summary>
    private const double DepthX = 0.50;

    /// <summary>进深轴在屏幕上的纵向分量（向上）。</summary>
    private const double DepthY = 0.40;

    /// <summary>圆柱端面的采样段数。28 段在 8.3 比例下已经看不出棱角。</summary>
    private const int CircleSegments = 28;

    private readonly List<SceneShape> _shapes = [];

    /// <summary>推迟绘制的端面。同一排辊子要等所有筒身画完再统一压上端面。</summary>
    private readonly List<SceneShape> _deferred = [];

    /// <summary>已构建的图形，按添加顺序绘制（即由远及近）。</summary>
    public IReadOnlyList<SceneShape> Shapes => _shapes;

    /// <summary>
    /// 把世界坐标投影到屏幕坐标。
    /// </summary>
    /// <param name="x">沿纸机方向（物料流向）。</param>
    /// <param name="y">进深方向，越大越靠后。</param>
    /// <param name="z">高度。</param>
    /// <returns>屏幕坐标。</returns>
    public static ScenePoint Project(double x, double y, double z)
    {
        return new ScenePoint(
            OriginX + ((x + (y * DepthX)) * Scale),
            OriginY - ((z + (y * DepthY)) * Scale));
    }

    /// <summary>
    /// 地面：一块水平矩形，用来铺厂房地坪与工艺分区。
    /// </summary>
    /// <param name="x0">起点（沿纸机方向）。</param>
    /// <param name="x1">终点。</param>
    /// <param name="y0">近侧进深。</param>
    /// <param name="y1">远侧进深。</param>
    /// <param name="cssClass">样式类。</param>
    public void AddFloor(double x0, double x1, double y0, double y1, string cssClass)
    {
        AddPolygon(
            [
                Project(x0, y0, 0),
                Project(x1, y0, 0),
                Project(x1, y1, 0),
                Project(x0, y1, 0)
            ],
            cssClass);
    }

    /// <summary>
    /// 长方体设备：正面、顶面、左侧面三个可见面。
    /// </summary>
    /// <param name="x0">沿纸机方向起点。</param>
    /// <param name="x1">沿纸机方向终点。</param>
    /// <param name="y0">近侧进深。</param>
    /// <param name="y1">远侧进深。</param>
    /// <param name="z0">底面高度，通常为 0。</param>
    /// <param name="z1">顶面高度。</param>
    /// <param name="stageCss">工艺段取色类。</param>
    public void AddBox(double x0, double x1, double y0, double y1, double z0, double z1, string stageCss)
    {
        AddPolygon(
            [
                Project(x0, y0, z0),
                Project(x0, y1, z0),
                Project(x0, y1, z1),
                Project(x0, y0, z1)
            ],
            $"iso-side {stageCss}");

        AddPolygon(
            [
                Project(x0, y0, z1),
                Project(x1, y0, z1),
                Project(x1, y1, z1),
                Project(x0, y1, z1)
            ],
            $"iso-top {stageCss}");

        AddPolygon(
            [
                Project(x0, y0, z0),
                Project(x1, y0, z0),
                Project(x1, y0, z1),
                Project(x0, y0, z1)
            ],
            $"iso-front {stageCss}");
    }

    /// <summary>
    /// 立式圆筒：浆池、水力碎浆机这类设备。
    ///
    /// **侧面画满 360°，不做背面剔除**：这是斜投影而不是真实相机成像，
    /// 用「法线与视线点积」筛面会漏掉轮廓右侧的一段，顶面圆盘就会悬空。
    /// 侧面统一填充、不描边，全部面叠在一起正好是圆筒的正确剪影。
    /// </summary>
    /// <param name="cx">圆心沿纸机方向坐标。</param>
    /// <param name="cy">圆心进深坐标。</param>
    /// <param name="radius">半径。</param>
    /// <param name="z0">底面高度。</param>
    /// <param name="z1">顶面高度。</param>
    /// <param name="stageCss">工艺段取色类。</param>
    public void AddVerticalCylinder(
        double cx,
        double cy,
        double radius,
        double z0,
        double z1,
        string stageCss)
    {
        // 筒身必须是一整块：28 个相邻多边形各自抗锯齿，接缝会漏出底色，
        // 深色柱体上会显出一排竖向亮线。这里按角度取「上半椭圆 + 下半椭圆」拼成闭合剪影，
        // 不靠坐标过滤，避免舍入后端点错位、底边出现锯齿。
        var half = CircleSegments / 2;
        var outline = new List<ScenePoint>((half + 1) * 2);

        for (var i = 0; i <= half; i++)
        {
            var angle = Math.PI - (Math.PI * i / half);
            outline.Add(Project(
                cx + (radius * Math.Cos(angle)),
                cy + (radius * Math.Sin(angle)),
                z1));
        }

        for (var i = 0; i <= half; i++)
        {
            var angle = -Math.PI * i / half;
            outline.Add(Project(
                cx + (radius * Math.Cos(angle)),
                cy + (radius * Math.Sin(angle)),
                z0));
        }

        AddPolygon(outline, $"iso-shell {stageCss}");
        AddPolygon(BuildRing(cx, cy, radius).Select(point => Project(point.X, point.Y, z1)).ToArray(),
            $"iso-top {stageCss}");
    }

    /// <summary>
    /// 卧式圆筒：烘缸、压辊、卷取缸这类轴线横跨纸机的设备。
    /// 先画远端面，再画筒身的切线四边形，最后画近端面——近端面盖住筒身，
    /// 远端面只露出凸出的一段弧，正好是圆筒的轮廓。
    /// </summary>
    /// <param name="cx">轴线沿纸机方向坐标。</param>
    /// <param name="cz">轴线高度。</param>
    /// <param name="radius">半径。</param>
    /// <param name="y0">近端进深。</param>
    /// <param name="y1">远端进深。</param>
    /// <param name="stageCss">取色类。</param>
    /// <param name="deferCap">
    /// 是否推迟端面。一排同进深的辊子必须先把筒身全部画完，再统一压端面，
    /// 否则右边辊子的筒身会盖住左边辊子的端面，看起来像一堆斜方块。
    /// </param>
    public void AddCylinderAlongY(
        double cx,
        double cz,
        double radius,
        double y0,
        double y1,
        string stageCss,
        bool deferCap = false)
    {
        var nearCenter = Project(cx, y0, cz);
        var farCenter = Project(cx, y1, cz);

        // 投影后圆是正圆（x、z 同比例），两个端面只差一个平移量。
        var radiusPx = radius * Scale;
        var offsetX = farCenter.X - nearCenter.X;
        var offsetY = farCenter.Y - nearCenter.Y;

        // 两个等大圆的外公切线平行于圆心连线，切点半径与之垂直：
        // 令（cosθ, −sinθ）·（offsetX, offsetY）= 0，得 θ = atan2(offsetX, offsetY)。
        var tangent = Math.Atan2(offsetX, offsetY);

        AddEllipse(farCenter.X, farCenter.Y, radiusPx, $"iso-cyl-back {stageCss}");

        AddPolygon(
            [
                PointAt(nearCenter, radiusPx, tangent),
                PointAt(farCenter, radiusPx, tangent),
                PointAt(farCenter, radiusPx, tangent + Math.PI),
                PointAt(nearCenter, radiusPx, tangent + Math.PI)
            ],
            $"iso-cyl-side {stageCss}");

        var cap = new SceneEllipse(
            Format(nearCenter.X),
            Format(nearCenter.Y),
            Format(radiusPx),
            $"iso-cyl-cap {stageCss}");

        if (deferCap)
        {
            _deferred.Add(cap);
        }
        else
        {
            _shapes.Add(cap);
        }
    }

    /// <summary>
    /// 把推迟的端面一次性画出来。调用位置就是「这一排辊子的筒身都画完了」的位置。
    /// </summary>
    public void FlushDeferredCaps()
    {
        _shapes.AddRange(_deferred);
        _deferred.Clear();
    }

    /// <summary>
    /// 圆（端面内圈、轴心），用于纸卷的同心圆这类装饰。
    /// </summary>
    /// <param name="cx">圆心沿纸机方向坐标。</param>
    /// <param name="cz">圆心高度。</param>
    /// <param name="radius">半径。</param>
    /// <param name="y">端面所在进深。</param>
    /// <param name="cssClass">样式类。</param>
    public void AddRing(double cx, double cz, double radius, double y, string cssClass)
    {
        var center = Project(cx, y, cz);
        AddEllipse(center.X, center.Y, radius * Scale, cssClass);
    }

    /// <summary>
    /// 三维折线：管线、纸幅。所有点必须共面（这里都取同一进深）。
    /// </summary>
    /// <param name="points">世界坐标点序列。</param>
    /// <param name="cssClass">样式类。</param>
    public void AddPolyline(IReadOnlyList<(double X, double Y, double Z)> points, string cssClass)
    {
        AddPolyline(points.Select(point => Project(point.X, point.Y, point.Z)).ToArray(), cssClass);
    }

    /// <summary>
    /// 屏幕折线：地面网格这类已经算好坐标的线。
    /// </summary>
    /// <param name="points">屏幕坐标点序列。</param>
    /// <param name="cssClass">样式类。</param>
    public void AddPolyline(IReadOnlyList<ScenePoint> points, string cssClass)
    {
        _shapes.Add(new ScenePolyline(Format(points), cssClass));
    }

    private static (double X, double Y)[] BuildRing(double cx, double cy, double radius)
    {
        var ring = new (double X, double Y)[CircleSegments];

        for (var i = 0; i < CircleSegments; i++)
        {
            var angle = Math.Tau * i / CircleSegments;
            ring[i] = (cx + (radius * Math.Cos(angle)), cy + (radius * Math.Sin(angle)));
        }

        return ring;
    }

    private static ScenePoint PointAt(ScenePoint center, double radius, double angle)
    {
        return new ScenePoint(
            center.X + (radius * Math.Cos(angle)),
            center.Y - (radius * Math.Sin(angle)));
    }

    private void AddPolygon(IReadOnlyList<ScenePoint> points, string cssClass)
    {
        _shapes.Add(new ScenePolygon(Format(points), cssClass));
    }

    private void AddEllipse(double cx, double cy, double radius, string cssClass)
    {
        _shapes.Add(new SceneEllipse(
            Format(cx),
            Format(cy),
            Format(radius),
            cssClass));
    }

    private static string Format(IEnumerable<ScenePoint> points)
    {
        return string.Join(' ', points.Select(point => $"{Format(point.X)},{Format(point.Y)}"));
    }

    private static string Format(double value)
    {
        return value.ToString("F1", CultureInfo.InvariantCulture);
    }
}
