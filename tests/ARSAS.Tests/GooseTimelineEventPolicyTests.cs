using ArIED61850Tester.Services;
namespace ARSAS.Tests;

public sealed class GooseTimelineEventPolicyTests
{
    [Fact]
    public void NormalRetransmissionWithPersistentTestWarning_DoesNotRepeatInMessages()
    {
        var first = new GooseTimelineSignature("intermediate|false", "1", "GOOSE test flag is set.", "New");
        var retrans = first with { Sequence = "Retransmission" };
        Assert.True(GooseTimelineEventPolicy.Evaluate(null, first, false).Include);
        Assert.False(GooseTimelineEventPolicy.Evaluate(first, retrans, false).Include);
        Assert.False(GooseTimelineEventPolicy.Evaluate(retrans, retrans, false).Include);
        Assert.True(GooseTimelineEventPolicy.Evaluate(retrans, retrans, true).Include);
    }

    [Fact]
    public void UnexpectedPayloadChangeWithoutStateIncrement_IsVisible()
    {
        var first = new GooseTimelineSignature("intermediate|false", "1", "", "Retransmission");
        var changed = first with { Payload = "closed|false" };
        var result = GooseTimelineEventPolicy.Evaluate(first, changed, false);
        Assert.True(result.Include);
        Assert.True(result.PayloadChanged);
        Assert.False(result.StateChanged);
    }

    [Fact]
    public void ChangedStateAndNewDiagnostic_AreVisible()
    {
        var first = new GooseTimelineSignature("intermediate", "1", "", "Normal");
        Assert.True(GooseTimelineEventPolicy.Evaluate(first, first with { State = "2" }, false).Include);
        Assert.True(GooseTimelineEventPolicy.Evaluate(first, first with { Diagnostics = "Bad confRev" }, false).Include);
    }

    [Fact]
    public void CurrentGooseUi_HasThreeSecondHighlightAndOperatorRetransmissionToggle()
    {
        var view = Read("Views/GooseSubscriberLiteView.xaml");
        var timeline = Read("MainWindow.GooseTimeline.cs");
        var leaves = Read("Models/GooseSubscriberModels.cs");
        Assert.Contains("ShowGooseRetransmissions", view);
        Assert.Contains("GooseTimelineEventPolicy.Evaluate", timeline);
        Assert.Contains("AddSeconds(3)", leaves);
        Assert.DoesNotContain("IsMeaningfulGooseTimelineEvent", timeline);
    }

    private static string Read(string path)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
