using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Tests;

public sealed class AppDbContextMappingTests
{
    private static AppDbContext CreateContext()
    {
        // EF metadata can be inspected without connecting to PostgreSQL.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_tests;Username=tests;Password=unused")
            .Options;
        return new AppDbContext(options);
    }

    [Xunit.Fact]
    public void Users_keep_existing_table_and_column_names()
    {
        using var context = CreateContext();
        var user = context.Model.FindEntityType(typeof(User));
        Xunit.Assert.NotNull(user);
        Xunit.Assert.Equal("app_users", user.GetTableName());

        var table = StoreObjectIdentifier.Table("app_users", null);
        Xunit.Assert.Equal("user_name",
            user.FindProperty(nameof(User.UserName))?.GetColumnName(table));
        Xunit.Assert.Equal("position",
            user.FindProperty(nameof(User.PositionId))?.GetColumnName(table));
        Xunit.Assert.Equal("avatar_content_type",
            user.FindProperty(nameof(User.AvatarContentType))?.GetColumnName(table));
        Xunit.Assert.Equal("NOW()",
            user.FindProperty(nameof(User.CreatedAt))?.GetDefaultValueSql());
    }

    [Xunit.Fact]
    public void Users_keep_unique_indexes_and_restricted_profession_relation()
    {
        using var context = CreateContext();
        var user = context.Model.FindEntityType(typeof(User));
        Xunit.Assert.NotNull(user);

        Xunit.Assert.Contains(user.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Count == 1 &&
                index.Properties[0].Name == nameof(User.UserName));
        Xunit.Assert.Contains(user.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Count == 1 &&
                index.Properties[0].Name == nameof(User.Email));
        Xunit.Assert.Contains(user.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Count == 1 &&
                foreignKey.Properties[0].Name == nameof(User.PositionId) &&
                foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
    }

    [Xunit.Fact]
    public void Professions_keep_existing_table_and_identity_mapping()
    {
        using var context = CreateContext();
        var profession = context.Model.FindEntityType(typeof(Profession));
        Xunit.Assert.NotNull(profession);
        Xunit.Assert.Equal("professions", profession.GetTableName());

        var table = StoreObjectIdentifier.Table("professions", null);
        Xunit.Assert.Equal("profession",
            profession.FindProperty(nameof(Profession.Name))?.GetColumnName(table));
        Xunit.Assert.Equal("gen_random_uuid()",
            profession.FindProperty(nameof(Profession.Id))?.GetDefaultValueSql());
    }
    [Xunit.Fact]
    public void Vehicle_purchases_keep_existing_columns_and_generated_timestamp()
    {
        using var context = CreateContext();
        var purchase = context.Model.FindEntityType(typeof(VehiclePurchaseEntity));
        Xunit.Assert.NotNull(purchase);
        Xunit.Assert.Equal("vehicle_purchase_requests", purchase.GetTableName());

        var table = StoreObjectIdentifier.Table("vehicle_purchase_requests", null);
        Xunit.Assert.Equal("vehicle_id",
            purchase.FindProperty(nameof(VehiclePurchaseEntity.VehicleId))?.GetColumnName(table));
        Xunit.Assert.Equal("created_by",
            purchase.FindProperty(nameof(VehiclePurchaseEntity.CreatedBy))?.GetColumnName(table));
        Xunit.Assert.Equal("quantity",
            purchase.FindProperty(nameof(VehiclePurchaseEntity.Quantity))?.GetColumnName(table));
        Xunit.Assert.Equal("NOW()",
            purchase.FindProperty(nameof(VehiclePurchaseEntity.CreatedAt))?.GetDefaultValueSql());
    }

    [Xunit.Fact]
    public void Material_groups_keep_tables_columns_and_cascade_relationship()
    {
        using var context = CreateContext();
        var group = context.Model.FindEntityType(typeof(MaterialGroupEntity));
        var item = context.Model.FindEntityType(typeof(MaterialGroupItemEntity));
        Xunit.Assert.NotNull(group);
        Xunit.Assert.NotNull(item);
        Xunit.Assert.Equal("material_groups", group.GetTableName());
        Xunit.Assert.Equal("material_group_items", item.GetTableName());
        var itemTable = StoreObjectIdentifier.Table("material_group_items", null);
        Xunit.Assert.Equal("source_table",
            item.FindProperty(nameof(MaterialGroupItemEntity.SourceTable))?.GetColumnName(itemTable));
        Xunit.Assert.Contains(item.GetForeignKeys(),
            fk => fk.DeleteBehavior == DeleteBehavior.Cascade &&
                  fk.Properties.Single().Name == nameof(MaterialGroupItemEntity.GroupId));
    }

}
