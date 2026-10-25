using System.Security.Cryptography;
using PKHeX.Core;
using PKForge.Domain;
using PKForge.Engine;
using Xunit;

namespace PKForge.Engine.Tests;

public sealed class ParkIdentityTests
{
    private static (byte[] Data, string? Format) Make(int generation, int species, int level)
    {
        var engine = new SaveEngine();
        using var session = engine.OpenBlankSession(generation);
        var made = new LegalizerService().GenerateData(session, new GenerationRequest(species, level, false, null, null, null, null))
            ?? throw new InvalidOperationException($"Could not generate #{species}.");
        return (made.Data, made.Info.Format);
    }

    private static byte[] Edit(byte[] data, string? format, Action<PKM> change)
    {
        var pk = EntityBytes.Parse(data, format) ?? throw new InvalidOperationException("Could not read the Pokémon.");
        change(pk);
        pk.RefreshChecksum();
        var bytes = new byte[pk.SIZE_PARTY];
        pk.WriteDecryptedDataParty(bytes);
        return bytes;
    }

    [Theory]
    [InlineData(3, 252)]
    [InlineData(4, 387)]
    [InlineData(8, 810)]
    public void Playing_on_keeps_it_the_same_Pokemon(int generation, int species)
    {
        var (data, format) = Make(generation, species, 6);
        var recorded = ParkIdentity.Of(data, format);
        var played = Edit(data, format, pk =>
        {
            pk.CurrentFriendship = (byte)(pk.CurrentFriendship == 70 ? 71 : 70);
            pk.EXP += 40;
            pk.Move1_PPUps = 1;
        });

        Assert.NotNull(recorded);
        Assert.Equal(recorded, ParkIdentity.Of(played, format));
        Assert.True(ParkIdentity.IsSameIndividual(recorded, played, format));
    }

    [Fact]
    public void Evolving_keeps_it_the_same_Pokemon()
    {
        var (data, format) = Make(3, 252, 16);
        var evolved = Edit(data, format, pk => pk.Species = 253);
        Assert.True(ParkIdentity.IsSameIndividual(ParkIdentity.Of(data, format), evolved, format));
    }

    [Fact]
    public void Another_Pokemon_in_the_slot_is_not_it()
    {
        var (data, format) = Make(3, 252, 6);
        var other = Edit(data, format, pk => pk.PID ^= 0x00010001);
        var traded = Edit(data, format, pk => pk.ID32 ^= 0x00000001);

        Assert.False(ParkIdentity.IsSameIndividual(ParkIdentity.Of(data, format), other, format));
        Assert.False(ParkIdentity.IsSameIndividual(ParkIdentity.Of(data, format), traded, format));
    }

    [Fact]
    public void Game_Boy_Pokemon_are_told_apart_by_their_DVs()
    {
        var (data, format) = Make(2, 152, 10);
        var other = Edit(data, format, pk => pk.IV_ATK = (pk.IV_ATK + 1) % 16);
        Assert.False(ParkIdentity.IsSameIndividual(ParkIdentity.Of(data, format), other, format));
    }

    [Fact]
    public void The_key_fits_in_a_resident_id()
    {
        var (data, format) = Make(3, 252, 6);
        var key = ParkIdentity.Of(data, format)!;
        Assert.DoesNotContain(':', key);
        Assert.False(ParkIdentity.IsLegacyFingerprint(key));
    }

    [Fact]
    public void A_stay_recorded_before_the_fix_still_checks_the_whole_Pokemon()
    {
        var (data, format) = Make(3, 252, 6);
        var legacy = Convert.ToHexString(SHA256.HashData(data));
        var played = Edit(data, format, pk => pk.CurrentFriendship = (byte)(pk.CurrentFriendship == 70 ? 71 : 70));

        Assert.True(ParkIdentity.IsLegacyFingerprint(legacy));
        Assert.True(ParkIdentity.IsSameIndividual(legacy, data, format));
        Assert.False(ParkIdentity.IsSameIndividual(legacy, played, format));
    }

    [Fact]
    public void Nothing_recorded_or_unreadable_is_never_a_match()
    {
        var (data, format) = Make(3, 252, 6);
        Assert.False(ParkIdentity.IsSameIndividual(null, data, format));
        Assert.Null(ParkIdentity.Of([1, 2, 3], format));
        Assert.False(ParkIdentity.IsSameIndividual(ParkIdentity.Of(data, format), [1, 2, 3], format));
    }
}

public sealed class ParkIdentityResidentIdTests
{
    [Theory]
    [InlineData("save:F5643CBB7A574312:-1:0:id1A2B3C4DA7AF64E9A7AF64E9", "id1A2B3C4DA7AF64E9A7AF64E9")]
    [InlineData("save:F5643CBB7A574312:-1:0:4E0E1D311E7BCD330F9E239EA47F39E604498FE2F405F497F1790175E0219461", null)]
    [InlineData("save:F5643CBB7A574312:3:12", null)]
    [InlineData("bank:0f8fad5bd9cb469fa165708677289f8e", null)]
    public void A_resident_id_carries_its_identity_only_once_identities_were_kept(string residentId, string? identity) =>
        Assert.Equal(identity, ParkIdentity.InResidentId(residentId));
}
