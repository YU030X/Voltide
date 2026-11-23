using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Voltide.Core;

namespace Voltide.UI
{
    /// <summary>
    /// Non-activating bottom-right toast popup for low-battery alerts.
    /// Styled to match Voltide's dark card aesthetics and never steals focus.
    /// </summary>
    internal class LowBatteryPopup : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const float DesignW = 310f;
        private const float MarginPx = 16f;
        private const float RowH = 28f;
        private const int HoldMs = 12000;
        private const int TickMs = 16;

        private const string SmallBoltPath =
            "M104.13,163.15h-52.65c-4.24,0-7-4.46-5.1-8.25L130.53-13.1h96.53l-72.07,113.8" +
            "h63.06c4.87,0,7.5,5.71,4.33,9.41L33.7,330.7l70.43-167.55Z";

        private static readonly Color BgColor = Color.FromArgb(15, 15, 15);
        private static readonly Color BorderColor = Color.FromArgb(36, 255, 255, 255);
        private static readonly Color TitleColor = Color.FromArgb(242, 242, 242);
        private static readonly Color NameColor = Color.FromArgb(229, 203, 179);
        private static readonly Color MutedColor = Color.FromArgb(135, 135, 135);
        private static readonly Color ActionIdle = Color.FromArgb(160, 160, 160);
        private static readonly Color ActionHot = Color.FromArgb(225, 225, 225);
        private static readonly Color CloseIdle = Color.FromArgb(145, 145, 145);
        private static readonly Color CloseHot = Color.FromArgb(240, 240, 240);

        private enum AnimPhase
        {
            Hidden,
            FadeIn,
            Hold,
            FadeOut
        }

        private readonly List<LowBatteryAlert> _alerts = new List<LowBatteryAlert>();
        private readonly float _scale;
        private readonly System.Windows.Forms.Timer _timer;
        private GraphicsPath _boltPath;

        private int _threshold = 20;
        private AnimPhase _phase = AnimPhase.Hidden;
        private float _opacity;
        private int _holdElapsed;
        private bool _hovering;
        private bool _closeHot;
        private RectangleF _closeRect;

        public event EventHandler PanelRequested;

        public LowBatteryPopup()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                _scale = Math.Max(1f, g.DpiX / 96f);
            }

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = BgColor;
            Opacity = 0.0;

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = TickMs;
            _timer.Tick += OnAnimTick;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080 | 0x00000008 | 0x08000000; // WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int pref = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int));
            }
            catch
            {
            }
        }

        public void ShowAlerts(List<LowBatteryAlert> alerts, int threshold)
        {
            if (alerts == null || alerts.Count == 0) return;

            _threshold = threshold;

            if (!Visible || _phase == AnimPhase.Hidden)
            {
                _alerts.Clear();
            }

            foreach (LowBatteryAlert incoming in alerts)
            {
                if (incoming == null) continue;
                bool updated = false;
                for (int i = 0; i < _alerts.Count; i++)
                {
                    if (string.Equals(_alerts[i].DeviceId, incoming.DeviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        _alerts[i].DeviceName = incoming.DeviceName;
                        _alerts[i].Percent = incoming.Percent;
                        updated = true;
                        break;
                    }
                }
                if (!updated)
                {
                    _alerts.Add(incoming);
                }
            }

            LayoutPopup();

            _holdElapsed = 0;
            if (!Visible)
            {
                _opacity = 0f;
                Opacity = 0.0;
                _phase = AnimPhase.FadeIn;
                Show();
            }
            else
            {
                _phase = _opacity < 0.99f ? AnimPhase.FadeIn : AnimPhase.Hold;
            }

            _timer.Start();
            Invalidate();
        }

        public void Dismiss()
        {
            if (!Visible || _phase == AnimPhase.Hidden) return;
            _phase = AnimPhase.FadeOut;
            _timer.Start();
        }

        private void LayoutPopup()
        {
            int count = Math.Max(1, _alerts.Count);
            float designH = 108f + (count - 1) * RowH;
            int w = (int)Math.Round(DesignW * _scale);
            int h = (int)Math.Round(designH * _scale);
            int margin = (int)Math.Round(MarginPx * _scale);

            Rectangle area = Screen.PrimaryScreen != null
                ? Screen.PrimaryScreen.WorkingArea
                : new Rectangle(0, 0, 1920, 1040);

            Size = new Size(w, h);
            Location = new Point(area.Right - w - margin, area.Bottom - h - margin);

            float closeSize = 24f * _scale;
            _closeRect = new RectangleF(w - 10f * _scale - closeSize, 8f * _scale, closeSize, closeSize);
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            if (_phase == AnimPhase.FadeIn)
            {
                _opacity += TickMs / 180f;
                if (_opacity >= 1f)
                {
                    _opacity = 1f;
                    _phase = AnimPhase.Hold;
                    _holdElapsed = 0;
                }
                Opacity = _opacity;
            }
            else if (_phase == AnimPhase.Hold)
            {
                bool inside = Visible && ClientRectangle.Contains(PointToClient(Cursor.Position));
                if (!inside && !_hovering)
                {
                    _holdElapsed += TickMs;
                    if (_holdElapsed >= HoldMs)
                    {
                        _phase = AnimPhase.FadeOut;
                    }
                }
            }
            else if (_phase == AnimPhase.FadeOut)
            {
                _opacity -= TickMs / 220f;
                if (_opacity <= 0f)
                {
                    _opacity = 0f;
                    Opacity = 0.0;
                    _phase = AnimPhase.Hidden;
                    _timer.Stop();
                    Hide();
                    _alerts.Clear();
                    return;
                }
                Opacity = _opacity;
            }
            else
            {
                _timer.Stop();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            float s = _scale;

            using (SolidBrush bg = new SolidBrush(BgColor))
            {
                g.FillRectangle(bg, 0, 0, Width, Height);
            }

            int lowest = 100;
            foreach (LowBatteryAlert a in _alerts)
            {
                if (a.Percent < lowest) lowest = a.Percent;
            }
            Color accent = lowest < 20 ? CardRenderer.PrimaryFor(10) : CardRenderer.PrimaryFor(30);

            // Left accent bar
            using (SolidBrush bar = new SolidBrush(accent))
            {
                g.FillRectangle(bar, 0f, 0f, 4f * s, Height);
            }

            // Subtle 1px inner border
            using (Pen border = new Pen(BorderColor, 1f))
            {
                g.DrawRectangle(border, 0.5f, 0.5f, Width - 1f, Height - 1f);
            }

            // Header bolt icon
            if (_boltPath == null) _boltPath = SvgPath.Parse(SmallBoltPath);
            GraphicsState st = g.Save();
            g.TranslateTransform(16f * s, 14f * s);
            float bk = 13f * s / 256f;
            g.ScaleTransform(bk, bk);
            using (SolidBrush boltBrush = new SolidBrush(accent))
            {
                g.FillPath(boltBrush, _boltPath);
            }
            g.Restore(st);

            // Header title
            Font titleFont = FontManager.Get(FontKind.Chinese, 14f * s);
            using (SolidBrush titleBrush = new SolidBrush(TitleColor))
            using (StringFormat sf = new StringFormat())
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                float baseline = 26f * s;
                g.DrawString("低电量提醒", titleFont, titleBrush,
                    34f * s, baseline - FontManager.Ascent(titleFont), sf);
            }

            // Close button "×"
            float cx = _closeRect.Left + _closeRect.Width / 2f;
            float cy = _closeRect.Top + _closeRect.Height / 2f;
            float arm = 4.5f * s;
            using (Pen cp = new Pen(_closeHot ? CloseHot : CloseIdle, Math.Max(1.4f, 1.5f * s)))
            {
                cp.StartCap = LineCap.Round;
                cp.EndCap = LineCap.Round;
                g.DrawLine(cp, cx - arm, cy - arm, cx + arm, cy + arm);
                g.DrawLine(cp, cx - arm, cy + arm, cx + arm, cy - arm);
            }

            // Device rows
            float rowTop = 42f * s;
            float leftX = 16f * s;
            float rightEdge = Width - 16f * s;
            Font pctFont = FontManager.Get(FontKind.MontserratSemiBold, 14f * s);

            for (int i = 0; i < _alerts.Count; i++)
            {
                LowBatteryAlert a = _alerts[i];
                float baseline = rowTop + i * RowH * s + 19f * s;

                string pctText = a.Percent + "%";
                Color pctColor = CardRenderer.PrimaryFor(a.Percent);
                float pctW;
                using (StringFormat sfNum = new StringFormat(StringFormat.GenericTypographic))
                {
                    sfNum.FormatFlags |= StringFormatFlags.NoWrap;
                    pctW = g.MeasureString(pctText, pctFont, int.MaxValue, sfNum).Width;
                    using (SolidBrush pb = new SolidBrush(pctColor))
                    {
                        g.DrawString(pctText, pctFont, pb,
                            rightEdge - pctW, baseline - FontManager.Ascent(pctFont), sfNum);
                    }
                }

                float maxNameW = (rightEdge - pctW - 10f * s) - leftX;
                string name = a.DeviceName != null ? a.DeviceName : "";
                bool cjk = CardRenderer.HasCjk(name);
                FontKind nameKind = cjk ? FontKind.Chinese : FontKind.MontserratMedium;
                float namePx = 13.5f * s;
                Font nameFont = FontManager.Get(nameKind, namePx);

                using (StringFormat sfName = cjk ? new StringFormat() : new StringFormat(StringFormat.GenericTypographic))
                {
                    sfName.FormatFlags |= StringFormatFlags.NoWrap;
                    sfName.Trimming = StringTrimming.EllipsisCharacter;
                    float measured = g.MeasureString(name, nameFont, int.MaxValue, sfName).Width;
                    if (measured > maxNameW && measured > 0f)
                    {
                        namePx = Math.Max(10f * s, namePx * (maxNameW / measured));
                        nameFont = FontManager.Get(nameKind, namePx);
                    }

                    using (SolidBrush nb = new SolidBrush(NameColor))
                    {
                        RectangleF nameRect = new RectangleF(leftX, baseline - FontManager.Ascent(nameFont),
                            Math.Max(20f, maxNameW), nameFont.GetHeight(g) + 4f);
                        g.DrawString(name, nameFont, nb, nameRect, sfName);
                    }
                }
            }

            // Footer line
            float footerBaseline = Height - 15f * s;
            Font hintFont = FontManager.Get(FontKind.Chinese, 11f * s);
            using (StringFormat sfFoot = new StringFormat())
            {
                sfFoot.FormatFlags |= StringFormatFlags.NoWrap;
                using (SolidBrush mb = new SolidBrush(MutedColor))
                {
                    g.DrawString("电量低于 " + _threshold + "%，请及时充电", hintFont, mb,
                        leftX, footerBaseline - FontManager.Ascent(hintFont), sfFoot);
                }

                string actionText = "点击查看";
                float actionW = g.MeasureString(actionText, hintFont, int.MaxValue, sfFoot).Width;
                using (SolidBrush ab = new SolidBrush(_hovering && !_closeHot ? ActionHot : ActionIdle))
                {
                    g.DrawString(actionText, hintFont, ab,
                        rightEdge - actionW, footerBaseline - FontManager.Ascent(hintFont), sfFoot);
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovering = true;
            if (_phase == AnimPhase.FadeOut) _phase = AnimPhase.FadeIn;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovering = false;
            _closeHot = false;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool closeHot = _closeRect.Contains(e.X, e.Y);
            if (closeHot != _closeHot)
            {
                _closeHot = closeHot;
                Invalidate();
            }
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            if (_closeRect.Contains(e.X, e.Y))
            {
                Dismiss();
                return;
            }

            Dismiss();
            if (PanelRequested != null) PanelRequested(this, EventArgs.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Dispose();
                }
                if (_boltPath != null)
                {
                    _boltPath.Dispose();
                    _boltPath = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
