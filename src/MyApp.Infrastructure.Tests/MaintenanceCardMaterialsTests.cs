using MyApp.Application.DTO;
using MyApp.Application.Services;
using Xunit;

namespace MyApp.Infrastructure.Tests;
public sealed class MaintenanceCardMaterialsTests
{
    private static CardTemplate Template() => new("Sany SET150","ТО-700","Excel","",
        [new("operation","ДВС","Заменить масло")],[new("excel","ДВС","Старая норма","","л",100)]);
    private static MaintenanceEquipmentResponse Source(string name="Самосвалы",string service="ТО-700",decimal quantity=80) =>
        new(Guid.NewGuid(),name,[new(Guid.NewGuid(),service,[new(Guid.NewGuid(),"Масло из ТО","л",quantity,0)])]);
    [Fact] public void Loads_category_norm_even_when_stock_is_empty_and_replaces_excel_rows()
    {
        var source=Source();var (card,error)=MaintenanceCardMaterials.Resolve(Template(),"Sany SET150","Самосвал",[source]);
        Assert.Null(error);var material=Assert.Single(card!.Materials);
        Assert.Equal(80,material.Quantity);Assert.Equal("Масло из ТО",material.Name);Assert.StartsWith("maintenance:",material.Id);
        Assert.Equal("Самосвалы / ТО-700",card.MaterialSource);Assert.Equal(source.Intervals[0].Id,card.MaterialIntervalId);
        Assert.Equal("Старая норма",Template().Materials[0].Name);
    }
    [Fact] public void Exact_model_wins_over_generic_category()
    {
        var (card,error)=MaintenanceCardMaterials.Resolve(Template(),"Sany SET150","Самосвал",[Source(),Source("SANY SET150",quantity:95)]);
        Assert.Null(error);Assert.Equal(95,Assert.Single(card!.Materials).Quantity);
    }
    [Theory]
    [InlineData("Карьерный самосвал")]
    [InlineData("Карьерные самосвалы")]
    [InlineData(" карьерный САМОСВАЛ ")]
    public void Quarry_dump_truck_uses_dump_truck_materials(string type)
    {
        var source=Source();
        var (card,error)=MaintenanceCardMaterials.Resolve(Template(),"Sany SET150",type,[Source("Экскаваторы"),source]);
        Assert.Null(error);Assert.Equal(source.Intervals[0].Id,card!.MaterialIntervalId);
        Assert.Equal("Самосвалы / ТО-700",card.MaterialSource);
    }
    [Fact] public void Explicit_link_handles_custom_equipment_names()
    {
        var source=Source("Самосвалы участка 1");
        var (card,error)=MaintenanceCardMaterials.Resolve(Template() with {MaterialIntervalId=source.Intervals[0].Id},"Sany SET150","Самосвал",[source]);
        Assert.Null(error);Assert.Equal(source.Intervals[0].Id,card!.MaterialIntervalId);
    }
    [Fact] public void Missing_ambiguous_and_wrong_service_sources_are_not_guessed()
    {
        Assert.NotNull(MaintenanceCardMaterials.Resolve(Template(),"Sany SET150","Самосвал",[]).Error);
        Assert.NotNull(MaintenanceCardMaterials.Resolve(Template(),"Sany SET150","Самосвал",[Source(),Source()]).Error);
        var wrong=Source(service:"ТО-1000");
        Assert.NotNull(MaintenanceCardMaterials.Resolve(Template() with {MaterialIntervalId=wrong.Intervals[0].Id},"Sany SET150","Самосвал",[wrong]).Error);
    }
    [Fact] public void Next_card_uses_updated_norm_while_existing_snapshot_keeps_old_norm()
    {
        var source=Source();var first=MaintenanceCardMaterials.Resolve(Template(),"Sany SET150","Самосвал",[source]).Template!;
        var interval=source.Intervals[0];var changed=source with {Intervals=[interval with {Items=[interval.Items[0] with {Quantity=90}]}]};
        var second=MaintenanceCardMaterials.Resolve(Template(),"Sany SET150","Самосвал",[changed]).Template!;
        Assert.Equal(80,first.Materials[0].Quantity);Assert.Equal(90,second.Materials[0].Quantity);Assert.Equal(first.Materials[0].Id,second.Materials[0].Id);
    }
    [Fact] public void Empty_configured_interval_is_different_from_missing_source()
    {
        var source=Source();source=source with {Intervals=[source.Intervals[0] with {Items=[]}]};
        var result=MaintenanceCardMaterials.Resolve(Template(),"Sany SET150","Самосвал",[source]);
        Assert.Null(result.Error);Assert.Empty(result.Template!.Materials);
    }
}
