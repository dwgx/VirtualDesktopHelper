// ShellWindow.g.cs — 02-shell.xaml 的 code-behind 桩
//
// 02-shell.xaml 声明了 x:Class="VDHelper.Streamer.ShellWindow"，WPF 编译
// XAML 时会生成 .g.cs 并要求存在同名的 partial class，否则 MSBuild 报
// CS0101/CS0246。这里只提供最小可编译外壳：
//   - InitializeComponent() 由生成的 .g.cs 提供
//   - 标题栏按钮的实际行为留空，接线时在 OnMinimizeClick / OnCloseClick 里写
//
// 本文件属于临时 build proof 工程，落地 src/ 时用真实 ViewModel 替换即可。

using System.Windows;

namespace VDHelper.Shell
{
    public partial class ShellWindow : Window
    {
        public ShellWindow()
        {
            InitializeComponent();
        }

        /// <summary>标题栏最小化。接线：this.WindowState = WindowState.Minimize;</summary>
        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        /// <summary>标题栏关闭。接线：this.Close();</summary>
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}