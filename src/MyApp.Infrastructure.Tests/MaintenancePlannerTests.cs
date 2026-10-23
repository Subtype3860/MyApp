using MyApp.Application.DTO;
using MyApp.Application.Services;
using Xunit;

namespace MyApp.Infrastructure.Tests;

public sealed class MaintenancePlannerTests
{
    [Theory]
    [InlineData(16, 0)]
    [InlineData(17, 11)]
    [InlineData(23, 143)]
    public void Missing_hours_use_full_days_and_half_target_day(int day, int added)
    {
        var source = new DateOnly(2026,10,16);
        var reading = MaintenancePlanner.Estimate(2, 1000, source, new DateOnly(2026,10,day));
        Assert.Equal(1000 + added, reading.Hours);
        Assert.Equal(day > 16, reading.IsEstimated);
        Assert.Equal(source, reading.SourceDate);
        Assert.Equal(1000, reading.SourceHours);
    }

    private static readonly DateOnly Date = new(2026, 10, 23);
    private static MaintenancePlan Plan() => new("Test",
        [new("ТО-250",250),new("ТО-500",250),new("ТО-250",250),new("ТО-1000",250)],
        1, Date, "ТО-250", [new(1,250)], []);

    [Theory]
    [InlineData(480,730)]
    [InlineData(530,780)]
    public void Actual_service_reading_restarts_the_next_interval(int actual, int next)
    {
        var plan=MaintenancePlanner.Complete(Plan(),new(1,Date,[new(1,actual)],"ТО"));
        var due=Assert.Single(MaintenancePlanner.Due(plan,[new(1,actual,Date)]));
        Assert.Equal(next,due.DueHours);Assert.Equal(2,plan.NextStepIndex);
    }

    [Fact]
    public void Second_cycle_uses_300_hours_between_700_and_1000()
    {
        var plan=Plan() with { Steps=[new("ТО-350",350),new("ТО-700",350),new("ТО-1000",300)],NextStepIndex=2,Engines=[new(1,700)] };
        var due=Assert.Single(MaintenancePlanner.Due(plan,[new(1,850,Date)]));
        Assert.Equal(1000,due.DueHours);Assert.Equal(150,due.Remaining);
    }

    [Fact]
    public void Last_cycle_step_wraps_to_first()
    {
        var plan=Plan() with {NextStepIndex=3};
        var after=MaintenancePlanner.Complete(plan,new(1,Date,[new(1,4100)],"ТО"));
        Assert.Equal(0,after.NextStepIndex);
        Assert.Equal(4350,Assert.Single(MaintenancePlanner.Due(after,[new(1,4200,Date)])).DueHours);
    }

    [Fact]
    public void Largest_increment_wins_not_largest_absolute_meter()
    {
        var plan=Plan() with { Engines=[new(1,20000),new(2,500)] };
        var due=Assert.Single(MaintenancePlanner.Due(plan,[new(1,20080,Date),new(2,700,Date)]));
        Assert.Equal(2,due.EngineNumber);Assert.Equal(50,due.Remaining);Assert.Equal(750,due.DueHours);
    }

    [Fact]
    public void Run_in_completion_does_not_move_regular_cycle()
    {
        var id=Guid.NewGuid();
        var plan=Plan() with {RunIns=[new(id,"Замена ДВС",Date,150,[new(1,300)])]};
        var due=MaintenancePlanner.Due(plan,[new(1,400,Date)]);
        Assert.Equal("run-in",due[0].Kind);Assert.Equal(50,due[0].Remaining);
        var after=MaintenancePlanner.Complete(plan,new(1,Date,[new(1,460)],"Обкатка",id));
        Assert.Equal(plan.Engines,after.Engines);Assert.Equal(plan.NextStepIndex,after.NextStepIndex);
        Assert.Empty(after.RunIns);Assert.Equal(500,Assert.Single(MaintenancePlanner.Due(after,[new(1,460,Date)])).DueHours);
    }

    [Theory]
    [InlineData(200,0)]
    [InlineData(300,-1)]
    public void Inconsistent_or_older_readings_are_not_used(int hours,int days)
    {
        Assert.Empty(MaintenancePlanner.Due(Plan(),[new(1,hours,Date.AddDays(days))]));
    }

    [Fact]
    public void Missing_engine_reading_prevents_a_false_deadline()
    {
        var plan=Plan() with {Engines=[new(1,250),new(2,250)]};
        Assert.Empty(MaintenancePlanner.Due(plan,[new(1,400,Date)]));
    }

    [Fact]
    public void Schedule_revision_changes_next_interval_without_rewriting_baseline()
    {
        var plan=Plan() with {Steps=[new("Новое ТО",350)],NextStepIndex=0};
        Assert.Equal(600,Assert.Single(MaintenancePlanner.Due(plan,[new(1,400,Date)])).DueHours);
        Assert.Equal(250,plan.Engines[0].Hours);
    }

    [Fact]
    public void Overdue_keeps_negative_remaining()
    {
        Assert.Equal(-30,Assert.Single(MaintenancePlanner.Due(Plan(),[new(1,530,Date)])).Remaining);
    }

    [Fact]
    public void Validate_rejects_bad_steps_and_duplicate_engines()
    {
        Assert.Null(MaintenancePlanner.Validate(Plan(),1,Date));
        Assert.NotNull(MaintenancePlanner.Validate(Plan() with {Steps=[new("ТО",0)]},1,Date));
        Assert.NotNull(MaintenancePlanner.Validate(Plan() with {Engines=[new(1,100),new(1,200)]},2,Date));
        Assert.NotNull(MaintenancePlanner.Validate(Plan() with {NextStepIndex=10},1,Date));
        Assert.NotNull(MaintenancePlanner.Validate(Plan() with {LastDate=Date.AddDays(1)},1,Date));
    }
}

