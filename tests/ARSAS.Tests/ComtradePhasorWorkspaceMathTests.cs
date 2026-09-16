using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradePhasorWorkspaceMathTests
{
    [Fact]
    public void SelectRoleSet_OrdersElectricalPhasesAndKeepsVoltageCurrentIndependent()
    {
        var channels = new[]
        {
            D(8, 2, 3, "IC", "C", "Bay 1", "A"),
            D(2, 1, 2, "VB", "B", "Bay 1", "V"),
            D(7, 2, 1, "IA", "A", "Bay 1", "A"),
            D(1, 1, 1, "VA", "A", "Bay 1", "V"),
            D(3, 1, 3, "VC", "C", "Bay 1", "V"),
            D(9, 2, 2, "IB", "B", "Bay 1", "A")
        };

        var voltage = ComtradePhasorWorkspaceMath.SelectRoleSet(channels, ComtradePhasorWorkspaceMath.RoleVoltage);
        var current = ComtradePhasorWorkspaceMath.SelectRoleSet(channels, ComtradePhasorWorkspaceMath.RoleCurrent);

        Assert.Equal(new uint[] { 1, 2, 3 }, voltage.Select(item => item.Index));
        Assert.Equal(new uint[] { 7, 9, 8 }, current.Select(item => item.Index));
    }

    [Fact]
    public void SelectRoleSet_PrefersMostCompleteSameCircuitAndUnitFamily()
    {
        var channels = new[]
        {
            D(0, 1, 1, "VA-Bay2", "A", "Bay 2", "V"),
            D(1, 1, 1, "VA", "A", "Bay 1", "V"),
            D(2, 1, 2, "VB", "B", "Bay 1", "V"),
            D(3, 1, 3, "VC", "C", "Bay 1", "V"),
            D(4, 1, 2, "VB-kV", "B", "Bay 1", "kV")
        };

        var selected = ComtradePhasorWorkspaceMath.SelectRoleSet(channels, ComtradePhasorWorkspaceMath.RoleVoltage);

        Assert.Equal(new uint[] { 1, 2, 3 }, selected.Select(item => item.Index));
        Assert.All(selected, item => Assert.Equal("V", item.Units));
        Assert.All(selected, item => Assert.Equal("Bay 1", item.Circuit));
    }

    [Fact]
    public void SelectRoleSet_FallsBackToSameUnitWhenCircuitMetadataIsSparse()
    {
        var channels = new[]
        {
            D(0, 2, 1, "IA", "A", "CT-A", "A"),
            D(1, 2, 2, "IB", "B", "CT-B", "A"),
            D(2, 2, 3, "IC", "C", "CT-C", "A"),
            D(3, 2, 1, "IA-kA", "A", "Other", "kA")
        };

        var selected = ComtradePhasorWorkspaceMath.SelectRoleSet(channels, ComtradePhasorWorkspaceMath.RoleCurrent);

        Assert.Equal(new uint[] { 0, 1, 2 }, selected.Select(item => item.Index));
        Assert.All(selected, item => Assert.Equal("A", item.Units));
    }

    [Fact]
    public void SelectRoleSet_KeepsOneChannelPerPhaseAndIgnoresOtherPhase()
    {
        var channels = new[]
        {
            D(0, 1, 1, "VA-1", "A", "Bay", "V"),
            D(1, 1, 1, "VA-duplicate", "A", "Bay", "V"),
            D(2, 1, 4, "VE", "E", "Bay", "V"),
            D(3, 1, 0, "Aux", "", "Bay", "V")
        };

        var selected = ComtradePhasorWorkspaceMath.SelectRoleSet(channels, ComtradePhasorWorkspaceMath.RoleVoltage);

        Assert.Equal(new uint[] { 0, 2 }, selected.Select(item => item.Index));
    }

    [Theory]
    [InlineData("A", 1)]
    [InlineData("L1", 1)]
    [InlineData("B", 2)]
    [InlineData("L2", 2)]
    [InlineData("C", 3)]
    [InlineData("L3", 3)]
    [InlineData("N", 4)]
    [InlineData("E", 4)]
    [InlineData("Other", 0)]
    public void PhaseRoleFromCanonicalName_MapsExpectedNames(string phase, int expected)
        => Assert.Equal(expected, ComtradePhasorWorkspaceMath.PhaseRoleFromCanonicalName(phase));

    private static ComtradePhasorChannelDescriptor D(
        uint index,
        int role,
        int phaseRole,
        string label,
        string phase,
        string circuit,
        string units)
        => new(index, role, phaseRole, label, phase, circuit, units);
}
