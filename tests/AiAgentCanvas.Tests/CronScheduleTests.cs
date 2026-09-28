using AiAgentCanvas.Abstractions;
using Xunit;

namespace AiAgentCanvas.Tests;

public class CronScheduleTests
{
    [Theory]
    [InlineData("0 8 * * *")]
    [InlineData("*/15 * * * *")]
    [InlineData("0 0 1 1 *")]
    [InlineData("30 9-17 * * 1-5")]
    [InlineData("0 0 * * 0")]
    [InlineData("0 0 * * 7")]
    public void Parses_supported_expressions(string expression) =>
        Assert.True(CronSchedule.TryParse(expression, out _));

    [Theory]
    [InlineData("")]
    [InlineData("0 8 * *")]
    [InlineData("0 8 * * * *")]
    [InlineData("60 * * * *")]
    [InlineData("* 24 * * *")]
    [InlineData("every 5 minutes")]
    [InlineData("5m")]
    public void Rejects_unsupported_expressions(string expression) =>
        Assert.False(CronSchedule.TryParse(expression, out _));

    [Fact]
    public void Matches_the_minute_it_names()
    {
        var schedule = CronSchedule.Parse("30 8 * * *");

        Assert.True(schedule.Matches(new DateTimeOffset(2026, 3, 1, 8, 30, 0, TimeSpan.Zero)));
        Assert.False(schedule.Matches(new DateTimeOffset(2026, 3, 1, 8, 31, 0, TimeSpan.Zero)));
        Assert.False(schedule.Matches(new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Step_syntax_matches_every_nth_minute()
    {
        var schedule = CronSchedule.Parse("*/15 * * * *");
        var day = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.True(schedule.Matches(day));
        Assert.True(schedule.Matches(day.AddMinutes(15)));
        Assert.True(schedule.Matches(day.AddMinutes(45)));
        Assert.False(schedule.Matches(day.AddMinutes(20)));
    }

    [Fact]
    public void Day_of_week_range_excludes_the_weekend()
    {
        var schedule = CronSchedule.Parse("0 9 * * 1-5");

        // 2026-03-02 is a Monday, 2026-03-07 a Saturday.
        Assert.True(schedule.Matches(new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero)));
        Assert.False(schedule.Matches(new DateTimeOffset(2026, 3, 7, 9, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Next_occurrence_is_strictly_after_the_given_instant()
    {
        var schedule = CronSchedule.Parse("0 8 * * *");
        var at8 = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);

        Assert.Equal(at8.AddDays(1), schedule.GetNextOccurrence(at8));
    }

    [Fact]
    public void Due_when_a_matching_minute_passed_since_the_last_run()
    {
        var schedule = CronSchedule.Parse("0 8 * * *");
        var lastRun = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);

        Assert.False(schedule.IsDue(lastRun, lastRun.AddHours(5)));
        Assert.True(schedule.IsDue(lastRun, lastRun.AddDays(1)));
    }

    [Fact]
    public void Never_run_recurring_task_is_due_at_its_next_match()
    {
        var schedule = CronSchedule.Parse("*/5 * * * *");
        var now = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);

        Assert.True(schedule.IsDue(null, now));
    }

    [Fact]
    public void Impossible_date_has_no_next_occurrence()
    {
        var schedule = CronSchedule.Parse("0 0 30 2 *");
        Assert.Null(schedule.GetNextOccurrence(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }
}
