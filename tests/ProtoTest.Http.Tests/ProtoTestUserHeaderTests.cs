namespace ProtoTest.Http.Tests;

using System.Text;

/// <summary>
/// The wire form of the built-in test user: the round trip, and the malformed values the app-side
/// authentication must refuse instead of throwing on.
/// </summary>
[TestFixture]
public sealed class ProtoTestUserHeaderTests
{
    [Test]
    public void RoundTrip_ShouldKeepNameRolesAndClaims()
    {
        var user = new ProtoTestUser("alice", ["admin", "billing"], [new ProtoTestUserClaim("tenant", "northstar")]);

        var decoded = ProtoTestUserHeader.TryDecode(ProtoTestUserHeader.Encode(user), out var roundTripped);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(decoded, Is.True);
            Assert.That(roundTripped!.Name, Is.EqualTo("alice"));
            Assert.That(roundTripped.Roles, Is.EqualTo(new[] { "admin", "billing" }));
            Assert.That(roundTripped.Claims, Is.EqualTo(new[] { new ProtoTestUserClaim("tenant", "northstar") }));
        }
    }

    [Test]
    public void TryDecode_ShouldRefuseAValueThatIsNotBase64()
    {
        Assert.That(ProtoTestUserHeader.TryDecode("not base64!", out var user), Is.False);
        Assert.That(user, Is.Null);
    }

    [Test]
    public void TryDecode_ShouldRefuseANullRoleElement()
    {
        AssertMalformed("""{"Name":"alice","Roles":["admin",null],"Claims":[]}""");
    }

    [Test]
    public void TryDecode_ShouldRefuseANullClaimElement()
    {
        AssertMalformed("""{"Name":"alice","Roles":[],"Claims":[null]}""");
    }

    [Test]
    public void TryDecode_ShouldRefuseAClaimWithoutAType()
    {
        AssertMalformed("""{"Name":"alice","Roles":[],"Claims":[{"Type":null,"Value":"northstar"}]}""");
    }

    [Test]
    public void TryDecode_ShouldRefuseAClaimWithoutAValue()
    {
        AssertMalformed("""{"Name":"alice","Roles":[],"Claims":[{"Type":"tenant","Value":null}]}""");
    }

    [Test]
    public void TryDecode_ShouldRefuseAnOversizedValue()
    {
        // A valid identity whose header exceeds the accepted length: the decode refuses it before
        // decoding, so an oversized header cannot allocate the payload.
        var header = ProtoTestUserHeader.Encode(new ProtoTestUser(new string('a', ProtoTestUserHeader.MaxHeaderLength)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(header.Length, Is.GreaterThan(ProtoTestUserHeader.MaxHeaderLength));
            Assert.That(ProtoTestUserHeader.TryDecode(header, out var user), Is.False);
            Assert.That(user, Is.Null);
        }
    }

    private static void AssertMalformed(string json)
    {
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        Assert.That(ProtoTestUserHeader.TryDecode(header, out var user), Is.False);
        Assert.That(user, Is.Null);
    }
}
