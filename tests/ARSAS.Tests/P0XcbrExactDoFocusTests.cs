using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P0XcbrExactDoFocusTests
{
    private static LiveIedLogicalNodeModel Xcbr() => new()
    {
        Name = "XCBR1",
        DataObjects =
        [
            new LiveIedDataObjectModel {
                Name = "ExNumTr",
                Reference = "BCUGEF650/XCBR1.ExNumTr"
            },
            new LiveIedDataObjectModel {
                Name = "Pos",
                Reference = "BCUGEF650/XCBR1.Pos"
            }
        ]
    };

    [Fact]
    public void DirectOwnerRetainsReadDoScope()
    {
        Assert.True(IedBrowserExactDoFocus.IsDeclaredOwner(
            Xcbr(), "BCUGEF650/XCBR1.ExNumTr"));
    }

    [Theory]
    [InlineData("BCUGEF650/XCBR1.ExNumTr.minVal")]
    [InlineData("BCUGEF650/XCBR1.ExNumTr.ExNumTr")]
    [InlineData("BCUGEF650/XCBR2.ExNumTr")]
    [InlineData("BCUGEF650/XCBR1.exNumTr")]
    public void NestedOrOtherOwnerCannotHijackXcbrReadDo(string reference)
    {
        Assert.False(IedBrowserExactDoFocus.IsDeclaredOwner(Xcbr(), reference));
    }

    [Fact]
    public void DuplicateModelOwnerIsNotUniqueReadDoAuthority()
    {
        var ln = new LiveIedLogicalNodeModel {
            Name = "XCBR1",
            DataObjects = [Xcbr().DataObjects[0], Xcbr().DataObjects[0]]
        };
        Assert.False(IedBrowserExactDoFocus.IsDeclaredOwner(
            ln, "BCUGEF650/XCBR1.ExNumTr"));
    }
}
