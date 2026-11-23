using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Voltide.Core;

namespace Voltide.UI
{
    public class TrayApp : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private readonly DeviceManager _manager;
        private readonly NotifyIcon _tray;
        private readonly MainForm _form;
        private readonly System.Windows.Forms.Timer _timer;

        private bool _refreshing;
        private bool _refreshQueued;

        // A second right-click replaces the popup instead of stacking another copy.
        private TrayMenuForm _menuForm;
        private LowBatteryPopup _alertPopup;
        private readonly LowBatteryMonitor _alertMonitor = new LowBatteryMonitor();
        private IconTheme _iconTheme = IconTheme.Color;
        private int _lowBatteryThreshold = 20;
        private Icon _trayIcon;
        private Icon _formIcon;


        internal static readonly Color MenuMuted = Color.FromArgb(135, 135, 135); // card status line

        public TrayApp()
        {
            _manager = new DeviceManager();

            _form = new MainForm(_manager);
            _form.FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    _form.Hide();
                }
            };
            _form.RefreshRequested += delegate { RefreshNow(); };

            // force handle creation so cross-thread marshalling works immediately
            IntPtr handle = _form.Handle;

            _iconTheme = LoadIconTheme();
            _lowBatteryThreshold = LoadLowBatteryThreshold();

            _trayIcon = IconFactory.ToIcon(32, _iconTheme);
            _formIcon = IconFactory.ToIcon(32, _iconTheme);
            _form.Icon = _formIcon;

            _tray = new NotifyIcon();
            _tray.Icon = _trayIcon;
            _tray.Text = "Voltide";   // Windows tooltip on hover
            _tray.Visible = true;

            _tray.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { _form.ShowPanel(); return; }
                ShowMenuAboveRight();
            };

            _form.RefreshSeconds = LoadRefreshSeconds();
            _form.IconTheme = _iconTheme;
            _form.LowBatteryThreshold = _lowBatteryThreshold;
            _form.RefreshRateChanged += delegate
            {
                SaveRefreshSeconds(_form.RefreshSeconds);
                ApplyInterval();
            };
            _form.IconThemeChanged += delegate { ApplyIconTheme(_form.IconTheme); };
            _form.LowBatteryThresholdChanged += delegate { ApplyLowBatteryThreshold(_form.LowBatteryThreshold); };

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = _form.RefreshSeconds * 1000;
            _timer.Tick += delegate
            {
                // Keep the footprint small: the receiver shares its input pipe with
                // G HUB, and every request we send is broadcast to it too. It also
                // stops answering for a while if it is polled too hard, which showed
                // up as cards flapping between "97%" and "asleep".
                ApplyInterval();
                RefreshNow();
            };
            _timer.Start();

            RefreshNow();
        }

        /// <summary>
        /// The panel's own cadence while it is open; a slow trickle while hidden in the
        /// tray so a sleeping mouse is not woken every 15 seconds for nothing.
        /// </summary>
        private void ApplyInterval()
        {
            int seconds = _form.Visible ? _form.RefreshSeconds : 300;
            if (_timer.Interval != seconds * 1000) _timer.Interval = seconds * 1000;
        }

        // ------------------------------------------------------- refresh rate

        private const string SettingsKey = @"Software\Voltide";

        private static int LoadRefreshSeconds()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key =
                       Microsoft.Win32.Registry.CurrentUser.OpenSubKey(SettingsKey, false))
                {
                    if (key == null) return 90;
                    object raw = key.GetValue("RefreshSeconds");
                    if (raw == null) return 90;

                    int seconds = Convert.ToInt32(raw);
                    return (seconds == 15 || seconds == 30 || seconds == 90) ? seconds : 90;
                }
            }
            catch
            {
                return 30;
            }
        }

        private static void SaveRefreshSeconds(int seconds)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key =
                       Microsoft.Win32.Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    if (key != null) key.SetValue("RefreshSeconds", seconds);
                }
            }
            catch
            {
            }
        }

        private static IconTheme LoadIconTheme()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key =
                       Microsoft.Win32.Registry.CurrentUser.OpenSubKey(SettingsKey, false))
                {
                    if (key == null) return IconTheme.Color;
                    object raw = key.GetValue("IconTheme");
                    if (raw == null) return IconTheme.Color;

                    int v = Convert.ToInt32(raw);
                    if (v == (int)IconTheme.Light) return IconTheme.Light;
                    if (v == (int)IconTheme.Dark) return IconTheme.Dark;
                    return IconTheme.Color;
                }
            }
            catch
            {
                return IconTheme.Color;
            }
        }

        private static void SaveIconTheme(IconTheme theme)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key =
                       Microsoft.Win32.Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    if (key != null) key.SetValue("IconTheme", (int)theme);
                }
            }
            catch
            {
            }
        }

        private static int LoadLowBatteryThreshold()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key =
                       Microsoft.Win32.Registry.CurrentUser.OpenSubKey(SettingsKey, false))
                {
                    if (key == null) return 20;
                    object raw = key.GetValue("LowBatteryThreshold");
                    if (raw == null) return 20;

                    int v = Convert.ToInt32(raw);
                    return NormalizeThreshold(v);
                }
            }
            catch
            {
                return 20;
            }
        }

        private static void SaveLowBatteryThreshold(int threshold)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key =
                       Microsoft.Win32.Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    if (key != null) key.SetValue("LowBatteryThreshold", threshold);
                }
            }
            catch
            {
            }
        }

        private static int NormalizeThreshold(int value)
        {
            if (value <= 0) return 0;
            if (value < 5) return 5;
            if (value > 50) return 50;
            return ((value + 2) / 5) * 5;
        }

        private void ApplyIconTheme(IconTheme theme)
        {
            _iconTheme = theme;
            SaveIconTheme(theme);
            _form.IconTheme = theme;

            Icon nextTray = IconFactory.ToIcon(32, theme);
            Icon oldTray = _trayIcon;
            _trayIcon = nextTray;
            _tray.Icon = nextTray;
            if (oldTray != null) oldTray.Dispose();

            Icon nextForm = IconFactory.ToIcon(32, theme);
            Icon oldForm = _formIcon;
            _formIcon = nextForm;
            _form.Icon = nextForm;
            if (oldForm != null) oldForm.Dispose();
        }

        private void ApplyLowBatteryThreshold(int threshold)
        {
            int normalized = NormalizeThreshold(threshold);
            _lowBatteryThreshold = normalized;
            SaveLowBatteryThreshold(normalized);
            _form.LowBatteryThreshold = normalized;

            if (normalized <= 0 && _alertPopup != null && _alertPopup.Visible)
            {
                _alertPopup.Dismiss();
            }

            CheckLowBattery(_manager.Snapshot());
        }

        private void CheckLowBattery(List<DeviceSnapshot> snaps)
        {
            if (_lowBatteryThreshold <= 0 || snaps == null) return;

            List<DeviceSnapshot> visible = new List<DeviceSnapshot>();
            foreach (DeviceSnapshot s in snaps)
            {
                if (s == null || s.Info == null || s.Reading == null) continue;
                if (!s.Reading.Online && !s.Reading.Sleeping) continue;
                visible.Add(s);
            }

            List<DeviceSnapshot> merged = MainForm.CollapseTransports(visible);
            List<LowBatteryAlert> alerts = _alertMonitor.Evaluate(merged, _lowBatteryThreshold);
            if (alerts.Count == 0) return;

            if (_alertPopup == null || _alertPopup.IsDisposed)
            {
                _alertPopup = new LowBatteryPopup();
                _alertPopup.PanelRequested += delegate { ShowPanel(); };
            }

            _alertPopup.ShowAlerts(alerts, _lowBatteryThreshold);
        }

        /// <summary>
        /// Opens the popup to the upper-right of the cursor, in a borderless window of
        /// our own so the size, the corners and the dismissal are all under our control.
        /// </summary>
        private void ShowMenuAboveRight()
        {
            // One popup at a time. Without this, each right-click built a new window
            // and the old one stayed alive until it lost focus — repeated clicks piled
            // up as a stack of menus.
            if (_menuForm != null && !_menuForm.IsDisposed)
            {
                try { _menuForm.Close(); } catch { }
                _menuForm = null;
            }

            MenuPanel panel = BuildMenu();
            if (panel == null) return;

            TrayMenuForm form = new TrayMenuForm();
            form.Panel = panel;
            form.ClientSize = panel.Size;

            panel.Dock = DockStyle.Fill;
            panel.QuitRequested += delegate { Application.Exit(); };

            panel.AutostartEnabled = Autostart.IsSupported();
            panel.AutostartOn = Autostart.IsSupported() && Autostart.IsEnabled();
            panel.IconTheme = _iconTheme;
            panel.LowBatteryThreshold = _lowBatteryThreshold;

            MenuPanel captured = panel;
            panel.AutostartToggled += delegate { Autostart.Set(captured.AutostartOn); };
            panel.IconThemeChanged += delegate { ApplyIconTheme(captured.IconTheme); };
            panel.LowBatteryThresholdChanged += delegate { ApplyLowBatteryThreshold(captured.LowBatteryThreshold); };

            form.Controls.Add(panel);

            Point p = Cursor.Position;
            Rectangle screen = Screen.FromPoint(p).WorkingArea;

            int x = p.X + 4;
            int y = p.Y - panel.Height - 4;

            if (x + panel.Width > screen.Right) x = p.X - panel.Width - 4;
            if (y < screen.Top) y = p.Y + 4;

            form.Location = new Point(x, y);
            _menuForm = form;
            form.Show();
            form.Activate();
            panel.Focus();
        }

        // ---------------------------------------------------------------- menu

        /// <summary>
        /// Rebuilt on every open so the list is never stale. Order matches the panel:
        /// reporting a level, then awake-but-waiting, then asleep.
        /// </summary>
        private MenuPanel BuildMenu()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                float scale = g.DpiX / 96f;

                // Sizes straight from the layered artboards: 20 px names and levels,
                // 17 px Chinese, 9.76 px wordmark.
                Font nameFont = FontManager.Get(FontKind.MontserratMedium, 20f * scale);
                Font valueFont = FontManager.Get(FontKind.MontserratMedium, 20f * scale);
                Font cnFont = FontManager.Get(FontKind.Chinese, 17f * scale);
                Font markFont = FontManager.Get(FontKind.MontserratSemiBold, 9f * scale);

                List<MenuRow> rows = new List<MenuRow>();

                foreach (DeviceSnapshot s in OrderedForMenu())
                {
                    MenuRow row = new MenuRow();

                    // The tray row is narrow, so devices may publish a shorter label
                    // than the one the card uses.
                    if (!string.IsNullOrEmpty(s.Info.ShortModel)) row.Name = s.Info.ShortModel;
                    else if (!string.IsNullOrEmpty(s.Info.Model)) row.Name = s.Info.Model;
                    else row.Name = s.Info.Brand;

                    if (HasLevel(s))
                    {
                        row.Kind = MenuRowKind.Level;
                        row.Percent = s.Reading.Percent.Value;
                        row.Value = s.Reading.Percent.Value + "%";
                    }
                    else
                    {
                        row.Kind = MenuRowKind.Asleep;

                        int? last = _form.LastPercent(s.Info.Id);
                        if (last.HasValue)
                        {
                            // Still paired, just idle: show the last known level in grey
                            // instead of a blank, same rule as the card.
                            row.Percent = last.Value;
                            row.Value = last.Value >= 100 ? "Full" : last.Value + "%";
                            row.GreyValue = true;
                        }
                        else
                        {
                            row.Value = "休眠中";
                        }
                    }

                    rows.Add(row);
                }

                return new MenuPanel(rows, scale, nameFont, valueFont, cnFont, markFont);
            }
        }
        private List<DeviceSnapshot> OrderedForMenu()
        {
            List<DeviceSnapshot> live = new List<DeviceSnapshot>();
            List<DeviceSnapshot> asleep = new List<DeviceSnapshot>();

            foreach (DeviceSnapshot s in _manager.Snapshot())
            {
                if (s.Reading == null) { asleep.Add(s); continue; }

                if (s.Reading.Online && s.Reading.Percent.HasValue) live.Add(s);
                else asleep.Add(s);
            }

            live.AddRange(asleep);
            return live;
        }

        private static bool HasLevel(DeviceSnapshot s)
        {
            return s != null && s.Reading != null && s.Reading.Online && s.Reading.Percent.HasValue;
        }

        // ------------------------------------------------------------- refresh

        private void RefreshNow()
        {
            if (_refreshing)
            {
                // A read is already running. Queue one more instead of dropping the
                // request — dropping it is what made a click on the refresh icon spin
                // the arrow and change nothing at all.
                _refreshQueued = true;
                return;
            }

            _refreshing = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    _manager.Refresh();
                }
                catch
                {
                }
                finally
                {
                    _refreshing = false;
                }

                try
                {
                    List<DeviceSnapshot> snaps = _manager.Snapshot();
                    _form.BeginInvoke(new MethodInvoker(delegate
                    {
                        _form.UpdateSnapshot(snaps);
                        CheckLowBattery(snaps);
                    }));
                }
                catch
                {
                }

                if (_refreshQueued)
                {
                    _refreshQueued = false;
                    RefreshNow();
                }
            });
        }

        public void ShowPanel()
        {
            _form.ShowPanel();
        }

        public void Dispose()
        {
            // The popup is a separate top-level window: closing the tray app must not
            // leave it on screen.
            if (_menuForm != null && !_menuForm.IsDisposed)
            {
                try { _menuForm.Close(); } catch { }
                _menuForm = null;
            }

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
            }

            if (_alertPopup != null && !_alertPopup.IsDisposed)
            {
                try { _alertPopup.Close(); } catch { }
                _alertPopup.Dispose();
                _alertPopup = null;
            }

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Icon = null;
                _tray.Dispose();
            }

            if (_trayIcon != null)
            {
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            if (_formIcon != null)
            {
                _formIcon.Dispose();
                _formIcon = null;
            }

        }
    }
}
