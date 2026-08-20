using System;
using System.ComponentModel.DataAnnotations;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Primitives.Unit.Tests;

[TestClass]
public sealed class EnumExtensionsTests
{
    [TestMethod]
    public void ToFlagsEnum_UInt64HighBit_CombinesWithoutOverflow()
    {
        var value = "Low flag, High flag".ToFlagsEnum<LargePermissions>();

        Assert.AreEqual(LargePermissions.All, value);
    }

    [TestMethod]
    public void GetFlagsDisplayName_CompositeAlias_ReturnsEachSetBitOnce()
    {
        var displayName = LargePermissions.All.GetFlagsDisplayName();

        Assert.AreEqual("Low flag, High flag", displayName);
    }

    [Flags]
    private enum LargePermissions : ulong
    {
        None = 0,

        [Display(Name = "Low flag")]
        Low = 1,

        [Display(Name = "High flag")]
        High = 1UL << 63,

        [Display(Name = "All flags")]
        All = Low | High
    }
}
