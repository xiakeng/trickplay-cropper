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


internal static class PreviewHttpJellyfinFakes
{
    internal static void Register(
        IServiceCollection services,
        PreviewHostContext context,
        IApplicationPaths applicationPaths)
    {
        User user = CreateUser(context.Scenario);
        services.AddSingleton(user);
        services.AddSingleton(CreateUserManager(context.Scenario, user));
        services.AddSingleton(CreateLibraryManager(context.Scenario, user));
        services.AddSingleton(CreateMediaSourceManager(context.Scenario, user));
        services.AddSingleton(CreateServerConfigurationManager(context.Scenario));
        services.AddSingleton(PreviewHttpMetadataFake.CreateTrickplayManager(context));
        services.AddSingleton(applicationPaths);
    }

    private static User CreateUser(PreviewScenario scenario)
    {
        var user = new User("component-user", "test-provider", "test-reset-provider")
        {
            Id = scenario.UserId,
        };
        SetPlaybackPermission(user, !scenario.DeniesLogicalVideoPlayback);
        return user;
    }

    private static IUserManager CreateUserManager(PreviewScenario scenario, User user)
    {
        InterfaceMockSpecs<IUserManager> mock = InterfaceMock.Create<IUserManager>();
        mock.Handle("GetUserById", arguments =>
        {
            scenario.RecordUserLookup();
            return Equals(arguments?[0], user.Id) ? user : null;
        });
        return mock.Service;
    }

    private static ILibraryManager CreateLibraryManager(PreviewScenario scenario, User user)
    {
        var logicalVideo = new Video
        {
            Id = scenario.LogicalItemId,
            Name = scenario.LogicalTitle,
            Path = scenario.SelectedMediaPath,
        };
        var alternateVideo = new Video
        {
            Id = scenario.ReturnsMismatchedSourceIdentity ? OtherItemId : AlternateSourceId,
            Name = scenario.SelectedTitle,
            Path = scenario.SelectedMediaPath,
        };
        var context = new VideoLookupContext
        {
            AlternateVideo = alternateVideo,
            LogicalVideo = logicalVideo,
            Scenario = scenario,
            User = user,
        };

        InterfaceMockSpecs<ILibraryManager> mock = InterfaceMock.Create<ILibraryManager>();
        mock.Handle("GetItemById", arguments => ResolveVideoLookup(arguments, context));
        mock.Handle("GetLibraryOptions", arguments =>
            ReferenceEquals(arguments?[0], context.LogicalVideo)
                || ReferenceEquals(arguments?[0], context.AlternateVideo)
                ? new LibraryOptions { SaveTrickplayWithMedia = false }
                : throw new InvalidOperationException("Library options were requested for the wrong video."));
        return mock.Service;
    }

    private static Video? ResolveVideoLookup(object?[]? arguments, VideoLookupContext context)
    {
        bool isUserScoped = arguments?.Length == 2 && ReferenceEquals(arguments[1], context.User);
        bool isUserIndependent = arguments?.Length == 1;
        if ((!isUserScoped && !isUserIndependent) || arguments?[0] is not Guid requestedId)
        {
            throw new InvalidOperationException("The video lookup did not use a supported Jellyfin overload.");
        }

        context.Scenario.LibraryLookupIds.Add(requestedId);
        if (isUserScoped)
        {
            context.Scenario.RecordUserScopedLibraryLookup();
        }
        else
        {
            context.Scenario.RecordUserIndependentLibraryLookup();
        }

        if (requestedId == context.Scenario.LogicalItemId)
        {
            return ResolveVisibleVideo(
                context.LogicalVideo,
                context.Scenario.LogicalVideo,
                isUserScoped);
        }

        if (requestedId != AlternateSourceId)
        {
            return null;
        }

        if (isUserScoped && context.Scenario.DeniesSelectedVideoPlayback)
        {
            SetPlaybackPermission(context.User, false);
        }

        return ResolveVisibleVideo(
            context.AlternateVideo,
            context.Scenario.SelectedVideo,
            isUserScoped);
    }

    private static Video? ResolveVisibleVideo(
        Video video,
        ItemAvailability availability,
        bool isUserScoped)
    {
        return availability switch
        {
            ItemAvailability.Available => video,
            ItemAvailability.Hidden when !isUserScoped => video,
            ItemAvailability.Hidden => null,
            ItemAvailability.Missing => null,
            ItemAvailability.WrongType => null,
            _ => throw new ArgumentOutOfRangeException(
                nameof(availability),
                availability,
                "Unknown Item availability."),
        };
    }

    private static IMediaSourceManager CreateMediaSourceManager(PreviewScenario scenario, User user)
    {
        InterfaceMockSpecs<IMediaSourceManager> mock = InterfaceMock.Create<IMediaSourceManager>();
        mock.Handle("GetPlaybackMediaSources", arguments =>
        {
            if (arguments?.Length != 5
                || arguments[0] is not Video video
                || video.Id != scenario.LogicalItemId)
            {
                throw new InvalidOperationException("Playback sources were not enumerated for the logical video.");
            }

            if (ReferenceEquals(arguments[1], user))
            {
                if (!Equals(arguments[2], true) || !Equals(arguments[3], false))
                {
                    throw new InvalidOperationException("GET changed its Jellyfin source-enumeration contract.");
                }

                scenario.RecordUserScopedSourceEnumeration();
            }
            else if (arguments[1] is null)
            {
                if (!Equals(arguments[2], false) || !Equals(arguments[3], false))
                {
                    throw new InvalidOperationException("The probe did not explicitly disable host media probing.");
                }

                scenario.RecordUserIndependentSourceEnumeration();
            }
            else
            {
                throw new InvalidOperationException("Playback sources used an unexpected Jellyfin user.");
            }

            return ReadMediaSourcesAsync(
                scenario,
                scenario.BeginSourceRead(),
                (CancellationToken)arguments[4]!);
        });
        return mock.Service;
    }

    private static async Task<IReadOnlyList<MediaSourceInfo>> ReadMediaSourcesAsync(
        PreviewScenario scenario,
        SourceReadPlan? plan,
        CancellationToken cancellationToken)
    {
        if (plan is not null)
        {
            await plan.WaitForReleaseAsync(cancellationToken);
        }

        SourceMembership membership = plan?.Membership ?? scenario.Membership;
        int? sourceVideoWidth = plan is null ? scenario.SourceVideoWidth : plan.SourceVideoWidth;
        string memberId = membership switch
        {
            SourceMembership.Member => scenario.SelectedSourceId.ToString("D"),
            SourceMembership.NotMember => UnavailableSourceId.ToString("D"),
            SourceMembership.Malformed => "not-a-guid",
            _ => throw new InvalidOperationException("Unknown source-membership scenario."),
        };
        var mediaSource = new MediaSourceInfo
        {
            Id = memberId,
            OpenToken = scenario.HostSource == HostSourceKind.EligibleDynamic
                ? "component-provider-token"
                : null,
            Path = scenario.HostSource switch
            {
                HostSourceKind.LinkedAlternate => "/media/resolved-linked-target.mkv",
                HostSourceKind.EligibleDynamic => "https://provider.invalid/dynamic-stream",
                _ => scenario.SelectedMediaPath,
            },
        };
        if (sourceVideoWidth is int width)
        {
            mediaSource.MediaStreams =
            [
                new MediaStream
                {
                    Type = MediaStreamType.Video,
                    IsDefault = true,
                    Width = width,
                },
            ];
        }

        return [mediaSource];
    }

    private static IServerConfigurationManager CreateServerConfigurationManager(PreviewScenario scenario)
    {
        var configuration = new ServerConfiguration
        {
            TrickplayOptions = new TrickplayOptions
            {
                // The unreadable-snapshot scenario deliberately stores null in this non-nullable
                // property to reproduce a configuration deserialized without a target array.
                WidthResolutions = scenario.ConfiguredWidthResolutions!,
            },
        };
        InterfaceMockSpecs<IServerConfigurationManager> mock = InterfaceMock.Create<IServerConfigurationManager>();
        mock.Handle("get_Configuration", _ => configuration);
        return mock.Service;
    }

    internal static void SetPlaybackPermission(User user, bool hasPlaybackAccess)
    {
        user.Permissions.Clear();
        user.Permissions.Add(new Permission(PermissionKind.EnableMediaPlayback, hasPlaybackAccess));
    }
}
