using System.Globalization;
using DAP.Presentation.BlazorWeb.Client.Components;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 孪生视图的轴测投影测试。
///
/// 这些图形跑在浏览器里，翻了错单元测试看不出来（例如把 &lt;ellipse&gt; 的半径写成 r，
/// SVG 会静默不画）。这里守住几何本身的契约：投影方向、面数、坐标范围。
/// </summary>
public sealed class IsoSceneTests
{
    /// <summary>画布尺寸变化会让标注跑出画面，因此把它固定成测试常量。</summary>
    private const double Scale = 8.3;

    private const double OriginX = 44;

    private const double OriginY = 372;

    /// <summary>
    /// 高度上升时屏幕纵坐标必须变小（SVG 的 y 轴向下）。
    /// </summary>
    [Fact]
    public void Project_Should_Lift_Higher_Points()
    {
        ScenePoint ground = IsoScene.Project(10, 5, 0);
        ScenePoint raised = IsoScene.Project(10, 5, 2);

        Assert.Equal(ground.X, raised.X, 3);
        Assert.True(raised.Y < ground.Y);
        Assert.Equal(ground.Y - (2 * Scale), raised.Y, 3);
    }

    /// <summary>
    /// 进深增加时，点向右上方退——这是「相机在左前方」的定义。
    /// </summary>
    [Fact]
    public void Project_Should_Recede_Up_And_Right()
    {
        ScenePoint front = IsoScene.Project(10, 0, 0);
        ScenePoint back = IsoScene.Project(10, 10, 0);

        Assert.True(back.X > front.X);
        Assert.True(back.Y < front.Y);
    }

    /// <summary>
    /// 长方体始终是三个可见面：正面、顶面、左侧面。
    /// </summary>
    [Fact]
    public void Box_Should_Emit_Three_Faces()
    {
        IsoScene scene = new();
        scene.AddBox(0, 4, 0, 6, 0, 3, "stage-wet");

        Assert.Equal(3, scene.Shapes.Count);

        string[] classes = scene.Shapes.Cast<ScenePolygon>().Select(shape => shape.CssClass).ToArray();
        Assert.Contains(classes, css => css.Contains("iso-front", StringComparison.Ordinal));
        Assert.Contains(classes, css => css.Contains("iso-top", StringComparison.Ordinal));
        Assert.Contains(classes, css => css.Contains("iso-side", StringComparison.Ordinal));
        Assert.All(classes, css => Assert.Contains("stage-wet", css, StringComparison.Ordinal));
    }

    /// <summary>
    /// 卧式圆筒必须是「远端面 + 筒身 + 近端面」三块，且端面半径等于真实半径乘比例。
    /// </summary>
    [Fact]
    public void Cylinder_Should_Emit_Cap_Body_And_Near_Cap()
    {
        IsoScene scene = new();
        scene.AddCylinderAlongY(20, 6, 2, 4, 12, "stage-metal");

        Assert.Equal(3, scene.Shapes.Count);
        Assert.IsType<SceneEllipse>(scene.Shapes[0]);
        Assert.IsType<ScenePolygon>(scene.Shapes[1]);

        SceneEllipse nearCap = Assert.IsType<SceneEllipse>(scene.Shapes[2]);
        Assert.Equal((2 * Scale).ToString("F1", CultureInfo.InvariantCulture), nearCap.Radius);
        Assert.Contains("iso-cyl-cap", nearCap.CssClass, StringComparison.Ordinal);
    }

    /// <summary>
    /// 筒身的水平宽度必须大于端面直径：少画一段就会露出端面悬空。
    /// </summary>
    [Fact]
    public void Cylinder_Body_Should_Cover_Whole_Silhouette()
    {
        IsoScene scene = new();
        scene.AddCylinderAlongY(20, 6, 2, 4, 12, "stage-metal");

        ScenePolygon body = Assert.IsType<ScenePolygon>(scene.Shapes[1]);
        double[] xs = ParsePoints(body.Points).Select(point => point.X).ToArray();

        Assert.True(xs.Max() - xs.Min() > 2 * 2 * Scale,
            "筒身宽度应大于端面直径，否则端面会悬在筒身之外");
    }

    /// <summary>
    /// 立式圆筒：顶面是多边形而不是椭圆（端面在水平面上，投影后是斜椭圆）。
    /// </summary>
    [Fact]
    public void Vertical_Cylinder_Should_Emit_Single_Shell_And_Top()
    {
        IsoScene scene = new();
        scene.AddVerticalCylinder(12, 8, 3, 0, 7, "stage-stock");

        Assert.Equal(2, scene.Shapes.Count);

        ScenePolygon shell = Assert.IsType<ScenePolygon>(scene.Shapes[0]);
        Assert.Contains("iso-shell", shell.CssClass, StringComparison.Ordinal);
        Assert.DoesNotContain("iso-side", shell.CssClass, StringComparison.Ordinal);

        ScenePolygon top = Assert.IsType<ScenePolygon>(scene.Shapes[1]);
        Assert.Contains("iso-top", top.CssClass, StringComparison.Ordinal);
    }

    /// <summary>
    /// 整台纸机必须落在画布内，否则右侧的卷曲与顶部标注会被裁掉。
    /// </summary>
    [Fact]
    public void Scene_Should_Stay_Inside_ViewBox()
    {
        IsoScene scene = new();

        scene.AddFloor(0, 120, 0, 30, "scene-floor");
        scene.AddBox(0.8, 118.4, 3.4, 19.4, 0, 0.5, "stage-metal");
        scene.AddVerticalCylinder(33.6, 11, 3.95, 0, 12.4, "stage-stock");
        scene.AddCylinderAlongY(114.9, 5.6, 3.4, 7.2, 14.8, "stage-paper");
        scene.AddBox(78.4, 98.8, 3.6, 19.2, 13.4, 14.4, "stage-dry");

        foreach (ScenePolygon polygon in scene.Shapes.OfType<ScenePolygon>())
        foreach ((double X, double Y) point in ParsePoints(polygon.Points))
        {
            Assert.InRange(point.X, 0, IsoScene.ViewWidth);
            Assert.InRange(point.Y, 0, IsoScene.ViewHeight);
        }
    }

    /// <summary>
    /// 解析 SVG <c>points</c> 属性。顺带断言没有 NaN：一旦投影算出非数字，
    /// 浏览器会整块不画，页面上只是少一截设备，很难发现。
    /// </summary>
    private static IEnumerable<(double X, double Y)> ParsePoints(string points)
    {
        foreach (string pair in points.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split(',');
            Assert.Equal(2, parts.Length);

            double x = double.Parse(parts[0], CultureInfo.InvariantCulture);
            double y = double.Parse(parts[1], CultureInfo.InvariantCulture);

            Assert.False(double.IsNaN(x) || double.IsInfinity(x), $"横坐标异常：{pair}");
            Assert.False(double.IsNaN(y) || double.IsInfinity(y), $"纵坐标异常：{pair}");

            yield return (x, y);
        }
    }
}
