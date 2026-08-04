using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Threading;
using System.Globalization;
using Tessalume.App.Infrastructure;
using Tessalume.App.Models;

namespace Tessalume.App;

public partial class ThemeQuickSwitchWindow
{
    private void Home_Click(object sender, RoutedEventArgs e)
    {
        _showHome();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _showHome();
        Close();
    }

    private void WindowDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null)
        {
            if (source is System.Windows.Controls.Button)
            {
                return;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void PositionAtTopCenter()
    {
        Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - ActualWidth) / 2;
        Top = SystemParameters.WorkArea.Top + 18;
    }
}
