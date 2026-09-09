using System.Text.Json;
using Biblia.Domain.Entities;
using Biblia.Domain.Rules;
using Xunit;

namespace Biblia.Tests.Domain;

public sealed class ThemeBlockMarkersTests
{
    [Theory]
    [InlineData(0, TextMarkerStyle.None)]
    [InlineData(1, TextMarkerStyle.Bullet)]
    [InlineData(2, TextMarkerStyle.Numbered)]
    [InlineData(3, TextMarkerStyle.Dash)]
    [InlineData(4, TextMarkerStyle.OrdinalNumbered)]
    public void StoredNumericEnumIsCompatible(int value, TextMarkerStyle expected)
    {
        var block=JsonSerializer.Deserialize<ThemeTextBlock>(JsonSerializer.Serialize(new {Content="Texto",Style=new {MarkerStyle=value}}))!.Validate();
        Assert.Equal(expected,block.Style.MarkerStyle);
        Assert.Equal(block,JsonSerializer.Deserialize<ThemeTextBlock>(JsonSerializer.Serialize(block)));
    }

    [Fact]
    public void MixedSequenceCountsBlocksOnlyAndIgnoresIds()
    {
        ThemeContentItem Block(long id, TextMarkerStyle marker) => new(id,42,0,null,new("Primeira\n\nSegunda",new(MarkerStyle:marker)));
        ThemeContentItem[] items=[Block(990,TextMarkerStyle.None),Block(72,TextMarkerStyle.Numbered),new(33,42,0,1,null),Block(3,TextMarkerStyle.Bullet),Block(1,TextMarkerStyle.OrdinalNumbered),Block(88,TextMarkerStyle.Dash),Block(2,TextMarkerStyle.Numbered)];
        var marked=ThemeBlockMarkers.Apply(items,x=>x.TextBlock);
        Assert.Equal(new[]{"","1.","","•","2º","–","3."},marked.Select(x=>x.Prefix));
        Assert.Equal(new[]{"Primeira","","Segunda"},items[1].TextBlock!.Lines());
        Assert.Equal(new[]{"1.","–","2º","•","","3.",""},ThemeBlockMarkers.Apply(items.Reverse(),x=>x.TextBlock).Select(x=>x.Prefix));
        Assert.Equal("1.",ThemeBlockMarkers.Apply(new[]{items[6]},x=>x.TextBlock)[0].Prefix);
    }
}
