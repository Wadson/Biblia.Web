using System.Net;
using Biblia.Domain.Entities;
using Biblia.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Biblia.Tests.Infrastructure;

public sealed class BlockRenderingTests
{
    [Theory]
    [InlineData("1.")]
    [InlineData("2º")]
    [InlineData("•")]
    [InlineData("–")]
    public async Task MultilinePreviewRendersOnePrefixWithStyleAndEscapesHtml(string prefix)
    {
        await using var services=new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        var html=await renderer.Dispatcher.InvokeAsync(async()=>{
            var component=await renderer.RenderComponentAsync<ThemeTextPreview>(ParameterView.FromDictionary(new Dictionary<string,object?>{
                [nameof(ThemeTextPreview.Block)]=new ThemeTextBlock("Primeira\n\n<script>teste</script>",new("#172033","#FFF4CC",14,TextMarkerStyle.OrdinalNumbered,true,true)),
                [nameof(ThemeTextPreview.Prefix)]=prefix
            }));return component.ToHtmlString();
        });
        Assert.Equal(1,html.Split("class=\"block-marker\"").Length-1);
        Assert.Contains(prefix,WebUtility.HtmlDecode(html));Assert.Contains("Primeira\n\n",WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("<script>",html);Assert.Contains("&lt;script&gt;",html);
        Assert.Contains("font-size:14pt",html);Assert.Contains("font-weight:600",html);Assert.Contains("font-style:italic",html);
        Assert.Contains("background:#FFF4CC",html);Assert.Contains("color:#172033",html);
    }

    [Fact]
    public async Task IconIsProportionalAndDecorative()
    {
        await using var services=new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        var html=await renderer.Dispatcher.InvokeAsync(async()=>(await renderer.RenderComponentAsync<AppIcon>(ParameterView.FromDictionary(new Dictionary<string,object?>{[nameof(AppIcon.Name)]="home",[nameof(AppIcon.Size)]="md"}))).ToHtmlString());
        Assert.Contains("viewBox=\"0 0 24 24\"",html);Assert.Contains("aria-hidden=\"true\"",html);Assert.Contains("currentColor",html);Assert.Contains("icon-md",html);
    }
}
