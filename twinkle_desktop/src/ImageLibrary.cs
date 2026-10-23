using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;

namespace TwinkleDesktop
{
    public sealed class LibraryItem : AnimationSpec
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Group { get; set; }
        public string Preview { get; set; }
        public bool IsUser { get; set; }
    }

    public sealed class ImageLibrary
    {
        public readonly string Root;
        public readonly List<LibraryItem> Items = new List<LibraryItem>();
        public ImageLibrary(string root)
        {
            Root = root;
            foreach (string relative in new[] { "library/catalog.json", "user_images/catalog.json" })
            {
                string path = Path.Combine(root, relative);
                if (File.Exists(path)) Items.AddRange(new JavaScriptSerializer().Deserialize<List<LibraryItem>>(File.ReadAllText(path)));
            }
        }
        public LibraryItem Find(string id) { return Items.FirstOrDefault(v => v.Id == id); }
        public Clip Load(LibraryItem item) { return Clip.Load(item, Root); }
        public BitmapSource Thumbnail(LibraryItem item)
        {
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 144;
            image.UriSource = new Uri(Path.Combine(Root, item.Preview)); image.EndInit(); image.Freeze(); return image;
        }
        public LibraryItem Import(string path)
        {
            List<RasterFrame> frames = RasterDecoder.Decode(path);
            if (frames.Count == 0) throw new InvalidDataException("이미지를 읽을 수 없습니다.");
            string id = "user_" + Guid.NewGuid().ToString("N"); string relative = "user_images/" + id;
            string directory = Path.Combine(Root, relative); Directory.CreateDirectory(directory);
            var files = new List<string>(); var durations = new List<double>(); BitmapSource preview = null; long best = -1;
            for (int i = 0; i < frames.Count; ++i)
            {
                var normalized = Clip.NormalizeImage(frames[i].Source); string name = i.ToString("000") + ".png";
                SavePng(normalized, Path.Combine(directory, name)); files.Add(relative + "/" + name); durations.Add(Math.Max(20, frames[i].Duration));
                var rgba = new FormatConvertedBitmap(normalized, PixelFormats.Bgra32, null, 0); byte[] pixels = new byte[rgba.PixelWidth * rgba.PixelHeight * 4]; rgba.CopyPixels(pixels, rgba.PixelWidth * 4, 0);
                long visible = 0; for (int p = 3; p < pixels.Length; p += 4) if (pixels[p] > 10) ++visible;
                if (visible > best) { preview = normalized; best = visible; }
            }
            SavePng(preview, Path.Combine(directory, "preview.png"));
            var item = new LibraryItem { Id = id, Title = Path.GetFileNameWithoutExtension(path), Group = "내가 추가한 이미지", Preview = relative + "/preview.png", files = files, durations_ms = durations, Normalize = false, IsUser = true };
            // Keep the attached file, so moving/deleting the original does not break the pet.
            File.Copy(path, Path.Combine(directory, "original" + Path.GetExtension(path)), true);
            var users = Items.Where(v => v.IsUser).ToList(); users.Add(item);
            File.WriteAllText(Path.Combine(Root, "user_images", "catalog.json"), new JavaScriptSerializer().Serialize(users)); Items.Add(item); return item;
        }
        public static void SavePng(BitmapSource image, string path)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var stream = File.Create(path)) encoder.Save(stream);
        }
    }

    public sealed class RasterFrame
    {
        public BitmapSource Source; public double Duration;
    }

    public static class RasterDecoder
    {
        public sealed class CrawlCollection { public List<CrawlAsset> assets { get; set; } }
        public sealed class CrawlAsset { public string id { get; set; } public string original_file { get; set; } public int frames { get; set; } }
        public static void VerifyLibrary(string catalogPath, string output)
        {
            Directory.CreateDirectory(output); string root = Path.GetDirectoryName(catalogPath);
            var assets = new JavaScriptSerializer().Deserialize<CrawlCollection>(File.ReadAllText(catalogPath)).assets;
            foreach (var asset in assets)
            {
                var decoded = Decode(Path.Combine(root, asset.original_file));
                if (decoded.Count != asset.frames) throw new Exception(asset.id + ": decoder frame count mismatch");
                string target = Path.Combine(output, asset.id); Directory.CreateDirectory(target);
                for (int i = 0; i < decoded.Count; ++i) ImageLibrary.SavePng(decoded[i].Source, Path.Combine(target, i.ToString("000") + ".png"));
                File.WriteAllText(Path.Combine(target, "timing.json"), new JavaScriptSerializer().Serialize(decoded.Select(f => f.Duration).ToArray()));
            }
            File.WriteAllText(Path.Combine(output, "RESULT.txt"), "PASS: decoded all " + assets.Count + " GIF/APNG originals with expected frame counts.");
        }
        sealed class Chunk { public string Type; public byte[] Data; }
        sealed class PngFrame { public int W, H, X, Y, Dispose, Blend; public double Delay; public List<byte[]> Data = new List<byte[]>(); }
        static uint Read(byte[] b, int p) { return ((uint)b[p] << 24) | ((uint)b[p + 1] << 16) | ((uint)b[p + 2] << 8) | b[p + 3]; }
        static void Put(byte[] b, int p, int v) { b[p] = (byte)(v >> 24); b[p + 1] = (byte)(v >> 16); b[p + 2] = (byte)(v >> 8); b[p + 3] = (byte)v; }
        static void Big(Stream s, uint v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
        static void WriteChunk(Stream s, string type, byte[] data)
        {
            byte[] t = System.Text.Encoding.ASCII.GetBytes(type); Big(s, (uint)data.Length); s.Write(t, 0, 4); s.Write(data, 0, data.Length);
            uint crc = 0xffffffff;
            foreach (byte value in t.Concat(data)) { crc ^= value; for (int j = 0; j < 8; ++j) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1; }
            Big(s, crc ^ 0xffffffff);
        }
        static void CheckSize(int w, int h, int count)
        {
            if (w <= 0 || h <= 0 || w > 8192 || h > 8192 || count > 500 || (long)w * h * Math.Max(1, count) > 80000000) throw new InvalidDataException("이미지가 너무 큽니다. 크기나 프레임 수를 줄인 파일을 골라 주세요.");
        }
        static BitmapSource Bitmap(Drawing.Bitmap image)
        {
            using (var stream = new MemoryStream())
            {
                image.Save(stream, Drawing.Imaging.ImageFormat.Png); stream.Position = 0;
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0]; frame.Freeze(); return frame;
            }
        }
        public static List<RasterFrame> Decode(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length > 64000000) throw new InvalidDataException("64MB 이하의 이미지 파일을 골라 주세요.");
            if (bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71)
            {
                var animated = DecodePng(bytes); if (animated != null) return animated;
            }
            var result = new List<RasterFrame>();
            using (var stream = new MemoryStream(bytes))
            using (var image = Drawing.Image.FromStream(stream))
            {
                bool gif = image.RawFormat.Guid == Drawing.Imaging.ImageFormat.Gif.Guid;
                int count = gif ? image.GetFrameCount(Drawing.Imaging.FrameDimension.Time) : 1; CheckSize(image.Width, image.Height, count);
                byte[] delays = null;
                if (gif) { try { delays = image.GetPropertyItem(0x5100).Value; } catch (ArgumentException) { } }
                for (int i = 0; i < count; ++i)
                {
                    if (gif) image.SelectActiveFrame(Drawing.Imaging.FrameDimension.Time, i);
                    using (var canvas = new Drawing.Bitmap(image.Width, image.Height, Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        using (var g = Drawing.Graphics.FromImage(canvas)) { g.CompositingMode = Drawing.Drawing2D.CompositingMode.SourceCopy; g.DrawImageUnscaled(image, 0, 0); }
                        double delay = delays != null && delays.Length >= (i + 1) * 4 ? BitConverter.ToInt32(delays, i * 4) * 10 : 100;
                        result.Add(new RasterFrame { Source = Bitmap(canvas), Duration = delay >= 20 ? delay : 100 });
                    }
                }
            }
            return result;
        }
        static List<RasterFrame> DecodePng(byte[] bytes)
        {
            byte[] header = null; int width = 0, height = 0; bool animated = false, seenData = false;
            var common = new List<Chunk>(); var frames = new List<PngFrame>(); PngFrame current = null;
            for (int p = 8; p + 12 <= bytes.Length; )
            {
                uint length = Read(bytes, p); if (length > 64000000 || p + 12L + length > bytes.Length) throw new InvalidDataException("PNG 파일이 손상되었습니다.");
                string type = System.Text.Encoding.ASCII.GetString(bytes, p + 4, 4); byte[] data = new byte[(int)length]; Buffer.BlockCopy(bytes, p + 8, data, 0, data.Length); p += data.Length + 12;
                if (type == "IHDR") { header = data; width = (int)Read(data, 0); height = (int)Read(data, 4); CheckSize(width, height, 1); }
                else if (type == "acTL") { animated = true; CheckSize(width, height, (int)Read(data, 0)); }
                else if (type == "fcTL")
                {
                    if (data.Length != 26) throw new InvalidDataException("APNG 프레임 정보가 올바르지 않습니다.");
                    if (current != null) frames.Add(current);
                    int denominator = (data[22] << 8) | data[23]; if (denominator == 0) denominator = 100;
                    double delay = ((data[20] << 8) | data[21]) * 1000.0 / denominator;
                    current = new PngFrame { W = (int)Read(data, 4), H = (int)Read(data, 8), X = (int)Read(data, 12), Y = (int)Read(data, 16), Delay = delay >= 20 ? delay : 100, Dispose = data[24], Blend = data[25] };
                    if (current.X < 0 || current.Y < 0 || current.W < 1 || current.H < 1 || (long)current.X + current.W > width || (long)current.Y + current.H > height) throw new InvalidDataException("APNG 프레임 범위가 올바르지 않습니다.");
                }
                else if (type == "IDAT") { seenData = true; if (current != null) current.Data.Add(data); }
                else if (type == "fdAT") { if (current == null || data.Length < 4) throw new InvalidDataException("APNG 데이터가 올바르지 않습니다."); current.Data.Add(data.Skip(4).ToArray()); }
                else if (!seenData && new[] { "PLTE", "tRNS", "gAMA", "sRGB", "iCCP", "cHRM" }.Contains(type)) common.Add(new Chunk { Type = type, Data = data });
            }
            if (!animated) return null;
            if (current != null) frames.Add(current); CheckSize(width, height, frames.Count);
            var result = new List<RasterFrame>(); var canvas = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                foreach (var frame in frames)
                {
                    Drawing.Bitmap previous = frame.Dispose == 2 ? (Drawing.Bitmap)canvas.Clone() : null;
                    using (var png = new MemoryStream())
                    {
                        png.Write(bytes, 0, 8); byte[] ihdr = (byte[])header.Clone(); Put(ihdr, 0, frame.W); Put(ihdr, 4, frame.H); WriteChunk(png, "IHDR", ihdr);
                        foreach (var chunk in common) WriteChunk(png, chunk.Type, chunk.Data);
                        foreach (var block in frame.Data) WriteChunk(png, "IDAT", block);
                        WriteChunk(png, "IEND", new byte[0]); png.Position = 0;
                        using (var patch = new Drawing.Bitmap(png))
                        using (var g = Drawing.Graphics.FromImage(canvas))
                        {
                            if (frame.Blend == 0) { g.CompositingMode = Drawing.Drawing2D.CompositingMode.SourceCopy; using (var brush = new Drawing.SolidBrush(Drawing.Color.Transparent)) g.FillRectangle(brush, frame.X, frame.Y, frame.W, frame.H); }
                            g.CompositingMode = Drawing.Drawing2D.CompositingMode.SourceOver; g.DrawImageUnscaled(patch, frame.X, frame.Y);
                        }
                    }
                    result.Add(new RasterFrame { Source = Bitmap(canvas), Duration = frame.Delay });
                    if (frame.Dispose == 1)
                    {
                        using (var g = Drawing.Graphics.FromImage(canvas)) { g.CompositingMode = Drawing.Drawing2D.CompositingMode.SourceCopy; using (var brush = new Drawing.SolidBrush(Drawing.Color.Transparent)) g.FillRectangle(brush, frame.X, frame.Y, frame.W, frame.H); }
                    }
                    else if (frame.Dispose == 2) { canvas.Dispose(); canvas = previous; previous = null; }
                    if (previous != null) previous.Dispose();
                }
            }
            finally { canvas.Dispose(); }
            return result;
        }
    }

    public sealed class ImagePicker : Window
    {
        readonly ImageLibrary library;
        readonly Func<ActivityMode, string> currentChoice;
        readonly Action<ActivityMode, LibraryItem> apply;
        readonly Action<ActivityMode> reset;
        readonly ComboBox modes = new ComboBox();
        readonly WrapPanel cards = new WrapPanel();
        readonly Image preview = new Image { Width = 210, Height = 210, Stretch = Stretch.Uniform };
        readonly TextBlock title = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 12, 6, 6) };
        readonly TextBlock detail = new TextBlock { FontSize = 12, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
        readonly TextBox search = new TextBox { MinWidth = 180, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(12, 0, 0, 0), ToolTip = "이름이나 모습으로 검색" };
        readonly Dictionary<string, Border> borders = new Dictionary<string, Border>();
        readonly DispatcherTimer animation = new DispatcherTimer(); readonly Stopwatch clock = Stopwatch.StartNew();
        LibraryItem selected; Clip clip; double started;
        ActivityMode Mode { get { return (ActivityMode)modes.SelectedIndex; } }

        public ImagePicker(PetWindow owner, ImageLibrary images, ActivityMode mode, Func<ActivityMode, string> choice, Action<ActivityMode, LibraryItem> onApply, Action<ActivityMode> onReset)
        {
            if (owner.IsLoaded) Owner = owner; library = images; currentChoice = choice; apply = onApply; reset = onReset;
            Title = "상황별 캐릭터 이미지"; Width = 860; Height = 650; MinWidth = 790; MinHeight = 550;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false; Topmost = owner.Topmost; Background = PetWindow.ColorBrush("#FAF8F3"); FontFamily = owner.FontFamily;
            var root = new DockPanel { Margin = new Thickness(20), Background = Background }; Content = root;
            var top = new StackPanel { Margin = new Thickness(0, 0, 0, 16) }; DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
            top.Children.Add(new TextBlock { Text = "상황에 맞는 모습을 골라요", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
            var tools = new StackPanel { Orientation = Orientation.Horizontal }; top.Children.Add(tools);
            modes.Width = 95; modes.FontSize = 14; modes.Padding = new Thickness(6); foreach (ActivityMode m in Enum.GetValues(typeof(ActivityMode))) modes.Items.Add(PetWindow.ModeLabel(m)); modes.SelectedIndex = (int)mode; tools.Children.Add(modes);
            tools.Children.Add(new TextBlock { Text = "검색", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Foreground = Brushes.Gray });
            search.Width = 190; search.Margin = new Thickness(6, 0, 0, 0); tools.Children.Add(search);
            var add = PetWindow.RoundButton(); add.Content = "파일 추가"; add.Width = 104; add.Margin = new Thickness(12, 0, 0, 0); add.Background = Brushes.White; add.Foreground = PetWindow.ColorBrush("#65587B"); add.Click += AddFile; tools.Children.Add(add);
            var defaults = PetWindow.RoundButton(); defaults.Content = "기본 이미지"; defaults.Width = 108; defaults.Margin = new Thickness(10, 0, 0, 0); defaults.Background = Brushes.White; defaults.Foreground = PetWindow.ColorBrush("#65587B"); defaults.Click += delegate { reset(Mode); Select(library.Find(currentChoice(Mode))); }; tools.Children.Add(defaults);
            var bottom = new DockPanel { Margin = new Thickness(0, 14, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
            var accept = PetWindow.RoundButton(); accept.Content = "적용"; accept.Width = 100; accept.Background = PetWindow.ColorBrush("#7060F7"); accept.Foreground = Brushes.White; DockPanel.SetDock(accept, Dock.Right); bottom.Children.Add(accept); accept.Click += delegate { if (selected != null) { apply(Mode, selected); Close(); } };
            bottom.Children.Add(new TextBlock { Text = "GIF · 움직이는 PNG(APNG) · PNG · JPG를 추가할 수 있어요.\nPNG/JPG 한 장은 정지 이미지로 표시돼요.", FontSize = 12, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            var side = new StackPanel { Width = 222, Margin = new Thickness(16, 0, 0, 0) }; DockPanel.SetDock(side, Dock.Right); root.Children.Add(side);
            var previewBox = new Border { Background = Checker(), CornerRadius = new CornerRadius(12), Padding = new Thickness(5), Child = preview }; side.Children.Add(previewBox); side.Children.Add(title); side.Children.Add(detail);
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = cards }; root.Children.Add(scroll);
            search.TextChanged += delegate { Populate(); }; modes.SelectionChanged += delegate { Select(library.Find(currentChoice(Mode))); };
            animation.Interval = TimeSpan.FromMilliseconds(33); animation.Tick += delegate { if (clip != null) preview.Source = clip.At(clock.Elapsed.TotalMilliseconds - started); };
            Loaded += delegate { animation.Start(); }; Closed += delegate { animation.Stop(); clip = null; };
            Populate(); Select(library.Find(currentChoice(mode)) ?? library.Items.FirstOrDefault());
        }
        static Brush Checker()
        {
            var group = new DrawingGroup(); group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
            var gray = PetWindow.ColorBrush("#EEF0F3"); group.Children.Add(new GeometryDrawing(gray, null, new RectangleGeometry(new Rect(0, 0, 8, 8)))); group.Children.Add(new GeometryDrawing(gray, null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
            var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute }; brush.Freeze(); return brush;
        }
        void Populate()
        {
            cards.Children.Clear(); borders.Clear(); string q = search.Text.Trim();
            foreach (var item in library.Items.Where(v => q.Length == 0 || (v.Title + " " + v.Group).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var inner = new StackPanel(); var image = new Image { Height = 108, Margin = new Thickness(5), Stretch = Stretch.Uniform };
                try { image.Source = library.Thumbnail(item); } catch { image.Source = null; }
                inner.Children.Add(new Border { Background = Checker(), CornerRadius = new CornerRadius(8), Child = image });
                inner.Children.Add(new TextBlock { Text = item.Title, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3, 6, 3, 3), Height = 34 });
                var border = new Border { Width = 157, Height = 166, Margin = new Thickness(3), Padding = new Thickness(6), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12), Background = Brushes.White, Child = inner };
                LibraryItem chosen = item; border.MouseLeftButtonUp += delegate { Select(chosen); }; border.Cursor = System.Windows.Input.Cursors.Hand; cards.Children.Add(border); borders[item.Id] = border;
            }
            Highlight();
        }
        void Highlight() { foreach (var pair in borders) pair.Value.BorderBrush = PetWindow.ColorBrush(selected != null && selected.Id == pair.Key ? "#7060F7" : "#E8E2D9"); }
        void Select(LibraryItem item)
        {
            if (item == null) return;
            try
            {
                var loaded = library.Load(item); clip = loaded; selected = item; started = clock.Elapsed.TotalMilliseconds; preview.Source = clip.At(0); title.Text = item.Title;
                detail.Text = item.Group + "\n" + (clip.Frames.Length > 1 ? "움직이는 이미지 · " + clip.Frames.Length + "프레임" : "정지 이미지") + "\n\n'" + PetWindow.ModeLabel(Mode) + "' 상황에 적용합니다."; Highlight();
            }
            catch (Exception error) { MessageBox.Show(this, "이미지를 읽을 수 없어요.\n" + error.Message, "이미지 확인"); }
        }
        void AddFile(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "캐릭터 이미지 추가", Filter = "이미지 파일|*.gif;*.png;*.apng;*.jpg;*.jpeg|모든 파일|*.*", CheckFileExists = true };
            string crawled = Path.GetFullPath(Path.Combine(library.Root, "..", "twinkle_twinkle", "characters")); if (Directory.Exists(crawled)) dialog.InitialDirectory = crawled;
            if (dialog.ShowDialog(this) != true) return;
            try { var item = library.Import(dialog.FileName); search.Text = ""; Populate(); Select(item); }
            catch (Exception error) { MessageBox.Show(this, "파일을 추가하지 못했어요.\n" + error.Message, "이미지 추가"); }
        }
        public void ValidateCards()
        {
            if (borders.Count != library.Items.Count) throw new Exception("Some library cards are missing");
            foreach (var border in borders.Values)
            {
                var panel = (StackPanel)border.Child; var frame = (Border)panel.Children[0]; var image = (Image)frame.Child;
                if (image.Source == null) throw new Exception("Library thumbnail did not load");
            }
        }
    }
}
