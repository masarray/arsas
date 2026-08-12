using System.Windows;
using System.Windows.Media.Animation;

namespace ArIED61850Tester;

public partial class IoListTestingWindow
{
    private CancellationTokenSource? _failureShoutCancellation;

    private async void ShowFailureShout(string title, string message)
    {
        _failureShoutCancellation?.Cancel();
        _failureShoutCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _failureShoutCancellation = cancellation;

        FailureShoutTitle.Text = string.IsNullOrWhiteSpace(title) ? "Connection failed" : title.Trim();
        FailureShoutMessage.Text = string.IsNullOrWhiteSpace(message)
            ? "ARSAS could not complete the requested connection workflow. Open Diagnostics for details."
            : message.Trim();
        FailureShout.BeginAnimation(OpacityProperty, null);
        FailureShout.Visibility = Visibility.Visible;
        FailureShout.Opacity = 0;
        FailureShout.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellation.Token);
            FailureShout.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
                    FillBehavior = FillBehavior.Stop
                });
            await Task.Delay(TimeSpan.FromMilliseconds(330), cancellation.Token);
            if (ReferenceEquals(_failureShoutCancellation, cancellation))
            {
                FailureShout.Opacity = 0;
                FailureShout.Visibility = Visibility.Collapsed;
            }
        }
        catch (OperationCanceledException)
        {
            // A newer shout owns the surface; leave its animation untouched.
        }
    }
}
