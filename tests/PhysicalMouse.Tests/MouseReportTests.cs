namespace PhysicalMouse.Tests;

/// <summary>Tests for <see cref="MouseReport" />.</summary>
[TestClass]
public sealed class MouseReportTests
{
    /// <summary>Checks the empty report.</summary>
    [TestMethod]
    public void EmptyReportIsEmpty()
    {
        Assert.IsTrue(MouseReport.Empty.IsEmpty);
    }

    /// <summary>Checks a non-empty report.</summary>
    [TestMethod]
    public void NonEmptyReportIsNotEmpty()
    {
        MouseReport report = new(MouseButtons.Left, 12, -3, 1);

        Assert.IsFalse(report.IsEmpty);
    }
}
