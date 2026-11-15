using Gloria.Core.Weapons;

namespace Gloria.Core.Tests.Weapons;

public class StaticWeaponDataTests
{
    private readonly StaticWeaponData _weapons = new();

    [Theory]
    [InlineData("weapon_ak47")]
    [InlineData("ak47")]
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
}
