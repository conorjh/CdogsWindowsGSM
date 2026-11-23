using WindowsGSM;

namespace CdogsWindowsGSM.Tests;

[TestClass]
public sealed class EditConfigNumberTests
{
    [TestMethod]
    public void ResolveEditConfigNumber_UsesConfiguredValueWhenItIsNumeric()
    {
        double? value = MainWindow.ResolveEditConfigNumber("24", string.Empty);

        Assert.AreEqual(24d, value);
    }

    [TestMethod]
    public void ResolveEditConfigNumber_UsesNumericGameDefaultWhenConfigurationIsEmpty()
    {
        double? value = MainWindow.ResolveEditConfigNumber(string.Empty, "32");

        Assert.AreEqual(32d, value);
    }

    [TestMethod]
    public void ResolveEditConfigNumber_ReturnsNullWhenConfigurationAndGameDefaultAreEmpty()
    {
        double? value = MainWindow.ResolveEditConfigNumber(string.Empty, string.Empty);

        Assert.IsNull(value);
    }
}
