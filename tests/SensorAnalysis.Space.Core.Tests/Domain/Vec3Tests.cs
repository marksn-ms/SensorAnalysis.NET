using FluentAssertions;
using SensorAnalysis.Space.Core.Domain;
using Xunit;

namespace SensorAnalysis.Space.Core.Tests.Domain;

/// <summary>Wrap and torus helpers on <see cref="Vec3"/>.</summary>
public sealed class Vec3Tests
{
    [Fact]
    public void Wrap_MapsPositiveHalfToNegativeHalf()
    {
        var extent = 100.0;
        Vec3.WrapCoord(50, extent).Should().BeApproximately(-50, 1e-9);
        Vec3.Wrap(new Vec3(60, 0, 0), extent).X.Should().BeApproximately(-40, 1e-9);
    }

    [Fact]
    public void ToroidalDisplacement_TakesShortestImage()
    {
        var extent = 100.0;
        var d = Vec3.ToroidalDisplacement(new Vec3(40, 0, 0), new Vec3(-40, 0, 0), extent);
        d.X.Should().BeApproximately(20, 1e-9);
    }

    [Fact]
    public void OrthonormalFrame_IsRightHanded()
    {
        var (f, up) = Vec3.OrthonormalFrame(Vec3.UnitZ, Vec3.UnitY);
        var right = Vec3.Cross(up, f);
        Vec3.Dot(right, Vec3.UnitX).Should().BeApproximately(1, 1e-9);
        Vec3.Dot(f, up).Should().BeApproximately(0, 1e-9);
    }
}
