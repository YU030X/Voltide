using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Voltide.UI
{
    public enum IconTheme
    {
        Color = 0,
        Light = 1,
        Dark = 2
    }

    /// <summary>
    /// Renders Logo.svg (256 x 256: a rounded plate with the bolt cut out) at any
    /// size, and packs the results into a multi-size .ico. Supports color and
    /// monochrome (light / dark) tray icon themes.
    ///
    /// Drawn rather than downscaled from one bitmap: the tray asks for 16 px at 100%
    /// and 32 px at 200%, and a single 256 px source blurred badly at 16.
    /// </summary>
    public static class IconFactory
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const string BoltPath =
            "M104.13,163.15h-52.65c-4.24,0-7-4.46-5.1-8.25L130.53-13.1h96.53l-72.07,113.8" +
            "h63.06c4.87,0,7.5,5.71,4.33,9.41L33.7,330.7l70.43-167.55Z";

        private static readonly float[] Stops =
            { 0f, 0.07f, 0.15f, 0.24f, 0.32f, 0.41f, 0.5f, 0.59f, 0.66f, 0.73f, 0.8f, 0.87f, 0.94f, 1f };

        private static readonly Color[] Colors = new Color[]
        {
            Color.Black,
            Color.FromArgb(0x11, 0x0f, 0x24),
            Color.FromArgb(0x22, 0x1f, 0x46),
            Color.FromArgb(0x2f, 0x2a, 0x61),
            Color.FromArgb(0x38, 0x33, 0x74),
            Color.FromArgb(0x3e, 0x38, 0x80),
            Color.FromArgb(0x40, 0x3a, 0x84),
            Color.FromArgb(0x3e, 0x38, 0x80),
            Color.FromArgb(0x38, 0x33, 0x75),
            Color.FromArgb(0x30, 0x2b, 0x63),
            Color.FromArgb(0x23, 0x20, 0x49),
            Color.FromArgb(0x13, 0x11, 0x28),
            Color.Black,
            Color.Black
        };

        private static readonly Color ColorPlate = Color.FromArgb(116, 221, 124);
        private static readonly Color LightPlate = Color.FromArgb(0xF2, 0xF2, 0xF2);
        private static readonly Color LightBolt = Color.FromArgb(0x12, 0x12, 0x12);
        private static readonly Color DarkPlate = Color.FromArgb(0x14, 0x14, 0x14);
        private static readonly Color DarkBolt = Color.FromArgb(0xF5, 0xF5, 0xF5);
        private static readonly Color DarkBorder = Color.FromArgb(110, 255, 255, 255);

        private static GraphicsPath _bolt;
        private static GraphicsPath _plate;
        private static GraphicsPath _innerPlate;

        private static void EnsurePaths()
        {
            if (_bolt != null) return;

            _bolt = SvgPath.Parse(BoltPath);
            _plate = RoundRect(0f, 0f, 256f, 256f, 50f);
            _innerPlate = RoundRect(6f, 6f, 244f, 244f, 44f);
        }

        public static string ThemeLabel(IconTheme theme)
        {
            switch (theme)
            {
                case IconTheme.Light: return "白黑";
                case IconTheme.Dark: return "黑白";
                default: return "彩色";
            }
        }

        public static IconTheme NextTheme(IconTheme theme)
        {
            switch (theme)
            {
                case IconTheme.Color: return IconTheme.Light;
                case IconTheme.Light: return IconTheme.Dark;
                default: return IconTheme.Color;
            }
        }

        /// <summary>Draws the logo at the requested pixel size using the default color theme.</summary>
        public static Bitmap Render(int size)
        {
            return Render(size, IconTheme.Color);
        }

        /// <summary>Draws the logo at the requested pixel size and theme.</summary>
        public static Bitmap Render(int size, IconTheme theme)
        {
            EnsurePaths();

            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);

                float k = size / 256f;
                g.ScaleTransform(k, k);

                Color plateColor;
                if (theme == IconTheme.Light) plateColor = LightPlate;
                else if (theme == IconTheme.Dark) plateColor = DarkPlate;
                else plateColor = ColorPlate;

                using (SolidBrush plateBrush = new SolidBrush(plateColor))
                {
                    g.FillPath(plateBrush, _plate);
                }

                GraphicsState state = g.Save();
                g.SetClip(_plate);

                if (theme == IconTheme.Light)
                {
                    using (SolidBrush boltBrush = new SolidBrush(LightBolt))
                    {
                        g.FillPath(boltBrush, _bolt);
                    }
                }
                else if (theme == IconTheme.Dark)
                {
                    using (SolidBrush boltBrush = new SolidBrush(DarkBolt))
                    {
                        g.FillPath(boltBrush, _bolt);
                    }
                    using (Pen borderPen = new Pen(DarkBorder, 12f))
                    {
                        g.DrawPath(borderPen, _innerPlate);
                    }
                }
                else
                {
                    using (LinearGradientBrush grad = new LinearGradientBrush(
                        new PointF(32.18f, 320.43f), new PointF(344.18f, -267.28f),
                        Color.Black, Color.Black))
                    {
                        ColorBlend blend = new ColorBlend();
                        blend.Positions = Stops;
                        blend.Colors = Colors;
                        grad.InterpolationColors = blend;

                        g.FillPath(grad, _bolt);
                    }
                }

                g.Restore(state);
            }

            return bmp;
        }

        public static Icon ToIcon(int size)
        {
            return ToIcon(size, IconTheme.Color);
        }

        public static Icon ToIcon(int size, IconTheme theme)
        {
            using (Bitmap bmp = Render(size, theme))
            {
                IntPtr handle = bmp.GetHicon();
                Icon icon = (Icon)Icon.FromHandle(handle).Clone();
                DestroyIcon(handle);
                return icon;
            }
        }

        /// <summary>
        /// Writes a Windows .ico containing one PNG entry per size. PNG entries are
        /// what Vista and later prefer; a single-size icon would be resampled by
        /// Explorer and the tray, which is exactly the blur we are avoiding.
        /// </summary>
        public static void WriteIco(string path, int[] sizes)
        {
            List<byte[]> images = new List<byte[]>();

            foreach (int size in sizes)
            {
                using (Bitmap bmp = Render(size))
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images.Add(ms.ToArray());
                }
            }

            using (FileStream fs = File.Create(path))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((ushort)0);              // reserved
                w.Write((ushort)1);              // type: icon
                w.Write((ushort)sizes.Length);

                long offset = 6 + 16 * sizes.Length;

                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    byte dim = (byte)(s >= 256 ? 0 : s);

                    w.Write(dim);                // width
                    w.Write(dim);                // height
                    w.Write((byte)0);            // palette
                    w.Write((byte)0);            // reserved
                    w.Write((ushort)1);          // colour planes
                    w.Write((ushort)32);         // bits per pixel
                    w.Write((uint)images[i].Length);
                    w.Write((uint)offset);

                    offset += images[i].Length;
                }

                foreach (byte[] image in images)
                {
                    w.Write(image);
                }
            }
        }

        private static GraphicsPath RoundRect(float x, float y, float w, float h, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2f;

            p.AddArc(x, y, d, d, 180f, 90f);
            p.AddArc(x + w - d, y, d, d, 270f, 90f);
            p.AddArc(x + w - d, y + h - d, d, d, 0f, 90f);
            p.AddArc(x, y + h - d, d, d, 90f, 90f);
            p.CloseFigure();

            return p;
        }
    }
}
