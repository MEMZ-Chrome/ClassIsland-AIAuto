using ClassIsland.Automation.Core.Services;
using Xunit;

namespace ClassIsland.Automation.Tests;

public class ScheduleTextTests
{
    [Theory]
    [InlineData("周一", 1)]
    [InlineData("星期三", 3)]
    [InlineData("礼拜天", 0)]
    [InlineData("周六课表", 6)]
    [InlineData("高三年级", -1)]
    [InlineData("初二课表", -1)]
    [InlineData("", -1)]
    public void ParseWeekDay_OnlyMatchesExplicitWeekdayPrefixes(string name, int expected)
    {
        Assert.Equal(expected, ToolExecutor.ParseWeekDay(name));
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, "周一")]
    [InlineData(DayOfWeek.Saturday, "周六")]
    [InlineData(DayOfWeek.Sunday, "周日")]
    public void GetChineseWeekDay_MapsAllDays(DayOfWeek day, string expected)
    {
        Assert.Equal(expected, ToolExecutor.GetChineseWeekDay(day));
    }

    [Fact]
    public void NormalizeWeekDayName_ConvertsSynonyms()
    {
        Assert.Equal("周三", ToolExecutor.NormalizeWeekDayName("星期三"));
        Assert.Equal("周三", ToolExecutor.NormalizeWeekDayName("礼拜三"));
    }
}
