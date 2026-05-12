namespace PhysicalMouse.Tests;

/// <summary>
/// Tests for the <see cref="MouseReport" /> contract.
/// </summary>
[TestClass]
public sealed class MouseReportTests
{
    /// <summary>
    /// Verifies that the empty report is recognized as empty.
    /// </summary>
    [TestMethod]
    public void EmptyReportIsEmpty()
    {
        Assert.IsTrue(MouseReport.Empty.IsEmpty);
    }

    /// <summary>
    /// Verifies that a report with input is recognized as non-empty.
    /// </summary>
    [TestMethod]
    public void NonEmptyReportIsNotEmpty()
    {
        MouseReport report = new(MouseButtons.Left, 12, -3, 1);

        Assert.IsFalse(report.IsEmpty);
    }
}
