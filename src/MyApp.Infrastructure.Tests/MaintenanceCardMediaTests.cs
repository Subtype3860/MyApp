using MyApp.Application.Services;
using Xunit;

namespace MyApp.Infrastructure.Tests;
public sealed class MaintenanceCardMediaTests
{
    [Fact] public void Detects_allowed_content_from_bytes()
    {
        Assert.Equal("image/png",MaintenanceCardMedia.ContentType([137,80,78,71,13,10,26,10]));
        Assert.Equal("image/jpeg",MaintenanceCardMedia.ContentType([255,216,255,224,0,16,74,70]));
        Assert.Equal("video/mp4",MaintenanceCardMedia.ContentType([0,0,0,24,102,116,121,112,105,115,111,109]));
        Assert.Equal("video/webm",MaintenanceCardMedia.ContentType([0x1a,0x45,0xdf,0xa3,0x42,0x82,0x84,119,101,98,109]));
    }
    [Fact] public void Rejects_unknown_and_oversize_files()
    {
        Assert.Null(MaintenanceCardMedia.ContentType("<svg>hello</svg>"u8));
        Assert.Null(MaintenanceCardMedia.ContentType([1,2,3]));
        var photo=new byte[MaintenanceCardMedia.PhotoLimit+1];photo[0]=255;photo[1]=216;photo[2]=255;
        Assert.Null(MaintenanceCardMedia.ContentType(photo));
        var video=new byte[MaintenanceCardMedia.VideoLimit+1];"ftypisom"u8.CopyTo(video.AsSpan(4));
        Assert.Null(MaintenanceCardMedia.ContentType(video));
    }
    [Fact] public void Does_not_accept_other_iso_media_as_mp4()
    {
        Assert.Null(MaintenanceCardMedia.ContentType([0,0,0,24,102,116,121,112,104,101,105,99]));
        Assert.Null(MaintenanceCardMedia.ContentType([0,0,0,24,102,116,121,112,113,116,32,32]));
    }
}
