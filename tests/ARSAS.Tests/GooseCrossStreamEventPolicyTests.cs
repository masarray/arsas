using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class GooseCrossStreamEventPolicyTests
{
    [Fact]
    public void SameTypedValueOnTwoDistinctStreamsWithin250ms_IsOneOperatorChange()
    {
        var key=GooseCrossStreamEventPolicy.Identity("02:00:4C:4F:4F:50",new[]{
            new GooseProcessDelta("BCUGEF650/CSWI6.Pos.stVal","Off",true)
        });
        Assert.NotNull(key);
        var policy=new GooseCrossStreamEventPolicy();
        var t=DateTimeOffset.Parse("2026-10-10T09:13:33.970+00:00");
        Assert.False(policy.IsMirror(key,"GOOSE1",t));
        Assert.True(policy.IsMirror(key,"GOOSE2",t.AddMilliseconds(3)));
        Assert.False(policy.IsMirror(key,"GOOSE1",t.AddMilliseconds(6)));
        Assert.False(policy.IsMirror(key,"GOOSE2",t.AddSeconds(3)));
    }

    [Fact]
    public void NeverMergeUnverifiedNamesOrIndependentProcessChanges()
    {
        Assert.Null(GooseCrossStreamEventPolicy.Identity("00:11",new[]{
            new GooseProcessDelta("CSWI6.Pos.stVal","Off",true)
        }));
        Assert.Null(GooseCrossStreamEventPolicy.Identity("00:11",new[]{
            new GooseProcessDelta("LD/CSWI6.Pos.stVal","Off",false)
        }));
        Assert.Null(GooseCrossStreamEventPolicy.Identity("00:11",new[]{
            new GooseProcessDelta("LD/CSWI6.Pos.stVal","Off",true),
            new GooseProcessDelta("LD/CSWI7.Pos.stVal","On",true)
        }));
        var k1=GooseCrossStreamEventPolicy.Identity("00:11",new[]{
            new GooseProcessDelta("LD/CSWI6.Pos.stVal","Off",true)
        });
        var k2=GooseCrossStreamEventPolicy.Identity("00:11",new[]{
            new GooseProcessDelta("LD/CSWI7.Pos.stVal","Off",true)
        });
        Assert.NotEqual(k1,k2);
    }

    [Fact]
    public void FollowLatestAndRawRetransmissionControlsAreIndependent()
    {
        var file=Read("MainWindow.GooseTimeline.cs");
        var view=Read("Views/GooseSubscriberLiteView.xaml");
        Assert.Contains("FollowLatestGooseEvents",view);
        Assert.Contains("ShowGooseRetransmissions",view);
        Assert.Contains("_settingGooseLiveSelection",file);
        Assert.Contains("_gooseMirroredEvents.IsMirror",file);
        Assert.Contains("_gooseMirroredEvents.Reset()",file);
        Assert.Contains("SelectedGooseEvent = eventRow",file);
    }

    private static string Read(string path)
    {
        DirectoryInfo? dir=new(AppContext.BaseDirectory);
        while(dir is not null)
        {
            var name=Path.Combine(dir.FullName,path.Replace('/',Path.DirectorySeparatorChar));
            if(File.Exists(name))return File.ReadAllText(name);
            dir=dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
