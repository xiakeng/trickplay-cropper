namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public enum NotFoundCondition
{
    LogicalVideoMissing,
    LogicalVideoHidden,
    LogicalItemWrongType,
    SelectedSourceNotMember,
    SelectedSourceMembershipMalformed,
    SelectedVideoMissing,
    SelectedVideoIdentityMismatch,
    SelectedVideoHidden,
    SelectedItemWrongType,
    NoConfiguredTarget,
    GeneratedMetadataMissing,
    ExactMetadataMissing,
    ThumbnailsMissing,
    ThumbnailsNegative,
    ManagerPathMissing,
    SourceSpriteMissing,
}
