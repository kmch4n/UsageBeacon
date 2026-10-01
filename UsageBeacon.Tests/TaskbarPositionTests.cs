using System.Drawing;
using UsageBeacon.Utilities;

namespace UsageBeacon.Tests;

public sealed class TaskbarPositionTests
{
    [Fact]
    public void HasNotificationClearance_RejectsTheObservedTwentyFourPixelShift()
    {
        var widget = Rectangle.FromLTRB(2037, 0, 2155, 40);
        var before = Rectangle.FromLTRB(2181, 0, 2560, 40);
        var after = Rectangle.FromLTRB(2157, 0, 2560, 40);

        Assert.True(TaskbarPosition.HasNotificationClearance(widget, before, 26));
        Assert.False(TaskbarPosition.HasNotificationClearance(widget, after, 26));
    }

    [Fact]
    public void HasNotificationClearance_AcceptsWidgetOutsideTaskbarVertically()
    {
        var widget = Rectangle.FromLTRB(2037, 44, 2155, 84);
        var notification = Rectangle.FromLTRB(2157, 0, 2560, 40);

        Assert.True(TaskbarPosition.HasNotificationClearance(widget, notification, 26));
    }

    [Fact]
    public void RequiredNotificationShift_UsesActualWindowBounds()
    {
        var widget = Rectangle.FromLTRB(1973, 0, 2164, 40);
        var notification = Rectangle.FromLTRB(2181, 0, 2560, 40);

        Assert.Equal(9, TaskbarPosition.RequiredNotificationShift(
            widget, notification, 26));
    }

    [Fact]
    public void IsOutsideTaskbar_RejectsAnUnverifiedFallbackPosition()
    {
        var taskbar = Rectangle.FromLTRB(0, 0, 2560, 40);

        Assert.False(TaskbarPosition.IsOutsideTaskbar(
            Rectangle.FromLTRB(2037, 0, 2155, 40), taskbar));
        Assert.True(TaskbarPosition.IsOutsideTaskbar(
            Rectangle.FromLTRB(2037, 44, 2155, 84), taskbar));
    }

    [Fact]
    public void IsWithinTaskbar_RejectsDesktopAndPartialOverlap()
    {
        var taskbar = Rectangle.FromLTRB(0, 0, 2560, 40);

        Assert.True(TaskbarPosition.IsWithinTaskbar(
            Rectangle.FromLTRB(2000, 0, 2150, 40), taskbar));
        Assert.False(TaskbarPosition.IsWithinTaskbar(
            Rectangle.FromLTRB(2000, 44, 2150, 84), taskbar));
        Assert.False(TaskbarPosition.IsWithinTaskbar(
            Rectangle.FromLTRB(2000, 10, 2150, 50), taskbar));
        Assert.True(TaskbarPosition.IsWithinTaskbar(
            Rectangle.FromLTRB(2000, 960, 2150, 1000),
            Rectangle.FromLTRB(0, 960, 2560, 1000)));
    }

    private static readonly DateTime Now = new(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    [Fact]
    public void ShouldRescan_ReturnsTrue_WhenGeometryChanged()
        => Assert.True(TaskbarPosition.ShouldRescan(
            Now, Now, geometryChanged: true, Interval));

    [Fact]
    public void ShouldRescan_ReturnsTrue_WhenCacheEntryIsStale()
        => Assert.True(TaskbarPosition.ShouldRescan(
            Now, Now.AddSeconds(-6), geometryChanged: false, Interval));

    [Fact]
    public void ShouldRescan_ReturnsFalse_WhenEntryIsFreshAndGeometryUnchanged()
        => Assert.False(TaskbarPosition.ShouldRescan(
            Now, Now.AddSeconds(-1), geometryChanged: false, Interval));
}
