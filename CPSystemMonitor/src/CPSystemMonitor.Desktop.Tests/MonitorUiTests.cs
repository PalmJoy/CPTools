using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CPSystemMonitor.App;
using CPSystemMonitor.Core.Model;
using CPSystemMonitor.Core.Monitoring;
using CPSystemMonitor.Core.Storage;
using CPSystemMonitor.Platform;
using CPSystemMonitor.Platform.MacOS;
using CPSystemMonitor.Platform.Windows;
namespace CPSystemMonitor.Desktop.Tests;
public sealed class MonitorUiTests
{
    private static MonitorSample Sample(DateTimeOffset time) => new(time, 24, Enumerable.Range(0,16).Select(i => i == 0 ? 72d : 20d+i).ToArray(),
        32L<<30,20L<<30,"/",1L<<40,410L<<30,128L<<20,42L<<20,24800000,3200000,
        new("Blender",12.4,2L<<30,0,0,0,0), new("Chrome",5,3L<<30,0,0,0,0),
        new("Code",1,1L<<30,18L<<20,32L<<20,0,0), new("Chrome",5,3L<<30,0,0,21L<<20,2L<<20));
    private static (SettingsHub Hub, string Root) Hub()
    {
        string root = Path.Combine(Path.GetTempPath(),"cpsm-ui-"+Guid.NewGuid());
        var s = new AppSettings(); s.Appearance.Animation = false; s.Appearance.Opacity = 100;
        return (new(new AppDataStore(new(root,true)),s), root);
    }
    [AvaloniaFact] public void All_section_combinations_reflow_in_both_modes()
    {
        var (hub, root) = Hub(); var history = new SampleHistory(); history.Add(Sample(DateTimeOffset.Now),60);
        var w = new MonitorWindow(hub,history) { AllowClose = true }; w.Show();
        try
        {
            foreach (var mode in Enum.GetValues<MonitorMode>())
            for (int mask=0;mask<16;mask++)
            {
                hub.Update(s => { s.Monitor.Mode=mode; foreach(var section in Enum.GetValues<MonitorSection>()) s.Monitor.For(section).Visible=(mask&(1<<(int)section))!=0; },SettingsChange.BoardLayout);
                w.Update(history.Samples[0]); Dispatcher.UIThread.RunJobs();
                Assert.Equal(System.Numerics.BitOperations.PopCount((uint)mask),w.GetVisualDescendants().OfType<MetricCard>().Count());
                Assert.True(double.IsFinite(w.Height));
            }
            hub.Update(s => { s.Monitor.Mode=MonitorMode.Standard; s.Monitor.Cpu.Visible=true; s.Monitor.PerCore=true; },SettingsChange.BoardLayout);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text?.StartsWith("L15") == true);
        }
        finally { w.Close(); hub.Flush(); if(Directory.Exists(root))Directory.Delete(root,true); }
    }
    [AvaloniaFact] public void Missing_attribution_is_hidden_and_cpu_has_no_removed_subtitles()
    {
        var (hub, root)=Hub(); var history=new SampleHistory(); var sample=Sample(DateTimeOffset.Now) with {TopStorage=null,TopNetwork=null}; history.Add(sample,60);
        var w=new MonitorWindow(hub,history){AllowClose=true}; w.Show(); w.Update(sample); Dispatcher.UIThread.RunJobs();
        try
        {
            var texts=w.GetVisualDescendants().OfType<TextBlock>().Select(t=>t.Text??"").ToArray();
            Assert.DoesNotContain(texts,t=>t.Contains("TOTAL USAGE")||t.Contains("LOGICAL CPUs")||t.Contains("UPTIME")||t.Contains("RECEIVE")||t=="SEND");
            Assert.Equal(2,texts.Count(t=>t.StartsWith("TOP  ")&&!string.IsNullOrWhiteSpace(t)));
            Assert.Contains(texts,t=>t.Contains(DateTime.Now.ToString("yyyy/MM/dd")));
        }
        finally { w.Close();hub.Flush();if(Directory.Exists(root))Directory.Delete(root,true); }
    }
    [AvaloniaFact] public void Context_menu_changes_mode_and_preserves_per_section_preferences()
    {
        var (hub, root) = Hub(); var history = new SampleHistory();
        var w = new MonitorWindow(hub, history) { AllowClose = true }; w.Show();
        try
        {
            var menu = w.ContextMenu!;
            menu.Open(w); Dispatcher.UIThread.RunJobs();
            var modes = (MenuItem)menu.Items[0]!;
            var compact = (MenuItem)modes.Items[1]!;
            compact.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(MonitorMode.Compact, hub.Current.Monitor.Mode);
            menu.Close();
            var cpu = w.ContextMenu!.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "CPU"));
            var entries = cpu.ItemsSource!.Cast<MenuItem>().ToArray();
            entries[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(hub.Current.Monitor.Cpu.TopApp);
            Assert.True(hub.Current.Monitor.Memory.TopApp);
            Assert.Equal(MonitorMode.Compact, hub.Current.Monitor.Mode);
        }
        finally { w.Close(); hub.Flush(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [AvaloniaFact] public void Capture_standard_compact_and_settings()
    {
        var output=Environment.GetEnvironmentVariable("CPSM_CAPTURE_DIR"); if(string.IsNullOrEmpty(output))return;
        Directory.CreateDirectory(output);var (hub,root)=Hub();var history=new SampleHistory();
        var time=DateTimeOffset.Now;
        for(int i=0;i<61;i++)history.Add(Sample(time.AddSeconds(i-60)) with {CpuPercent=24+Math.Sin(i*.8)*8,ReadBytesPerSecond=(120+Math.Sin(i)*40)*(1L<<20),WriteBytesPerSecond=(40+Math.Cos(i)*20)*(1L<<20)},60);
        var w=new MonitorWindow(hub,history){AllowClose=true}; w.Show();
        try
        {
            foreach(var mode in Enum.GetValues<MonitorMode>())
            {
                hub.Update(s=>s.Monitor.Mode=mode,SettingsChange.BoardLayout);w.Update(history.Samples[^1]);Dispatcher.UIThread.RunJobs();
                using var frame=w.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(output,mode.ToString().ToLowerInvariant()+".png"));
            }
            IPlatformServices platform=OperatingSystem.IsMacOS()?new MacPlatformServices():new WindowsPlatformServices();
            using(platform)
            {
                var settings=new SettingsWindow(new(hub,platform,new(root,true),()=>w.Position,_=>{},()=>{}));settings.Show();Dispatcher.UIThread.RunJobs();
                using var frame=settings.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(output,"settings.png"));settings.Close();
            }
        }
        finally{w.Close();hub.Flush();if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
