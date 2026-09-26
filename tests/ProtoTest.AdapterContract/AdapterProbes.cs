namespace ProtoTest.AdapterContract;

using ProtoTest.Core;

/// <summary>
/// The skip and attachment probes every adapter test project shares. One copy of the reason and one
/// factory keep the five runner-side tests comparable, so a change to the fixture cannot quietly fork
/// one adapter's proof.
/// </summary>
public static class AdapterProbes
{
    /// <summary>The capability no adapter test project composes, so the skip condition always fires.</summary>
    public const string SkipCapability = "not-composed";

    /// <summary>The reason every skip-condition test asserts the runner reports.</summary>
    public const string SkipReason = "the adapter proves the skip path";

    /// <summary>The name of the attachment every attachment test publishes.</summary>
    public const string AttachmentName = "adapter-attachment";

    /// <summary>The payload of the shared attachment.</summary>
    public const string AttachmentPayload = "payload";

    /// <summary>The description of the shared attachment.</summary>
    public const string AttachmentDescription = "Shared adapter artifact";

    /// <summary>
    /// The attachment every attachment test publishes and then looks for in the runner's own
    /// reporting; a publisher reduced to a no-op must fail those tests.
    /// </summary>
    public static ProtoTestAttachment CreateAttachment()
        => ProtoTestAttachment.FromText(AttachmentName, AttachmentPayload, description: AttachmentDescription);
}
