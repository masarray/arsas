using System.Net.Sockets;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class Iec61850ConnectionFailureClassifierTests
{
    [Fact]
    public void MissingAssociationSummary_PreservesGenericFailureClassification()
    {
        var failure = Iec61850ConnectionFailureClassifier.Classify(
            "192.0.2.10", 102, exception: null, associationSummary: null);

        Assert.Equal("IEC61850_CONNECTION_FAILED", failure.Kind);
        Assert.Contains("192.0.2.10:102", failure.FriendlyMessage, StringComparison.Ordinal);
        Assert.Equal(string.Empty, failure.TechnicalSummary);
    }

    [Fact]
    public void MissingAssociationSummary_PreservesSocketErrorEvidence()
    {
        var socketError = new SocketException((int)SocketError.ConnectionRefused);
        var failure = Iec61850ConnectionFailureClassifier.Classify(
            "192.0.2.10", 102, socketError, associationSummary: null);

        Assert.Equal("TCP_CONNECTION_REFUSED", failure.Kind);
        Assert.Contains("SocketError=ConnectionRefused", failure.TechnicalSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("AssociationAttempts=", failure.TechnicalSummary, StringComparison.Ordinal);
    }
}
