using Gloria.Core.Weapons;

namespace Gloria.Core.Tests.Weapons;

public class StaticWeaponDataTests
{
    private readonly StaticWeaponData _weapons = new();

    [Theory]
    [InlineData("weapon_ak47")]
    [InlineData("ak47")]
    [InlineData("AK47")]
    public void Find_AcceptsNamesWithAndWithoutPrefix(string weapon)
    {
        Assert.NotNull(_weapons.Find(weapon));
    }

    [Fact]
    public void IsStopped_UsesTheWeaponsOwnThreshold()
    {
        var ak = _weapons.Find("ak47")!;
        var awp = _weapons.Find("awp")!;
        var speed = (ak.AccurateSpeedThreshold + awp.AccurateSpeedThreshold) / 2;

        Assert.True(ak.IsStopped(speed));
        Assert.False(awp.IsStopped(speed));
    }

    [Theory]
    [InlineData("weapon_glock", WeaponClass.Pistol)]
    [InlineData("deagle", WeaponClass.Pistol)]
    [InlineData("mp9", WeaponClass.Smg)]
    [InlineData("m4a1_silencer", WeaponClass.Rifle)]
    [InlineData("weapon_awp", WeaponClass.Sniper)]
    [InlineData("xm1014", WeaponClass.Shotgun)]
    [InlineData("negev", WeaponClass.MachineGun)]
    [InlineData("knife", WeaponClass.Other)]
    [InlineData("weapon_knife_karambit", WeaponClass.Other)]
    [InlineData("hegrenade", WeaponClass.Other)]
    [InlineData("taser", WeaponClass.Other)]
    [InlineData("c4", WeaponClass.Other)]
    [InlineData("", WeaponClass.Other)]
    public void Classify_MapsWeaponsToTheirClass(string weapon, WeaponClass expected)
    {
        Assert.Equal(expected, _weapons.Classify(weapon));
    }

    [Fact]
    public void ScopedThreshold_IsLowerForSnipersThatSlowDownWhenZoomed()
    {
        var awp = _weapons.Find("awp")!;

        Assert.True(awp.AccurateSpeedThresholdWhen(scoped: true) < awp.AccurateSpeedThresholdWhen(scoped: false));
        Assert.Equal(awp.AccurateSpeedThreshold, awp.AccurateSpeedThresholdWhen(scoped: false));
    }
}
