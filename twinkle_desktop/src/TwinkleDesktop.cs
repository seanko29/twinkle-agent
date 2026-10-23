using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("트윙클 데스크탑")]
[assembly: System.Reflection.AssemblyVersion("0.8.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.8.0.0")]

namespace TwinkleDesktop
{
    public enum ActivityMode { Focus, Rest, Meeting, Walk }

    public sealed class PetSettings
    {
        public double CharacterSize { get; set; }
        public Dictionary<string, string> ImageChoices { get; set; }
        public PetSettings() { CharacterSize = 144; ImageChoices = new Dictionary<string, string>(); }
    }

    public static class SizeSettings
    {
        public static double Normalize(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 144 : Math.Round(Math.Max(80, Math.Min(320, value)));
        }
        public static PetSettings Load(string path)
        {
            try
            {
                var value = new JavaScriptSerializer().Deserialize<PetSettings>(File.ReadAllText(path));
                if (value == null) return new PetSettings();
                value.CharacterSize = Normalize(value.CharacterSize);
                if (value.ImageChoices == null) value.ImageChoices = new Dictionary<string, string>(); return value;
            }
            catch { return new PetSettings(); }
        }
        public static void Save(string path, double size)
        { Save(path, new PetSettings { CharacterSize = Normalize(size) }); }
        public static void Save(string path, PetSettings settings)
        {
            string temporary = path + ".tmp";
            settings.CharacterSize = Normalize(settings.CharacterSize);
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(settings));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
    }

    public sealed class FocusSession
    {
        public bool Active { get; private set; }
        public bool Paused { get; private set; }
        double accumulated, started;
        public void Start(double now) { Active = true; Paused = false; accumulated = 0; started = now; }
        public void Pause(double now) { if (!Active || Paused) return; accumulated += Math.Max(0, now - started); Paused = true; }
        public void Resume(double now) { if (!Active || !Paused) return; started = now; Paused = false; }
        public double Elapsed(double now) { return accumulated + (Active && !Paused ? Math.Max(0, now - started) : 0); }
        public double Stop(double now) { double result = Elapsed(now); Active = false; Paused = false; accumulated = 0; return result; }
    }

    public class AnimationSpec
    {
        public List<string> files { get; set; }
        public List<double> durations_ms { get; set; }
        public bool Normalize { get; set; }
    }

    public sealed class Clip
    {
        public BitmapSource[] Frames;
        public double[] Durations;
        public double Duration;
        public static int FrameAt(double time, double[] durations)
        {
            double total = durations.Sum();
            if (total <= 0) return 0;
            time = Math.Max(0, time) % total;
            for (int i = 0; i < durations.Length; ++i) { if (time < durations[i]) return i; time -= durations[i]; }
            return durations.Length - 1;
        }
        public BitmapSource At(double milliseconds) { return Frames[FrameAt(milliseconds, Durations)]; }
        public Clip Mirrored()
        {
            var frames = new List<BitmapSource>();
            foreach (var frame in Frames) { var flipped = new TransformedBitmap(frame, new ScaleTransform(-1, 1)); flipped.Freeze(); frames.Add(flipped); }
            return new Clip { Frames = frames.ToArray(), Durations = Durations, Duration = Duration };
        }
        public static Clip Load(AnimationSpec spec, string root)
        {
            if (spec.files == null || spec.durations_ms == null || spec.files.Count != spec.durations_ms.Count || spec.files.Count == 0) throw new InvalidDataException("애니메이션 정보가 올바르지 않습니다.");
            var images = new List<BitmapSource>();
            foreach (string file in spec.files)
            {
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 384;
                image.UriSource = new Uri(System.IO.Path.Combine(root, file.Replace('/', System.IO.Path.DirectorySeparatorChar))); image.EndInit(); image.Freeze();
                images.Add(spec.Normalize ? NormalizeImage(image) : image);
            }
            double[] durations = spec.durations_ms.Select(d => Math.Max(20, d)).ToArray();
            return new Clip { Frames = images.ToArray(), Durations = durations, Duration = durations.Sum() };
        }
        public static BitmapSource NormalizeImage(BitmapSource source)
        {
            double scale = Math.Min(336.0 / source.PixelWidth, 336.0 / source.PixelHeight);
            double width = source.PixelWidth * scale, height = source.PixelHeight * scale;
            var visual = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var drawing = visual.RenderOpen()) drawing.DrawImage(source, new Rect((384 - width) / 2, 360 - height, width, height));
            var result = new RenderTargetBitmap(384, 384, 96, 96, PixelFormats.Pbgra32); result.Render(visual); result.Freeze(); return result;
        }
    }

    public static class Motion
    {
        public static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(value, Math.Max(min, max))); }
        public static double Approach(double current, double target, double speed, double dt)
        {
            double step = Math.Max(0, speed * dt);
            return current < target ? Math.Min(target, current + step) : Math.Max(target, current - step);
        }
        public static string TimeText(double seconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds)));
            return t.TotalHours >= 1 ? ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : ((int)t.TotalMinutes).ToString("00") + ":" + t.Seconds.ToString("00");
        }
    }

    public sealed class PetWindow : Window
    {
        public readonly Canvas Scene = new Canvas();
        readonly Canvas bubble = new Canvas();
        readonly Image pet = new Image();
        readonly TextBlock status = new TextBlock(), time = new TextBlock();
        readonly System.Windows.Shapes.Ellipse dot = new System.Windows.Shapes.Ellipse();
        readonly Button primary, secondary;
        readonly Dictionary<ActivityMode, Button> modeButtons = new Dictionary<ActivityMode, Button>();
        readonly FocusSession focus = new FocusSession();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Dictionary<string, Clip> clips = new Dictionary<string, Clip>();
        readonly Dictionary<string, Clip> defaultClips = new Dictionary<string, Clip>();
        readonly ImageLibrary library;
        readonly PetSettings preferences;
        readonly DispatcherTimer timer = new DispatcherTimer();
        readonly DispatcherTimer settingsTimer = new DispatcherTimer();
        readonly Random random = new Random();
        readonly string folder;
        readonly bool persistPreferences;
        Forms.NotifyIcon tray;
        NotificationBridge notifications;
        MailService mail;
        MailWindow mailWindow;
        PetNotification notification;
        EventWaitHandle quit;
        bool roaming = true, walking, pointerDown, dragging, menuOpen, previewMode;
        double targetX, nextWalk = 7, reactionUntil, farewellUntil, notificationUntil, bubbleUntil = 12, lastTick, stateStarted, pausedAnimationAge;
        double dragX, dragY, dragLeft, dragTop, size = 144;
        double? previewElapsed;
        string currentState = "idle", lastUiKey = "";
        ActivityMode mode = ActivityMode.Walk;
        const string QuitName = "Local\\TwinkleDesktopQuit_20261022";
        [StructLayout(LayoutKind.Sequential)] struct CursorPoint { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out CursorPoint point);

        public PetWindow(string directory, bool preview, bool persist = true)
        {
            folder = directory; previewMode = preview; persistPreferences = persist;
            preferences = SizeSettings.Load(System.IO.Path.Combine(folder, "settings.json")); size = preferences.CharacterSize;
            Title = "트윙클 데스크탑";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Content = Scene;
            FontFamily = new FontFamily("Malgun Gothic");
            LoadAssets();
            library = new ImageLibrary(folder);
            foreach (var entry in clips) defaultClips[entry.Key] = entry.Value;
            foreach (ActivityMode activity in Enum.GetValues(typeof(ActivityMode)))
            {
                string selected;
                if (preferences.ImageChoices.TryGetValue(activity.ToString(), out selected))
                {
                    var item = library.Find(selected);
                    if (item != null) { try { AssignImage(activity, item, false); } catch { } }
                }
            }
            BuildBubble();
            primary = RoundButton(); secondary = RoundButton();
            Canvas.SetLeft(primary, 23); Canvas.SetTop(primary, 92); primary.Width = 116;
            Canvas.SetLeft(secondary, 149); Canvas.SetTop(secondary, 92); secondary.Width = 116;
            bubble.Children.Add(primary); bubble.Children.Add(secondary);
            primary.Click += delegate { if (!focus.Active) StartFocus(); else TogglePause(); };
            secondary.Click += delegate { if (focus.Active) StopFocus(); else ToggleRoaming(); };
            Scene.Children.Add(bubble);
            pet.Stretch = Stretch.Uniform;
            RenderOptions.SetBitmapScalingMode(pet, BitmapScalingMode.HighQuality);
            pet.Cursor = Cursors.Hand;
            Scene.Children.Add(pet);
            ApplySize(size);
            SetState("idle");
            pet.MouseLeftButtonDown += OnPointerDown;
            pet.MouseMove += OnPointerMove;
            pet.MouseLeftButtonUp += OnPointerUp;
            pet.LostMouseCapture += delegate { pointerDown = false; dragging = false; };
            pet.MouseRightButtonUp += delegate(object sender, MouseButtonEventArgs e) { OpenMenu(); e.Handled = true; };
            Scene.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    ApplySize(size + Math.Sign(e.Delta) * 8); nextWalk = clock.Elapsed.TotalSeconds + 2; e.Handled = true;
                }
            };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape && !focus.Active) bubbleUntil = 0; };
            timer.Interval = TimeSpan.FromMilliseconds(33);
            timer.Tick += Tick;
            settingsTimer.Interval = TimeSpan.FromMilliseconds(250);
            settingsTimer.Tick += delegate { settingsTimer.Stop(); SaveSize(); };
            Loaded += delegate
            {
                Rect area = WorkArea();
                Left = area.Right - Width - 42; Top = area.Bottom - Height - 8;
                targetX = Left;
                if (!previewMode)
                {
                    SetupTray(); quit = new EventWaitHandle(false, EventResetMode.ManualReset, QuitName); timer.Start();
                    notifications = new NotificationBridge(delegate(PetNotification value) { Dispatcher.BeginInvoke(new Action(delegate { ReceiveNotification(value); })); });
                    if (persistPreferences) { mail = new MailService(folder, delegate(PetNotification value) { Dispatcher.BeginInvoke(new Action(delegate { ReceiveNotification(value); })); }); mail.Start(); }
                }
            };
            Closed += delegate { timer.Stop(); settingsTimer.Stop(); SaveSize(); if (mail != null) mail.Dispose(); if (notifications != null) notifications.Dispose(); if (tray != null) { tray.Visible = false; tray.Dispose(); } if (quit != null) quit.Dispose(); };
            UpdateUi();
        }

        void LoadAssets()
        {
            var specs = new JavaScriptSerializer().Deserialize<Dictionary<string, AnimationSpec>>(File.ReadAllText(System.IO.Path.Combine(folder, "assets", "animations.json")));
            foreach (var entry in specs)
            {
                clips[entry.Key] = TwinkleDesktop.Clip.Load(entry.Value, folder);
            }
            foreach (string required in new[] { "walk_left", "walk_right", "idle", "focus", "rest", "meeting", "reaction", "farewell" }) if (!clips.ContainsKey(required)) throw new InvalidDataException(required);
        }

        internal static SolidColorBrush ColorBrush(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }

        void BuildBubble()
        {
            bubble.Width = 288; bubble.Height = 161;
            Canvas.SetTop(bubble, 9);
            var border = new Border { Width = 288, Height = 144, Background = Brushes.White, BorderBrush = ColorBrush("#AFD4B4"), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(21) };
            border.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = .13, Color = Colors.Black };
            bubble.Children.Add(border);
            var tail = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 128,143 L 143,158 Q 144,159 145,158 L 160,143"), Fill = Brushes.White, Stroke = ColorBrush("#AFD4B4"), StrokeThickness = 1.5 };
            bubble.Children.Add(tail);
            var cover = new System.Windows.Shapes.Rectangle { Width = 31, Height = 3, Fill = Brushes.White };
            Canvas.SetLeft(cover, 129); Canvas.SetTop(cover, 141); bubble.Children.Add(cover);
            var header = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            dot.Width = 10; dot.Height = 10; dot.Margin = new Thickness(0, 0, 10, 0); dot.Fill = ColorBrush("#B9DDBE");
            status.FontSize = 15; status.FontWeight = FontWeights.SemiBold; status.Foreground = ColorBrush("#85838B"); status.VerticalAlignment = VerticalAlignment.Center;
            time.FontSize = 25; time.FontWeight = FontWeights.Bold; time.Foreground = ColorBrush("#222126"); time.Margin = new Thickness(12, 0, 0, 0); time.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(dot); header.Children.Add(status); header.Children.Add(time);
            var headerBox = new Grid { Width = 288, Height = 41 }; headerBox.Children.Add(header);
            Canvas.SetTop(headerBox, 8); bubble.Children.Add(headerBox);
            int index = 0;
            foreach (ActivityMode activity in new[] { ActivityMode.Focus, ActivityMode.Rest, ActivityMode.Meeting, ActivityMode.Walk })
            {
                ActivityMode selected = activity; var button = RoundButton(); button.Width = 58; button.Height = 27; button.FontSize = 12;
                button.Content = ModeLabel(activity); button.ToolTip = ModeDescription(activity);
                Canvas.SetLeft(button, 19 + index * 64); Canvas.SetTop(button, 54);
                button.Click += delegate { SwitchMode(selected); }; bubble.Children.Add(button); modeButtons[activity] = button; index++;
            }
        }

        internal static Button RoundButton()
        {
            var template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='{TemplateBinding Background}' CornerRadius='13'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>");
            return new Button { Height = 36, FontSize = 14, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, BorderThickness = new Thickness(0), Template = template, FocusVisualStyle = null };
        }

        void ApplySize(double value)
        {
            value = SizeSettings.Normalize(value);
            if (IsLoaded && Math.Abs(value - size) < .01) return;
            double previousHeight = Height, previousWidth = Width;
            size = value; Width = Math.Max(312, size + 48); Height = 174 + size + 18;
            Scene.Width = Width; Scene.Height = Height;
            pet.Width = size; pet.Height = size;
            Canvas.SetLeft(pet, (Width - size) / 2); Canvas.SetTop(pet, 174);
            Canvas.SetLeft(bubble, (Width - bubble.Width) / 2);
            if (IsLoaded && !double.IsNaN(previousHeight))
            {
                Top += previousHeight - Height; Left += (previousWidth - Width) / 2; ClampToScreen();
                if (!previewMode && persistPreferences) { settingsTimer.Stop(); settingsTimer.Start(); }
            }
        }

        void SaveSize()
        {
            if (previewMode || !persistPreferences) return;
            try { preferences.CharacterSize = size; SizeSettings.Save(System.IO.Path.Combine(folder, "settings.json"), preferences); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        string CurrentImage(ActivityMode activity)
        {
            string selected;
            if (preferences.ImageChoices.TryGetValue(activity.ToString(), out selected) && library.Find(selected) != null) return selected;
            return activity == ActivityMode.Focus ? "base_14" : activity == ActivityMode.Rest ? "base_01" : activity == ActivityMode.Meeting ? "base_03" : "base_15";
        }

        void AssignImage(ActivityMode activity, LibraryItem item, bool save)
        {
            Clip loaded = library.Load(item);
            if (activity == ActivityMode.Walk)
            {
                clips["idle"] = loaded; clips["walk_right"] = loaded; clips["walk_left"] = loaded.Mirrored(); clips["reaction"] = loaded;
            }
            else clips[ModeState(activity)] = loaded;
            if (save)
            {
                preferences.ImageChoices[activity.ToString()] = item.Id; SaveSize();
                if (mode == activity) RefreshImage();
            }
        }

        void ResetImage(ActivityMode activity)
        {
            foreach (string key in activity == ActivityMode.Walk ? new[] { "idle", "walk_left", "walk_right", "reaction" } : new[] { ModeState(activity) }) clips[key] = defaultClips[key];
            preferences.ImageChoices.Remove(activity.ToString()); SaveSize(); if (mode == activity) RefreshImage();
        }

        void RefreshImage()
        {
            farewellUntil = 0;
            walking = false; nextWalk = clock.Elapsed.TotalSeconds + 4; stateStarted = clock.Elapsed.TotalMilliseconds; pausedAnimationAge = 0;
            SetState(ModeState(mode)); pet.Source = clips[currentState].At(0);
        }

        void OpenImagePicker(ActivityMode activity)
        {
            walking = false; menuOpen = true;
            try { new ImagePicker(this, library, activity, CurrentImage, delegate(ActivityMode m, LibraryItem item) { AssignImage(m, item, true); }, ResetImage).ShowDialog(); }
            finally { menuOpen = false; nextWalk = clock.Elapsed.TotalSeconds + 3; }
        }

        internal static string ModeLabel(ActivityMode value)
        {
            return value == ActivityMode.Focus ? "집중" : value == ActivityMode.Rest ? "휴식" : value == ActivityMode.Meeting ? "회의" : "퇴근";
        }

        static string ModeDescription(ActivityMode value)
        {
            return value == ActivityMode.Focus ? "두 별이 응원해요" : value == ActivityMode.Rest ? "졸림 · 하품" : value == ActivityMode.Meeting ? "갸우뚱 · 의아함" : "커튼 뒤에서 인사한 뒤 딸기 모자로 걸어요";
        }

        static string ModeState(ActivityMode value)
        {
            return value == ActivityMode.Focus ? "focus" : value == ActivityMode.Rest ? "rest" : value == ActivityMode.Meeting ? "meeting" : "idle";
        }

        void SetState(string state)
        {
            if (currentState != state) { currentState = state; stateStarted = clock.Elapsed.TotalMilliseconds; }
            pet.Source = clips[state].At(0);
        }

        void Tick(object sender, EventArgs args)
        {
            if (quit != null && quit.WaitOne(0)) { Close(); return; }
            double now = clock.Elapsed.TotalSeconds, dt = Math.Min(.1, Math.Max(0, now - lastTick)); lastTick = now;
            bool canWalk = roaming && !focus.Active && !pointerDown && !menuOpen && IsVisible && now >= reactionUntil && now >= farewellUntil && now >= notificationUntil;
            if (canWalk && !walking && now >= nextWalk)
            {
                Rect area = WorkArea(); targetX = area.Left + random.NextDouble() * Math.Max(0, area.Width - Width);
                if (Math.Abs(targetX - Left) < 40) targetX = Left + (Left < area.Left + area.Width / 2 ? 100 : -100);
                targetX = Motion.Clamp(targetX, area.Left, area.Right - Width); walking = true;
            }
            if (walking && canWalk)
            {
                double oldX = Left; Left = Motion.Approach(Left, targetX, 48, dt);
                if (Math.Abs(Left - targetX) < .5) { walking = false; nextWalk = now + 5 + random.NextDouble() * 6; }
                else SetState(oldX < Left ? "walk_right" : "walk_left");
            }
            else if (!canWalk) walking = false;
            if (!walking)
            {
                string state = DesiredStillState(now);
                if (currentState != state) SetState(state);
            }
            double age = clock.Elapsed.TotalMilliseconds - stateStarted;
            // Pause the pose along with the timer; resume explicitly restarts the clip.
            if (focus.Active && focus.Paused) age = pausedAnimationAge;
            pet.Source = clips[currentState].At(age);
            bubble.Visibility = focus.Active || pointerDown || menuOpen || now < bubbleUntil ? Visibility.Visible : Visibility.Collapsed;
            UpdateUi();
        }

        void UpdateUi()
        {
            double now = clock.Elapsed.TotalSeconds;
            string key = mode.ToString() + (focus.Active ? focus.Paused ? "paused" : "active" : roaming ? "roam" : "stay") + (focus.Active ? Motion.TimeText(previewElapsed ?? focus.Elapsed(now)) : "") + (now < farewellUntil ? "bye" : "") + (now < notificationUntil ? "notice" : "");
            if (key == lastUiKey) return; lastUiKey = key;
            foreach (var pair in modeButtons)
            {
                pair.Value.Background = ColorBrush(pair.Key == mode ? "#7060F7" : "#F3F1F7");
                pair.Value.Foreground = pair.Key == mode ? Brushes.White : ColorBrush("#81788C");
            }
            if (focus.Active)
            {
                status.Text = focus.Paused ? "잠깐 멈춤" : ModeLabel(mode) + " 중";
                status.FontSize = 15;
                time.Visibility = Visibility.Visible; time.Text = Motion.TimeText(previewElapsed ?? focus.Elapsed(clock.Elapsed.TotalSeconds));
                primary.Content = focus.Paused ? "다시 시작" : "일시정지"; secondary.Content = "그만하기";
                primary.Background = ColorBrush("#F1F1F5"); primary.Foreground = ColorBrush("#37353A");
                secondary.Background = ColorBrush("#7060F7"); secondary.Foreground = Brushes.White;
                dot.Fill = ColorBrush(focus.Paused ? "#D9CDA8" : mode == ActivityMode.Rest ? "#E3CC9F" : mode == ActivityMode.Meeting ? "#B9C9EB" : "#B9DDBE");
            }
            else
            {
                status.Text = now < farewellUntil ? "바이바이! 수고했어요" : roaming ? "오늘도 수고했어요" : "여기서 기다릴게요"; status.FontSize = 15; time.Visibility = Visibility.Collapsed;
                primary.Content = "다시 집중"; secondary.Content = roaming ? "이동 멈춤" : "이동 시작";
                primary.Background = ColorBrush("#7060F7"); primary.Foreground = Brushes.White;
                secondary.Background = ColorBrush("#F1F1F5"); secondary.Foreground = ColorBrush("#56515F");
                dot.Fill = ColorBrush("#E4C875");
            }
            status.MaxWidth = 236; status.TextTrimming = TextTrimming.CharacterEllipsis;
            status.ToolTip = null;
            if (notification != null && now < notificationUntil)
            {
                status.Text = notification.title; status.FontSize = 14; time.Visibility = Visibility.Collapsed;
                status.ToolTip = notification.source + "\n" + notification.message; dot.Fill = ColorBrush("#B9DDBE");
            }
        }

        string DesiredStillState(double now)
        {
            if (focus.Active && focus.Paused) return ModeState(mode);
            if (now < notificationUntil) return "focus";
            if (now < farewellUntil) return "farewell";
            return focus.Active ? ModeState(mode) : now < reactionUntil ? "reaction" : "idle";
        }

        void ReceiveNotification(PetNotification value)
        {
            value.Validate(); notification = value;
            double now = clock.Elapsed.TotalSeconds; notificationUntil = now + 8; bubbleUntil = now + 9;
            walking = false; nextWalk = now + 9; lastUiKey = "";
            if (!focus.Paused) { SetState("focus"); stateStarted = clock.Elapsed.TotalMilliseconds; }
            Show(); ClampToScreen(); UpdateUi();
            if (tray != null) tray.ShowBalloonTip(4500, value.title, value.message, Forms.ToolTipIcon.Info);
        }

        void TestNotification()
        {
            ReceiveNotification(new PetNotification { source = "테스트", title = "작업 완료 알림 미리보기", message = "완료 신호를 받으면 이렇게 알려줘요. 이 알림은 동작 확인용 테스트예요." });
        }

        internal void OpenMailSettings()
        {
            if (mailWindow != null)
            {
                if (mailWindow.WindowState == WindowState.Minimized) mailWindow.WindowState = WindowState.Normal;
                mailWindow.Show(); mailWindow.Activate(); return;
            }
            if (mail == null) mail = new MailService(folder, delegate(PetNotification value) { Dispatcher.BeginInvoke(new Action(delegate { ReceiveNotification(value); })); });
            var opened = new MailWindow(this, mail, folder); mailWindow = opened;
            opened.Closed += delegate { if (Object.ReferenceEquals(mailWindow, opened)) mailWindow = null; };
            opened.Show();
        }

        void SwitchMode(ActivityMode selected)
        {
            double now = clock.Elapsed.TotalSeconds;
            if (mode == selected && focus.Active) { bubbleUntil = double.MaxValue; return; }
            mode = selected; walking = false; reactionUntil = 0; farewellUntil = 0; notificationUntil = 0; pausedAnimationAge = 0;
            if (mode == ActivityMode.Walk)
            {
                focus.Stop(now); roaming = true; bubbleUntil = now + 9;
                farewellUntil = now + Math.Min(4, clips["farewell"].Duration / 1000 * 2); nextWalk = farewellUntil + .35;
            }
            else { focus.Start(now); bubbleUntil = double.MaxValue; }
            SetState(mode == ActivityMode.Walk ? "farewell" : ModeState(mode)); stateStarted = clock.Elapsed.TotalMilliseconds; UpdateUi();
        }

        void StartFocus() { SwitchMode(ActivityMode.Focus); }
        void TogglePause()
        {
            double now = clock.Elapsed.TotalSeconds;
            if (focus.Paused)
            {
                focus.Resume(now); stateStarted = clock.Elapsed.TotalMilliseconds; pausedAnimationAge = 0; pet.Source = clips[currentState].At(0);
            }
            else
            {
                if (currentState != ModeState(mode)) { SetState(ModeState(mode)); stateStarted = clock.Elapsed.TotalMilliseconds; }
                pausedAnimationAge = clock.Elapsed.TotalMilliseconds - stateStarted; focus.Pause(now);
            }
            UpdateUi();
        }
        void StopFocus() { SwitchMode(ActivityMode.Walk); }
        void ToggleRoaming() { roaming = !roaming; walking = false; nextWalk = clock.Elapsed.TotalSeconds + 1; bubbleUntil = clock.Elapsed.TotalSeconds + 6; UpdateUi(); }

        Matrix DeviceToDip()
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            return source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformFromDevice : Matrix.Identity;
        }

        Rect WorkArea()
        {
            var screen = IsLoaded ? Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle) : Forms.Screen.PrimaryScreen;
            var r = screen.WorkingArea; Matrix m = DeviceToDip();
            Point p1 = m.Transform(new Point(r.Left, r.Top)), p2 = m.Transform(new Point(r.Right, r.Bottom));
            return new Rect(p1, p2);
        }

        void ClampToScreen() { Rect a = WorkArea(); Left = Motion.Clamp(Left, a.Left, a.Right - Width); Top = Motion.Clamp(Top, a.Top, a.Bottom - Height); }

        void OnPointerDown(object sender, MouseButtonEventArgs e)
        {
            CursorPoint point; GetCursorPos(out point); Point dip = DeviceToDip().Transform(new Point(point.X, point.Y));
            pointerDown = true; dragging = false; walking = false; dragX = dip.X; dragY = dip.Y; dragLeft = Left; dragTop = Top;
            pet.CaptureMouse(); e.Handled = true;
        }

        void OnPointerMove(object sender, MouseEventArgs e)
        {
            if (!pointerDown) return;
            CursorPoint point; GetCursorPos(out point); Point dip = DeviceToDip().Transform(new Point(point.X, point.Y));
            double dx = dip.X - dragX, dy = dip.Y - dragY;
            if (Math.Abs(dx) + Math.Abs(dy) > 4) dragging = true;
            if (dragging) { Left = dragLeft + dx; Top = dragTop + dy; }
        }

        void OnPointerUp(object sender, MouseButtonEventArgs e)
        {
            if (!pointerDown) return;
            bool wasDrag = dragging; pointerDown = false; dragging = false; pet.ReleaseMouseCapture();
            ClampToScreen(); nextWalk = clock.Elapsed.TotalSeconds + 6;
            if (!wasDrag) { bubbleUntil = clock.Elapsed.TotalSeconds + 9; if (!focus.Active) { reactionUntil = clock.Elapsed.TotalSeconds + clips["reaction"].Duration / 1000; SetState("reaction"); } }
            e.Handled = true;
        }

        void WalkNow(bool right)
        {
            if (focus.Active) StopFocus(); roaming = true;
            farewellUntil = 0; notificationUntil = 0;
            Rect a = WorkArea(); targetX = Motion.Clamp(Left + (right ? 180 : -180), a.Left, a.Right - Width);
            walking = true; reactionUntil = 0; nextWalk = clock.Elapsed.TotalSeconds + 1; bubbleUntil = 0;
        }

        MenuItem Item(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += delegate { action(); }; return item; }

        internal void OpenGuide(bool gallery)
        {
            string bundled = System.IO.Path.Combine(folder, gallery ? "gallery" : "guide", "index.html");
            string source = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, "..", gallery ? "twinkle_twinkle/index.html" : "index.html"));
            string target = File.Exists(bundled) ? bundled : source;
            if (File.Exists(target)) Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            else MessageBox.Show("사용 안내 파일이 없어요. 저장소의 index.html을 확인해 주세요. / Open index.html in the repository.", "Twinkle Agent");
        }

        MenuItem SizeSliderItem()
        {
            var panel = new StackPanel { Width = 204, Margin = new Thickness(2, 4, 2, 5) };
            var label = new TextBlock { Text = "캐릭터 크기  " + size.ToString("0") + "px", FontSize = 12, Foreground = ColorBrush("#6D6675"), Margin = new Thickness(0, 0, 0, 5) };
            var slider = new Slider { Minimum = 80, Maximum = 320, Value = size, SmallChange = 4, LargeChange = 16, TickFrequency = 8, IsSnapToTickEnabled = false, Width = 200, Height = 24, ToolTip = "드래그하면 바로 크기가 바뀌어요" };
            slider.ValueChanged += delegate { ApplySize(slider.Value); label.Text = "캐릭터 크기  " + size.ToString("0") + "px"; };
            panel.Children.Add(label); panel.Children.Add(slider);
            return new MenuItem { Header = panel, StaysOpenOnClick = true };
        }

        void OpenMenu()
        {
            walking = false; var menu = new ContextMenu(); menu.FontFamily = FontFamily; menu.FontSize = 14;
            foreach (ActivityMode activity in new[] { ActivityMode.Focus, ActivityMode.Rest, ActivityMode.Meeting, ActivityMode.Walk })
            {
                ActivityMode selected = activity;
                var option = Item(ModeLabel(activity) + "  ·  " + ModeDescription(activity), delegate { SwitchMode(selected); });
                option.IsCheckable = true; option.IsChecked = mode == activity; menu.Items.Add(option);
            }
            if (focus.Active) menu.Items.Add(Item(focus.Paused ? "타이머 다시 시작" : "타이머 일시정지", TogglePause));
            var images = new MenuItem { Header = "상황별 이미지 바꾸기…" };
            foreach (ActivityMode activity in Enum.GetValues(typeof(ActivityMode)))
            {
                ActivityMode selected = activity; images.Items.Add(Item(ModeLabel(activity) + " 이미지 바꾸기…", delegate { OpenImagePicker(selected); }));
            }
            menu.Items.Add(images);
            menu.Items.Add(Item("사용법 / Guide (KOR · ENG)", delegate { OpenGuide(false); }));
            menu.Items.Add(Item("이미지 갤러리 / Gallery", delegate { OpenGuide(true); }));
            menu.Items.Add(Item("메일 연결…", OpenMailSettings));
            menu.Items.Add(Item("알림 미리보기", TestNotification));
            menu.Items.Add(Item("메일·AI 연동 안내", delegate { Process.Start(new ProcessStartInfo(System.IO.Path.Combine(folder, "알림_연동_안내.txt")) { UseShellExecute = true }); }));
            menu.Items.Add(new Separator());
            var roam = Item("자유롭게 돌아다니기", ToggleRoaming); roam.IsCheckable = true; roam.IsChecked = roaming; menu.Items.Add(roam);
            menu.Items.Add(Item("왼쪽으로 걷기", delegate { WalkNow(false); }));
            menu.Items.Add(Item("오른쪽으로 걷기", delegate { WalkNow(true); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(SizeSliderItem());
            menu.Items.Add(Item("조금 작게  −8px", delegate { ApplySize(size - 8); }));
            menu.Items.Add(Item("조금 크게  +8px", delegate { ApplySize(size + 8); }));
            menu.Items.Add(Item("기본 크기  144px", delegate { ApplySize(144); }));
            menu.Items.Add(new Separator());
            var top = Item("항상 위에 표시", delegate { Topmost = !Topmost; }); top.IsCheckable = true; top.IsChecked = Topmost; menu.Items.Add(top);
            menu.Items.Add(Item("잠깐 숨기기", Hide)); menu.Items.Add(new Separator()); menu.Items.Add(Item("종료", Close));
            menu.Opened += delegate { menuOpen = true; }; menu.Closed += delegate { menuOpen = false; nextWalk = clock.Elapsed.TotalSeconds + 2; };
            menu.PlacementTarget = pet; menu.IsOpen = true;
        }

        void SetupTray()
        {
            tray = new Forms.NotifyIcon(); tray.Icon = new System.Drawing.Icon(System.IO.Path.Combine(folder, "assets", "twinkle.ico")); tray.Text = "트윙클 데스크탑 · 우클릭으로 종료";
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("캐릭터 보이기", null, delegate { Dispatcher.BeginInvoke(new Action(delegate { Show(); bubbleUntil = clock.Elapsed.TotalSeconds + 9; ClampToScreen(); })); });
            foreach (ActivityMode activity in new[] { ActivityMode.Focus, ActivityMode.Rest, ActivityMode.Meeting, ActivityMode.Walk })
            {
                ActivityMode selected = activity;
                menu.Items.Add(ModeLabel(activity) + " · " + ModeDescription(activity), null, delegate { Dispatcher.BeginInvoke(new Action(delegate { Show(); SwitchMode(selected); })); });
            }
            menu.Items.Add("자유 이동 켜기 / 끄기", null, delegate { Dispatcher.BeginInvoke(new Action(ToggleRoaming)); });
            menu.Items.Add("상황별 이미지 바꾸기…", null, delegate { Dispatcher.BeginInvoke(new Action(delegate { Show(); OpenImagePicker(mode); })); });
            menu.Items.Add("메일 연결…", null, delegate { Dispatcher.BeginInvoke(new Action(OpenMailSettings)); });
            menu.Items.Add("사용법 / Guide (KOR · ENG)", null, delegate { Dispatcher.BeginInvoke(new Action(delegate { OpenGuide(false); })); });
            menu.Items.Add("이미지 갤러리 / Gallery", null, delegate { Dispatcher.BeginInvoke(new Action(delegate { OpenGuide(true); })); });
            menu.Items.Add("알림 미리보기", null, delegate { Dispatcher.BeginInvoke(new Action(TestNotification)); });
            menu.Items.Add("메일·AI 연동 안내", null, delegate { Process.Start(new ProcessStartInfo(System.IO.Path.Combine(folder, "알림_연동_안내.txt")) { UseShellExecute = true }); });
            menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("종료", null, delegate { Dispatcher.BeginInvoke(new Action(Close)); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { Dispatcher.BeginInvoke(new Action(delegate { Show(); bubbleUntil = clock.Elapsed.TotalSeconds + 9; })); };
            tray.Visible = true;
        }

        public void Preview(string mode, string path)
        {
            previewMode = true; bubble.Visibility = Visibility.Visible;
            if (mode == "welcome")
            {
                var welcome = new WelcomeWindow(this, folder); welcome.ShowActivated = false; welcome.Topmost = false; welcome.Opacity = 0; welcome.WindowStartupLocation = WindowStartupLocation.Manual; welcome.Left = -10000; welcome.Top = -10000; welcome.Show();
                welcome.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { })); var content = (FrameworkElement)welcome.Content; content.UpdateLayout();
                var capture = new RenderTargetBitmap((int)welcome.Width * 2, (int)welcome.Height * 2, 192, 192, PixelFormats.Pbgra32); capture.Render(content); ImageLibrary.SavePng(capture, path); welcome.Close(); return;
            }
            if (mode == "mail")
            {
                using (var previewMail = new MailService(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "twinkle-mail-preview-" + Guid.NewGuid().ToString("N")), delegate { }))
                {
                    var settings = new MailWindow(this, previewMail, folder); settings.ShowActivated = false; settings.Topmost = false; settings.Opacity = 0; settings.WindowStartupLocation = WindowStartupLocation.Manual; settings.Left = -10000; settings.Top = -10000; settings.Show();
                    settings.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { })); settings.ValidateUi(); var content = (FrameworkElement)settings.Content; content.UpdateLayout();
                    var capture = new RenderTargetBitmap((int)settings.Width * 2, (int)settings.Height * 2, 192, 192, PixelFormats.Pbgra32); capture.Render(content); ImageLibrary.SavePng(capture, path); settings.Close(); return;
                }
            }
            if (mode == "picker")
            {
                var picker = new ImagePicker(this, library, ActivityMode.Rest, CurrentImage, delegate(ActivityMode m, LibraryItem item) { AssignImage(m, item, true); }, ResetImage);
                picker.ShowActivated = false; picker.Topmost = false; picker.Opacity = 0; picker.WindowStartupLocation = WindowStartupLocation.Manual; picker.Left = -10000; picker.Top = -10000; picker.Show();
                picker.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { }));
                picker.ValidateCards(); var content = (FrameworkElement)picker.Content; content.UpdateLayout();
                var capture = new RenderTargetBitmap(1290, 975, 144, 144, PixelFormats.Pbgra32); capture.Render(content); ImageLibrary.SavePng(capture, path); picker.Close(); return;
            }
            if (mode == "focus" || mode == "rest" || mode == "meeting")
            {
                SwitchMode(mode == "focus" ? ActivityMode.Focus : mode == "rest" ? ActivityMode.Rest : ActivityMode.Meeting);
                previewElapsed = mode == "focus" ? 1838 : mode == "rest" ? 258 : 725;
            }
            else if (mode == "farewell") { SwitchMode(ActivityMode.Walk); pet.Source = clips["farewell"].At(clips["farewell"].Duration * .5); }
            else if (mode == "notification") { TestNotification(); }
            else { SetState(mode == "left" ? "walk_left" : "walk_right"); }
            UpdateUi();
            Scene.Measure(new Size(Width, Height)); Scene.Arrange(new Rect(0, 0, Width, Height)); Scene.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)(Width * 2), (int)(Height * 2), 192, 192, PixelFormats.Pbgra32); bitmap.Render(Scene);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }

        public void ExerciseControls()
        {
            primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!focus.Active || focus.Paused || currentState != "focus" || mode != ActivityMode.Focus) throw new Exception("Start button failed");
            primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!focus.Paused) throw new Exception("Pause button failed");
            primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (focus.Paused) throw new Exception("Resume button failed");
            modeButtons[ActivityMode.Rest].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (mode != ActivityMode.Rest || currentState != "rest" || status.Text != "휴식 중") throw new Exception("Rest mode button failed");
            modeButtons[ActivityMode.Meeting].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (mode != ActivityMode.Meeting || currentState != "meeting" || status.Text != "회의 중") throw new Exception("Meeting mode button failed");
            modeButtons[ActivityMode.Focus].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (mode != ActivityMode.Focus || currentState != "focus") throw new Exception("Focus mode button failed");
            secondary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (focus.Active || mode != ActivityMode.Walk) throw new Exception("Stop button failed");
            secondary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (roaming) throw new Exception("Roaming toggle failed");
            modeButtons[ActivityMode.Walk].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!roaming || focus.Active) throw new Exception("Walk mode button failed");
            var slider = ((StackPanel)SizeSliderItem().Header).Children.OfType<Slider>().Single();
            slider.Value = 118;
            if (size != 118 || pet.Width != 118) throw new Exception("Live resize slider failed");
            slider.Value = 0;
            if (size != 80) throw new Exception("Minimum size failed");
            slider.Value = 1000;
            if (size != 320) throw new Exception("Maximum size failed");
            ApplySize(144);
            var picker = new ImagePicker(this, library, ActivityMode.Rest, CurrentImage, delegate(ActivityMode m, LibraryItem item) { AssignImage(m, item, true); }, ResetImage);
            picker.ValidateCards(); picker.Close();
            AssignImage(ActivityMode.Rest, library.Find("we_are_14"), true);
            if (preferences.ImageChoices["Rest"] != "we_are_14" || clips["rest"].Frames.Length != 31) throw new Exception("Image assignment failed");
            ResetImage(ActivityMode.Rest);
            if (preferences.ImageChoices.ContainsKey("Rest") || !Object.ReferenceEquals(clips["rest"], defaultClips["rest"])) throw new Exception("Image reset failed");
            AssignImage(ActivityMode.Walk, library.Find("base_15"), true);
            if (clips["walk_left"].Frames.Length != 12 || clips["walk_right"].Frames.Length != 12 || !Object.ReferenceEquals(clips["idle"], clips["walk_right"])) throw new Exception("Walk image assignment failed");
            ResetImage(ActivityMode.Walk);
        }

        public void RunSmoke(string report)
        {
            Loaded += delegate
            {
                var check = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                var elapsed = Stopwatch.StartNew(); int stage = 0;
                double initialLeft = Left, leftPosition = 0, pausedSeconds = 0;
                check.Tick += delegate
                {
                    try
                    {
                        double t = elapsed.Elapsed.TotalSeconds;
                        if (stage == 0 && t >= .3) { if (tray == null || !tray.Visible) throw new Exception("Tray not visible"); WalkNow(false); stage++; }
                        else if (stage == 1 && t >= 1.5) { if (Left >= initialLeft - 2) throw new Exception("Left movement failed"); leftPosition = Left; WalkNow(true); stage++; }
                        else if (stage == 2 && t >= 2.7) { if (Left <= leftPosition + 2) throw new Exception("Right movement failed"); StartFocus(); stage++; }
                        else if (stage == 3 && t >= 3.7)
                        {
                            if (focus.Elapsed(clock.Elapsed.TotalSeconds) < .7) throw new Exception("Native timer did not advance");
                            modeButtons[ActivityMode.Focus].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            if (focus.Elapsed(clock.Elapsed.TotalSeconds) < .7) throw new Exception("Selecting same mode reset timer");
                            modeButtons[ActivityMode.Rest].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            if (currentState != "rest" || focus.Elapsed(clock.Elapsed.TotalSeconds) > .2) throw new Exception("Rest state/reset failed"); stage++;
                        }
                        else if (stage == 4 && t >= 4.7)
                        {
                            if (focus.Elapsed(clock.Elapsed.TotalSeconds) < .7) throw new Exception("Rest timer failed");
                            modeButtons[ActivityMode.Meeting].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            if (currentState != "meeting") throw new Exception("Meeting state failed"); stage++;
                        }
                        else if (stage == 5 && t >= 5.7) { if (focus.Elapsed(clock.Elapsed.TotalSeconds) < .7) throw new Exception("Meeting timer failed"); TogglePause(); pausedSeconds = focus.Elapsed(clock.Elapsed.TotalSeconds); stage++; }
                        else if (stage == 6 && t >= 6.7)
                        {
                            if (Math.Abs(focus.Elapsed(clock.Elapsed.TotalSeconds) - pausedSeconds) > .01) throw new Exception("Paused timer advanced"); TogglePause();
                            double bottom = Top + Height;
                            var slider = ((StackPanel)SizeSliderItem().Header).Children.OfType<Slider>().Single(); slider.Value = 118;
                            if (pet.Width != 118 || Math.Abs(Top + Height - bottom) > 1) throw new Exception("Native live resize/anchor failed");
                            ApplySize(144); stage++;
                        }
                        else if (stage == 7 && t >= 7.7)
                        {
                            if (focus.Elapsed(clock.Elapsed.TotalSeconds) < pausedSeconds + .7) throw new Exception("Native resume failed");
                            StopFocus(); Hide(); Show(); check.Stop();
                            File.WriteAllText(report, "PASS v0.2: real WPF window and tray; left/right movement; focus/rest/meeting buttons and animation states; same mode preserves timer; mode change resets timer; live elapsed/pause/resume/stop; slider resize updates image immediately and preserves bottom anchor; hide/show.\r\n");
                            using (var handle = EventWaitHandle.OpenExisting(QuitName)) handle.Set();
                        }
                    }
                    catch (Exception error) { check.Stop(); File.WriteAllText(report, "FAIL: " + error); Application.Current.Shutdown(1); }
                };
                Closed += delegate { check.Stop(); }; check.Start();
            };
        }

        public void RunMailWindowRegression(string report)
        {
            Loaded += delegate
            {
                mail = new MailService(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "twinkle-window-test-" + Guid.NewGuid().ToString("N")), delegate { });
                var check = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
                double began = clock.Elapsed.TotalSeconds, initialLeft = Left; int stage = 0;
                var frames = new HashSet<ImageSource>(); var lines = new List<string>(); MailWindow opened = null;
                nextWalk = began + .15; reactionUntil = farewellUntil = notificationUntil = 0;
                check.Tick += delegate
                {
                    try
                    {
                        double now = clock.Elapsed.TotalSeconds;
                        if (stage == 0)
                        {
                            opened = Application.Current.Windows.OfType<MailWindow>().SingleOrDefault();
                            if (opened != null)
                            {
                                if (!IsEnabled) throw new Exception("Mail settings disabled the pet window");
                                if (now - began > 1)
                                {
                                    if (Math.Abs(Left - initialLeft) < 5 || !walking || frames.Count < 3) throw new Exception("Natural roaming/foot animation stopped while mail settings were open");
                                    OpenMailSettings();
                                    if (Application.Current.Windows.OfType<MailWindow>().Count() != 1 || !Object.ReferenceEquals(opened, Application.Current.Windows.OfType<MailWindow>().Single())) throw new Exception("Repeated open duplicated mail settings");
                                    lines.Add("PASS: mail settings stay open while natural roaming and foot animation continue; pet stays interactive; repeated open reuses one window.");
                                    modeButtons[ActivityMode.Rest].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); began = now; frames.Clear(); stage = 1;
                                }
                                else frames.Add(pet.Source);
                            }
                            else if (now - began > 3) throw new Exception("Mail settings did not open");
                        }
                        else if (stage == 1)
                        {
                            frames.Add(pet.Source);
                            if (now - began > .8)
                            {
                                if (frames.Count < 2 || focus.Elapsed(now) < .6) throw new Exception("Rest animation/timer stopped with mail settings open");
                                opened.Close(); OpenMailSettings();
                                var reopened = Application.Current.Windows.OfType<MailWindow>().Single();
                                if (Object.ReferenceEquals(opened, reopened)) throw new Exception("Closed mail settings were reused");
                                reopened.Close(); StopFocus(); reactionUntil = farewellUntil = notificationUntil = 0; nextWalk = now + .1; initialLeft = Left; began = now; stage = 2;
                            }
                        }
                        else if (now - began > 1)
                        {
                            if (!IsEnabled || menuOpen || Math.Abs(Left - initialLeft) < 5) throw new Exception("Closing mail settings left roaming blocked");
                            lines.Add("PASS: rest animation and elapsed time continue with mail settings open; close/reopen and return to roaming work.");
                            check.Stop(); File.WriteAllLines(report, lines); using (var handle = EventWaitHandle.OpenExisting(QuitName)) handle.Set();
                        }
                        if (now > 12) throw new Exception("Mail window regression timed out");
                    }
                    catch (Exception error) { check.Stop(); File.WriteAllText(report, "FAIL: " + error.Message); Application.Current.Shutdown(1); }
                };
                Closed += delegate { check.Stop(); }; check.Start();
                Dispatcher.BeginInvoke(new Action(OpenMailSettings));
            };
        }

        public void RunAnimationRegression(string report)
        {
            Loaded += delegate
            {
                foreach (ActivityMode activity in new[] { ActivityMode.Rest, ActivityMode.Focus, ActivityMode.Meeting }) ResetImage(activity);
                var activities = new[] { ActivityMode.Rest, ActivityMode.Focus, ActivityMode.Meeting };
                int index = 0, stage = 0; double began = clock.Elapsed.TotalSeconds, heldElapsed = 0;
                ImageSource held = null; var seen = new HashSet<ImageSource>(); var lines = new List<string>();
                modeButtons[activities[0]].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var check = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                check.Tick += delegate
                {
                    try
                    {
                        double elapsed = clock.Elapsed.TotalSeconds - began;
                        if (stage == 0)
                        {
                            seen.Add(pet.Source);
                            if (elapsed > 3.2)
                            {
                                if (seen.Count < 2) throw new Exception("Running animation did not change: " + mode);
                                primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); heldElapsed = focus.Elapsed(clock.Elapsed.TotalSeconds); began = clock.Elapsed.TotalSeconds; stage = 1;
                            }
                        }
                        else if (stage == 1)
                        {
                            if (elapsed > .2 && held == null) held = pet.Source;
                            if (elapsed > .9)
                            {
                                if (!Object.ReferenceEquals(held, pet.Source) || Math.Abs(focus.Elapsed(clock.Elapsed.TotalSeconds) - heldElapsed) > .02) throw new Exception("Paused timer/pose moved: " + mode);
                                primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); seen.Clear(); began = clock.Elapsed.TotalSeconds; stage = 2;
                            }
                        }
                        else
                        {
                            seen.Add(pet.Source);
                            if (elapsed > 1)
                            {
                                if (seen.Count < 2 || focus.Elapsed(clock.Elapsed.TotalSeconds) < heldElapsed + .7) throw new Exception("Resume did not immediately animate: " + mode);
                                lines.Add("PASS " + mode + ": pause holds time and pose; resume shows " + seen.Count + " frame changes within 1 second.");
                                index++;
                                if (index == activities.Length)
                                {
                                    check.Stop(); File.WriteAllLines(report, lines); using (var handle = EventWaitHandle.OpenExisting(QuitName)) handle.Set(); return;
                                }
                                modeButtons[activities[index]].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); stage = 0; seen.Clear(); held = null; began = clock.Elapsed.TotalSeconds;
                            }
                        }
                    }
                    catch (Exception error) { check.Stop(); File.WriteAllText(report, "FAIL: " + error); Application.Current.Shutdown(1); }
                };
                Closed += delegate { check.Stop(); }; check.Start();
            };
        }

        public void RunOffworkRegression(string report)
        {
            Loaded += delegate
            {
                ResetImage(ActivityMode.Walk); ResetImage(ActivityMode.Rest);
                var lines = new List<string>(); var frames = new HashSet<ImageSource>();
                double began = clock.Elapsed.TotalSeconds, initialLeft = Left, heldElapsed = 0; ImageSource held = null;
                int stage = 0; Exception sendError = null;
                SwitchMode(ActivityMode.Walk);
                var check = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
                check.Tick += delegate
                {
                    try
                    {
                        double now = clock.Elapsed.TotalSeconds;
                        if (sendError != null) throw sendError;
                        if (stage == 0)
                        {
                            if (now < farewellUntil)
                            {
                                if (currentState != "farewell" || Math.Abs(Left - initialLeft) > .1) throw new Exception("Farewell was interrupted or moved");
                                frames.Add(pet.Source);
                            }
                            else if (now > nextWalk + .7)
                            {
                                if (frames.Count < 3 || Math.Abs(Left - initialLeft) < 2 || !currentState.StartsWith("walk_")) throw new Exception("Farewell did not finish and resume walking");
                                lines.Add("PASS: original 8-frame curtain farewell plays without moving, then strawberry walking resumes automatically.");
                                SwitchMode(ActivityMode.Rest); TogglePause(); heldElapsed = focus.Elapsed(now); began = now; stage = 1;
                                ThreadPool.QueueUserWorkItem(delegate { try { NotificationBridge.Send(new PetNotification { source = "Regression", title = "완료 알림 검사", message = "Local named-pipe signal" }); } catch (Exception error) { sendError = error; } });
                            }
                        }
                        else if (stage == 1)
                        {
                            if (now - began > 4) throw new Exception("Local notification was not received");
                            if (notification != null && notification.title == "완료 알림 검사")
                            {
                                if (!focus.Paused || currentState != "rest" || status.Text != "완료 알림 검사") throw new Exception("Notification changed paused activity");
                                held = pet.Source; began = now; stage = 2;
                            }
                        }
                        else if (stage == 2 && now - began > .4)
                        {
                            if (!Object.ReferenceEquals(held, pet.Source) || Math.Abs(focus.Elapsed(now) - heldElapsed) > .02) throw new Exception("Notification unfroze paused pose/timer");
                            lines.Add("PASS: real local pipe delivers notification; paused rest pose and timer remain paused.");
                            notificationUntil = now; TogglePause(); frames.Clear(); began = now; stage = 3;
                        }
                        else if (stage == 3)
                        {
                            frames.Add(pet.Source);
                            if (now - began > 1)
                            {
                                if (currentState != "rest" || status.Text != "휴식 중" || frames.Count < 2 || focus.Elapsed(now) < heldElapsed + .7) throw new Exception("Rest did not resume after notification");
                                SwitchMode(ActivityMode.Walk); SwitchMode(ActivityMode.Meeting);
                                if (currentState != "meeting" || farewellUntil != 0) throw new Exception("Mode button did not interrupt farewell");
                                lines.Add("PASS: notification expiry restores activity; paused rest resumes immediately; another mode cancels farewell.");
                                check.Stop(); File.WriteAllLines(report, lines); using (var handle = EventWaitHandle.OpenExisting(QuitName)) handle.Set();
                            }
                        }
                        if (clock.Elapsed.TotalSeconds > 15) throw new Exception("Offwork regression timed out");
                    }
                    catch (Exception error) { check.Stop(); File.WriteAllText(report, "FAIL: " + error); Application.Current.Shutdown(1); }
                };
                Closed += delegate { check.Stop(); }; check.Start();
            };
        }
    }

    public static class Program
    {
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Test(string directory, string output)
        {
            var f = new FocusSession(); f.Start(10); Require(f.Elapsed(15) == 5, "elapsed"); f.Pause(20); f.Pause(40); Require(f.Elapsed(100) == 10, "pause"); f.Resume(100); Require(f.Elapsed(110) == 20, "resume"); Require(f.Stop(115) == 25 && !f.Active && f.Elapsed(1000) == 0, "stop reset");
            Require(Clip.FrameAt(29, new[] { 30.0, 50.0, 80.0 }) == 0 && Clip.FrameAt(30, new[] { 30.0, 50.0, 80.0 }) == 1 && Clip.FrameAt(80, new[] { 30.0, 50.0, 80.0 }) == 2 && Clip.FrameAt(160, new[] { 30.0, 50.0, 80.0 }) == 0, "frame boundaries");
            Require(Motion.Approach(5, 10, 48, 1) == 10 && Motion.Approach(10, 0, 48, 1) == 0, "movement overshoot");
            Require(Motion.Clamp(-10, 0, 200) == 0 && Motion.Clamp(210, 0, 200) == 200, "screen boundaries");
            Require(Motion.TimeText(1838) == "30:38" && Motion.TimeText(3661) == "01:01:01", "time format");
            string testSettings = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(output)), "settings-test-" + Guid.NewGuid().ToString("N") + ".json");
            Require(SizeSettings.Load(testSettings).CharacterSize == 144, "new default size");
            SizeSettings.Save(testSettings, 118); Require(SizeSettings.Load(testSettings).CharacterSize == 118, "size persistence");
            SizeSettings.Save(testSettings, 140); Require(SizeSettings.Load(testSettings).CharacterSize == 140, "atomic settings update");
            var choiceSettings = new PetSettings { CharacterSize = 140 }; choiceSettings.ImageChoices["Rest"] = "base_01"; SizeSettings.Save(testSettings, choiceSettings);
            Require(SizeSettings.Load(testSettings).ImageChoices["Rest"] == "base_01", "image choice persistence");
            File.WriteAllText(testSettings, "broken json"); Require(SizeSettings.Load(testSettings).CharacterSize == 144, "invalid settings recovery"); File.Delete(testSettings);
            Require(SizeSettings.Normalize(-1) == 80 && SizeSettings.Normalize(10000) == 320 && SizeSettings.Normalize(double.NaN) == 144, "safe size range");
            string importRoot = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(output)), "import-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(importRoot);
            var importLibrary = new ImageLibrary(importRoot);
            var staticItem = importLibrary.Import(System.IO.Path.Combine(directory, "assets", "master_walk.png"));
            var animatedItem = importLibrary.Import(System.IO.Path.Combine(directory, "assets", "rest.png"));
            Require(staticItem.files.Count == 1 && animatedItem.files.Count > 1, "static/animated file import");
            var reloaded = new ImageLibrary(importRoot); Require(reloaded.Items.Count == 2 && reloaded.Load(reloaded.Find(animatedItem.Id)).Frames.Length == animatedItem.files.Count, "import persistence");
            var app = new Application(); var window = new PetWindow(directory, true); window.ExerciseControls();
            string result = "PASS v0.3: elapsed/pause/resume/stop; animation frame boundaries; movement/screen endpoints; real mode buttons and resize controls; settings and image-choice persistence; all 47 picker cards visible; per-mode assignment/reset and mirrored walk assignment; static PNG and APNG attachment/import persistence.\r\n";
            File.WriteAllText(output, result); window.Close(); app.Shutdown();
        }

        [STAThread]
        public static int Main(string[] args)
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                if (args.Length > 0 && args[0] == "--register-browser") { DesktopInstall.Browser(directory, false); return 0; }
                if (args.Length > 0 && args[0] == "--unregister-browser") { DesktopInstall.Browser(directory, true); return 0; }
                if (args.Length > 0 && args[0] == "--quit") { try { using (var handle = EventWaitHandle.OpenExisting("Local\\TwinkleDesktopQuit_20261022")) handle.Set(); } catch (WaitHandleCannotBeOpenedException) { } return 0; }
                if (args.Length > 1 && args[0] == "--notify-json") { NotificationBridge.Send(PetNotification.FromFile(args[1])); return 0; }
                if (args.Length > 1 && args[0] == "--notify") { NotificationBridge.Send(new PetNotification { source = args.Length > 2 ? args[2] : "외부 작업", title = args[1], message = args.Length > 3 ? args[3] : args[1] }); return 0; }
                if (args.Length > 1 && args[0] == "--self-test") { Test(directory, args[1]); return 0; }
                if (args.Length > 1 && args[0] == "--mail-test") { MailRegression.Run(args[1]).GetAwaiter().GetResult(); return 0; }
                if (args.Length > 2 && args[0] == "--verify-import") { RasterDecoder.VerifyLibrary(args[1], args[2]); return 0; }
                if (args.Length > 2 && args[0] == "--render") { var app = new Application(); var window = new PetWindow(directory, true); window.Preview(args[1], args[2]); window.Close(); app.Shutdown(); return 0; }
                bool created;
                using (var mutex = new Mutex(true, "Local\\TwinkleDesktopPortable_20261022", out created))
                {
                    if (!created) return 0;
                    var app = new Application(); app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    var window = new PetWindow(directory, false, !(args.Length > 0 && (args[0] == "--smoke-test" || args[0] == "--animation-test" || args[0] == "--offwork-test" || args[0] == "--mail-window-test")));
                    if (args.Length > 0 && args[0] == "--mail-settings") window.Loaded += delegate { window.Dispatcher.BeginInvoke(new Action(window.OpenMailSettings)); };
                    if (args.Length > 0 && args[0] == "--welcome") window.Loaded += delegate { window.Dispatcher.BeginInvoke(new Action(delegate { new WelcomeWindow(window, directory).ShowDialog(); })); };
                    if (args.Length > 1 && args[0] == "--smoke-test") window.RunSmoke(args[1]);
                    if (args.Length > 1 && args[0] == "--mail-window-test") window.RunMailWindowRegression(args[1]);
                    if (args.Length > 1 && args[0] == "--animation-test") window.RunAnimationRegression(args[1]);
                    if (args.Length > 1 && args[0] == "--offwork-test") window.RunOffworkRegression(args[1]);
                    int result = app.Run(window); mutex.ReleaseMutex(); return result;
                }
            }
            catch (Exception error)
            {
                File.WriteAllText(System.IO.Path.Combine(directory, "error.log"), error.ToString());
                if (args.Length == 0) MessageBox.Show("실행 중 문제가 생겼어요. 폴더의 error.log를 확인해 주세요.\n" + error.Message, "트윙클 데스크탑");
                return 1;
            }
        }
    }
}
