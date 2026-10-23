using System.Reflection;
using System.IO.Compression;
using System.Security;
using System.Text;
using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using MyApp.Application.Services;
using Xunit;
namespace MyApp.Infrastructure.Tests;
/// <summary>Тесты сопоставления двигателей при импорте показаний.</summary>
public sealed class VehicleHoursEngineImportTests
{
    /// <summary>Проверяет импорт в указанный ДВС без угадывания номера двигателя.</summary>
    [Theory]
    [InlineData("901;Truck;875;852", 2, 0, ".csv")]
    [InlineData("901;Truck;875/852", 2, 0, ".csv")]
    [InlineData("94;Excavator;20511;", 1, 0, ".csv")]
    [InlineData("94;Excavator;20511", 1, 0, ".csv")]
    [InlineData("901;Truck;875", 0, 1, ".csv")]
    [InlineData("94;Excavator;10;20", 0, 1, ".csv")]
    [InlineData("901;Truck;-1;20", 0, 1, ".csv")]
    [InlineData("901;Truck;;852", 1, 0, ".csv")]
    [InlineData("901;Truck;875;852", 2, 0, ".xlsx")]
    [InlineData("94;Excavator;20511;", 1, 0, ".xlsx")]
    public async Task Imports_correct_engine_without_guessing(string data, int count, int errors, string extension)
    {
        var vehicles = new VehicleResponse[] {new(Guid.NewGuid(),"Group","Самосвал","Truck",901,"",""),new(Guid.NewGuid(),"Group","Экскаватор","Excavator",94,"","")};
        IReadOnlyList<VehicleHoursImportItem> captured=[];
        var repository=DispatchProxy.Create<IVehicleRepository, Proxy>();
        ((Proxy)(object)repository).Handler=(method,args)=>method.Name switch {
            nameof(IVehicleRepository.GetVehiclesAsync)=>Task.FromResult<IReadOnlyList<VehicleResponse>>(vehicles),
            nameof(IVehicleRepository.ImportHoursAsync)=>Capture(args),
            _=>throw new InvalidOperationException(method.Name)
        };
        /** Перехватывает аргументы вызова репозитория для проверки выбранного двигателя. */
        Task Capture(object?[] args){captured=(IReadOnlyList<VehicleHoursImportItem>)args[1]!;Assert.Equal(new DateOnly(2026,10,11),args[0]);return Task.CompletedTask;}
        var users=DispatchProxy.Create<IUserRepository, Proxy>();
        ((Proxy)(object)users).Handler = (_, _) => Task.FromResult<MyApp.Application.Models.User?>(new() {
            Id = Guid.NewGuid(), FirstName = "Иван", MiddleName = "", LastName = "Иванов",
            UserName = "mechanic", Email = "mechanic@local", PasswordHash = "test", Role = "user",
            Profession = new() { Id = Guid.NewGuid(), Name = "Механик" }
        });
        var service=new VehicleService(repository,users);
        using var input=extension==".csv" ? new MemoryStream(Encoding.UTF8.GetBytes(data)) : Excel(data);
        var result=await service.ImportHoursAsync(input,extension,Guid.NewGuid(),new(2026,10,11),default);
        Assert.Equal(MyApp.Application.Common.ServiceResultStatus.Success, result.Status);
        Assert.Equal(count,result.Value!.Imported);Assert.Equal(errors,result.Value.Errors.Count);Assert.Equal(count,captured.Count);
        Assert.All(captured,item=>Assert.InRange(item.EngineNumber,1,2));
        if(count==2){Assert.Equal(875,captured[0].EngineHours);Assert.Equal(852,captured[1].EngineHours);Assert.Equal(1,captured[0].EngineNumber);Assert.Equal(2,captured[1].EngineNumber);}
        if(data.Contains(";;852"))Assert.Equal(2,Assert.Single(captured).EngineNumber);
    }
    /// <summary>Собирает тестовый Excel с показаниями двигателей.</summary>
    private static MemoryStream Excel(string row)
    {
        var output=new MemoryStream();
        using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))
        {
            /** Добавляет XML-часть в ZIP-архив тестовой книги Excel. */
            void Entry(string name,string xml){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(xml);}
            Entry("[Content_Types].xml", """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>""");
            Entry("_rels/.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Entry("xl/workbook.xml", """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Hours" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Entry("xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");
            var cells=string.Concat(row.Split(';').Select((value,index)=>$"<c r=\"{(char)('A'+index)}1\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(value)}</t></is></c>"));
            Entry("xl/worksheets/sheet1.xml", $"<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\">{cells}</row></sheetData></worksheet>");
        }
        output.Position=0;return output;
    }
    /// <summary>Тестовый прокси, передающий вызовы зависимости заданному обработчику.</summary>
    public class Proxy:DispatchProxy { public Func<MethodInfo,object?[],object?> Handler {get;set;}=null!;
        /// <summary>Передаёт вызов зависимости настроенному тестовому обработчику.</summary>
        protected override object? Invoke(MethodInfo? method,object?[]? args)=>Handler(method!,args!); }
}
