using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace TwinkleDesktop
{
    public static class DesktopInstall
    {
        static readonly string[] Keys = {
            @"Software\Google\Chrome\NativeMessagingHosts\com.twinkle.desktop",
            @"Software\Microsoft\Edge\NativeMessagingHosts\com.twinkle.desktop"
        };
        public static void Browser(string folder, bool remove)
        {
            string manifest = Path.Combine(folder, "native_host.json");
            if (!remove)
            {
                string id = File.ReadAllText(Path.Combine(folder, "browser_extension_id.txt")).Trim();
                if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-p]{32}$")) throw new InvalidDataException("확장 프로그램 ID를 확인해 주세요.");
                var value = new { name = "com.twinkle.desktop", description = "Twinkle local completion notifications", path = Path.Combine(folder, "TwinkleNativeHost.exe"), type = "stdio", allowed_origins = new[] { "chrome-extension://" + id + "/" } };
                File.WriteAllText(manifest, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false));
            }
            foreach (string path in Keys)
            {
                if (remove)
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(path))
                        if (key == null || !String.Equals(key.GetValue("") as string, manifest, StringComparison.OrdinalIgnoreCase)) continue;
                    Registry.CurrentUser.DeleteSubKey(path, false);
                }
                else using (var key = Registry.CurrentUser.CreateSubKey(path)) key.SetValue("", manifest, RegistryValueKind.String);
            }
            if (remove && File.Exists(manifest)) File.Delete(manifest);
        }
    }

    public sealed class WelcomeWindow : Window
    {
        readonly string folder;
        public WelcomeWindow(PetWindow owner, string folder)
        {
            this.folder = folder; if (owner.IsLoaded) Owner = owner; Title = "처음 만나는 트윙클";
            Width = 560; Height = 590; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
            FontFamily = new FontFamily("Malgun Gothic"); Background = PetWindow.ColorBrush("#FAF8F3");
            var panel = new StackPanel { Margin = new Thickness(30), Background = Background }; Content = panel;
            panel.Children.Add(new TextBlock { Text = "안녕, 오늘도 같이 있을게", FontSize = 27, FontWeight = FontWeights.SemiBold, Foreground = PetWindow.ColorBrush("#4C4359"), Margin = new Thickness(0, 0, 0, 18) });
            Note(panel, "캐릭터를 누르면 집중 · 휴식 · 회의 · 퇴근 버튼이 보여요.\n드래그해서 원하는 자리에 두세요.");
            Note(panel, "크기와 이미지 바꾸기", true);
            Note(panel, "캐릭터 우클릭 → 크기 조절 / 이미지 선택\nCtrl + 마우스 휠로도 바로 줄이거나 키울 수 있어요.\n내 GIF·PNG 이미지를 추가할 수도 있어요.");
            Note(panel, "새 메일 알림", true);
            Note(panel, "우클릭 → 메일 연결에서 원하는 계정을 연결해 주세요.\nGmail · 네이버 · Outlook을 여러 개 연결할 수 있어요.");
            var mail = Action("메일 연결하기"); mail.Click += delegate { Close(); owner.OpenMailSettings(); }; panel.Children.Add(mail);
            Note(panel, "AI 작업 완료 알림", true);
            Note(panel, "ChatGPT · Claude · Gemini 웹 알림은 브라우저 확장 연결이\n추가로 필요해요. 연결 안내를 보고 설정할 수 있어요.");
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) }; panel.Children.Add(row);
            var guide = Action("Guide · KOR / ENG"); guide.Click += delegate { owner.OpenGuide(false); }; row.Children.Add(guide);
            var done = Action("트윙클 시작하기"); done.Click += delegate { Close(); }; row.Children.Add(done);
        }
        static void Note(StackPanel panel, string text, bool title = false)
        {
            panel.Children.Add(new TextBlock { Text = text, FontSize = title ? 15 : 13, FontWeight = title ? FontWeights.SemiBold : FontWeights.Normal, Foreground = PetWindow.ColorBrush(title ? "#4C4359" : "#706779"), TextWrapping = TextWrapping.Wrap, LineHeight = 22, Margin = new Thickness(0, title ? 15 : 2, 0, title ? 3 : 5) });
        }
        static Button Action(string label)
        {
            var button = PetWindow.RoundButton(); button.Content = label; button.FontSize = 14; button.MinWidth = label.Length * 14 + 32; button.Margin = new Thickness(0, 5, 10, 0); button.Background = PetWindow.ColorBrush("#7060F7"); button.Foreground = Brushes.White; button.HorizontalAlignment = HorizontalAlignment.Left; return button;
        }
    }
}
