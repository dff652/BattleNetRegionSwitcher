using BattleNetRegionSwitcher.Core;

namespace BattleNetRegionSwitcher.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, @"Local\BattleNetRegionSwitcher", out var created);
        if (!created)
        {
            MessageBox.Show("切换工具已经打开，请使用现有窗口。", "战网地区切换");
            return;
        }
        using var form = new MainForm(new ClientLauncher(), new SettingsStore());
        if (args.Contains("--ui-smoke-test"))
        {
            var timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (_, _) => { timer.Stop(); timer.Dispose(); form.Close(); };
            form.Shown += (_, _) => timer.Start();
        }
        Application.Run(form);
    }
}
