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


internal static class PreviewHttpMetadataFake
{
    internal static ITrickplayManager CreateTrickplayManager(PreviewHostContext context)
    {
        InterfaceMockSpecs<ITrickplayManager> mock = InterfaceMock.Create<ITrickplayManager>();
        mock.Handle("GetTrickplayResolutions", arguments =>
        {
            context.Scenario.RecordMetadataRead();
            MetadataReadPlan? plan = context.Scenario.BeginMetadataRead();
            if (context.Scenario.MutatesConfiguredTargetsDuringMetadataRead)
            {
                context.Scenario.ConfiguredWidthResolutions![0] = 640;
            }

            return Equals(arguments?[0], context.Scenario.SelectedSourceId)
                ? ReadResolutionsAsync(context.Scenario, plan)
                : Task.FromResult(new Dictionary<int, TrickplayInfo>());
        });
        mock.Handle("GetTrickplayTilePathAsync", arguments => ResolveSourceSpritePath(arguments, context));
        return mock.Service;
    }

    private static async Task<Dictionary<int, TrickplayInfo>> ReadResolutionsAsync(
        PreviewScenario scenario,
        MetadataReadPlan? plan)
    {
        try
        {
            if (plan is not null)
            {
                await plan.WaitForReleaseAsync();
                if (plan.Outcome == MetadataReadOutcome.Failed)
                {
                    throw new IOException("The scripted metadata read failed.");
                }
            }

            MetadataAvailability availability = plan?.Availability ?? scenario.Metadata;
            return CreateResolutions(scenario, availability);
        }
        finally
        {
            plan?.MarkCompleted();
        }
    }

    private static Dictionary<int, TrickplayInfo> CreateResolutions(
        PreviewScenario scenario,
        MetadataAvailability availability)
    {
        TrickplayInfo metadata = CreateMetadata(scenario, availability);
        return availability switch
        {
            MetadataAvailability.ExactWidthMissing => new Dictionary<int, TrickplayInfo> { [640] = metadata },
            MetadataAvailability.GeneratedMetadataMissing => new Dictionary<int, TrickplayInfo>(),
            MetadataAvailability.MultipleWidths => new Dictionary<int, TrickplayInfo>
            {
                [320] = metadata,
                [640] = CreateMetadata(scenario, MetadataAvailability.ChangedWidth),
            },
            _ => new Dictionary<int, TrickplayInfo> { [320] = metadata },
        };
    }

    private static TrickplayInfo CreateMetadata(
        PreviewScenario scenario,
        MetadataAvailability availability)
    {
        var metadata = new TrickplayInfo
        {
            ItemId = scenario.SelectedSourceId,
            Width = 320,
            Height = 180,
            TileWidth = 2,
            TileHeight = 2,
            ThumbnailCount = 4,
            Interval = scenario.MetadataIntervalMilliseconds,
        };
        switch (availability)
        {
            case MetadataAvailability.Available:
            case MetadataAvailability.GeneratedMetadataMissing:
            case MetadataAvailability.MultipleWidths:
                {
                    break;
                }

            case MetadataAvailability.ChangedInterval:
                {
                    metadata.Interval = 20_000;
                    break;
                }

            case MetadataAvailability.ChangedWidth:
                {
                    metadata.Width = 640;
                    metadata.Interval = 20_000;
                    break;
                }

            case MetadataAvailability.ContradictoryFrameWidth:
            case MetadataAvailability.ExactWidthMissing:
                {
                    metadata.Width = 640;
                    break;
                }

            case MetadataAvailability.CropXOverflow:
            case MetadataAvailability.CropRightOverflow:
                {
                    metadata.Interval = 1;
                    metadata.TileWidth = 10_000_000;
                    metadata.TileHeight = 1;
                    metadata.ThumbnailCount = 10_000_000;
                    break;
                }

            case MetadataAvailability.CropYOverflow:
                {
                    metadata.Height = int.MaxValue;
                    metadata.TileWidth = 1;
                    metadata.TileHeight = 3;
                    metadata.ThumbnailCount = 3;
                    metadata.Interval = 1;
                    break;
                }

            case MetadataAvailability.CropBottomOverflow:
                {
                    metadata.Height = int.MaxValue;
                    metadata.TileWidth = 1;
                    metadata.TileHeight = 2;
                    metadata.ThumbnailCount = 2;
                    metadata.Interval = 1;
                    break;
                }

            case MetadataAvailability.FrameHeightZero:
                {
                    metadata.Height = 0;
                    break;
                }

            case MetadataAvailability.FrameWidthZero:
                {
                    metadata.Width = 0;
                    break;
                }

            case MetadataAvailability.IntervalZero:
                {
                    metadata.Interval = 0;
                    break;
                }

            case MetadataAvailability.NegativeThumbnails:
                {
                    metadata.ThumbnailCount = -1;
                    break;
                }

            case MetadataAvailability.NoThumbnails:
                {
                    metadata.ThumbnailCount = 0;
                    break;
                }

            case MetadataAvailability.TileHeightZero:
                {
                    metadata.TileHeight = 0;
                    break;
                }

            case MetadataAvailability.TileWidthZero:
                {
                    metadata.TileWidth = 0;
                    break;
                }

            default:
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(scenario),
                        availability,
                        "Unknown metadata scenario.");
                }
        }

        return metadata;
    }

    private static Task<string> ResolveSourceSpritePath(object?[]? arguments, PreviewHostContext context)
    {
        context.Scenario.RecordSourceSpritePathRequest();
        int expectedResolution = context.Scenario.Metadata == MetadataAvailability.MultipleWidths ? 640 : 320;
        if (!Equals(arguments?[1], expectedResolution)
            || !Equals(arguments?[2], 0)
            || !Equals(arguments?[3], false))
        {
            throw new InvalidOperationException("The Source Sprite path request was not normalized.");
        }

        string path = context.Scenario.SourceSprite switch
        {
            SourceSpriteAvailability.Available => context.SourceSpritePath,
            SourceSpriteAvailability.DimensionMismatch => context.SourceSpritePath,
            SourceSpriteAvailability.ManagerFailure => throw new IOException(
                $"manager-secret SourceSpritePath={context.SourceSpritePath}"),
            SourceSpriteAvailability.ManagerPathMissing => string.Empty,
            SourceSpriteAvailability.FileMissing => Path.Combine(
                context.TemporaryDirectory,
                "missing-source-sprite.jpg"),
            _ => throw new InvalidOperationException("Unknown Source Sprite scenario."),
        };
        return Task.FromResult(path);
    }

}
