using System.Text.Json;
using MyApp.Application.DTO;
using MyApp.Application.Services;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Db.Entities;
using Xunit;

namespace MyApp.Infrastructure.Tests;
public sealed class MaintenanceCardRulesTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static CardTemplate Template() => new("SANY SY1250H","ТО-1000","test","",
        [new("work","ДВС","Проверка"),new("control","Контроль","Запуск",Control:true)],
        [new("oil","ДВС","Масло","","л",80)]);
    private static CardData Data() => new(Today,"","",null,null,DateTimeOffset.UtcNow.AddHours(-2),DateTimeOffset.UtcNow.AddHours(-1),null,"",
        [new("work","done","ok","","Иванов",""),new("control","done","","","Петров","")],
        [new("oil",80,"","Иванов")]);
    [Fact] public void Blank_draft_is_allowed_but_not_submitted()
    {
        var d=Data() with { Answers=[new("work","","","","",""),new("control","","","","","")] };
        Assert.Null(MaintenanceCardRules.Validate(Template(),d,Today,false));
        Assert.NotNull(MaintenanceCardRules.Validate(Template(),d,Today,true));
    }
    [Fact] public void Final_control_cannot_be_skipped_even_with_reason()
    {
        var d=Data();d.Answers[1]=d.Answers[1] with { Status="na",Reason="Нет времени" };
        Assert.NotNull(MaintenanceCardRules.Validate(Template(),d,Today,true));
    }
    [Fact] public void Inspection_can_be_complete_and_find_a_defect()
    {
        var d=Data();d.Answers[0]=d.Answers[0] with { Condition="defect",Note="Течь" };
        Assert.Null(MaintenanceCardRules.Validate(Template(),d,Today,true));
        d.Answers[0]=d.Answers[0] with { Note="" };
        Assert.NotNull(MaintenanceCardRules.Validate(Template(),d,Today,true));
    }
    [Fact] public void Zero_consumption_differs_from_missing_value()
    {
        var d=Data() with { Materials=[new("oil",null,"","")] };
        Assert.NotNull(MaintenanceCardRules.Validate(Template(),d,Today,true));
        Assert.Null(MaintenanceCardRules.Validate(Template(),d with { Materials=[new("oil",0,"","")] },Today,true));
    }
    [Fact] public void Substituted_rows_and_reversed_times_are_rejected()
    {
        var d=Data();d.Answers[0]=d.Answers[0] with { Id="another-row" };
        Assert.NotNull(MaintenanceCardRules.Validate(Template(),d,Today,false));
        Assert.NotNull(MaintenanceCardRules.Validate(Template(),Data() with { StoppedAt=DateTimeOffset.UtcNow },Today,false));
    }
    [Fact] public void Seed_library_contains_both_fleets_and_no_personal_header_data()
    {
        using var stream=typeof(MaintenanceCardSchema).Assembly.GetManifestResourceStream("MyApp.Infrastructure.Db.Seeds.maintenance-cards.json")!;
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var seeds=JsonSerializer.Deserialize<MaintenanceCardTemplate[]>(stream,options)!;
        Assert.Equal(17,seeds.Length);Assert.All(seeds,s=>Assert.False(s.Published));
        var templates=seeds.Select(s=>JsonSerializer.Deserialize<CardTemplate>(s.Snapshot,options)!).ToArray();
        foreach(var model in new[]{"SANY SET150S","SANY SY1250H","KOMATSU D375А-6"})Assert.Contains(templates,t=>t.Model==model);
        Assert.All(templates,t=>{Assert.NotEmpty(t.Operations);Assert.Equal(t.Operations.Length,t.Operations.Select(o=>o.Id).Distinct().Count());});
        Assert.Contains(templates.Single(t=>t.Model=="SANY SY1250H"&&t.Service=="ТО-4000").Operations,o=>o.Control);
    }
    [Fact] public void Repeated_1000_step_does_not_restart_the_cycle()
    {
        int[] names=[350,700,1000,350,700,2000,350,700,1000,350,700,4000];
        var plan=new MaintenancePlan("350–4000",names.Select((n,i)=>new MaintenanceStep("ТО-"+n,i%3==2?300:350)).ToArray(),5,Today,"ТО-700",[new(1,1700)],[]);
        var actual=1700m;
        foreach(var expected in new[]{"ТО-2000","ТО-350","ТО-700","ТО-1000","ТО-350","ТО-700","ТО-4000"})
        {
            Assert.Equal(expected,plan.Steps[plan.NextStepIndex].Name);
            actual+=plan.Steps[plan.NextStepIndex].Interval;
            plan=MaintenancePlanner.Complete(plan,new(1,Today,[new(1,actual)],"Карта"));
            Assert.Equal(expected,plan.LastService);
        }
        Assert.Equal(0,plan.NextStepIndex);Assert.Equal(4000,actual);
    }
}
