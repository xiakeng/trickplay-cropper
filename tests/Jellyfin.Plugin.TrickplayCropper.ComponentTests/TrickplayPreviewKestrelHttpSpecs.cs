using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.TrickplayCropper.Api;
using Jellyfin.Plugin.TrickplayCropper.Caching;
using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Trickplay;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xunit;

using static Jellyfin.Plugin.TrickplayCropper.ComponentTests.PreviewHttpTestValues;
using static Jellyfin.Plugin.TrickplayCropper.ComponentTests.TrickplayPreviewHttpSupport;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class TrickplayPreviewKestrelHttpSpecs
{
    [Fact]
    public async Task ReturnsTheSelectedGetFrameIndexOverRealKestrelForImageAndConditionalSuccess()
    {
        var scenario = new PreviewScenario
        {
            RequestFrameIndex = 3,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateWithKestrelAsync(scenario);

        using HttpResponseMessage imageResponse = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.OK, imageResponse.StatusCode);
        Assert.False(imageResponse.Headers.Contains("X-Trickplay-Frame-Index"));
        string entityTag = Assert.IsType<string>(imageResponse.Headers.ETag?.Tag);

        using HttpResponseMessage conditionalResponse = await fixture.GetConditionalAsync(entityTag);
        Assert.Equal(HttpStatusCode.NotModified, conditionalResponse.StatusCode);
        Assert.False(conditionalResponse.Headers.Contains("X-Trickplay-Frame-Index"));
    }

}
