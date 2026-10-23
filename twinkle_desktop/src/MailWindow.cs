using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TwinkleDesktop
{
    public sealed class MailWindow : Window
    {
        readonly MailService service; readonly string folder;
        readonly OAuthAppConfig appConfig;
        readonly StackPanel accounts = new StackPanel();
        readonly TextBlock status = new TextBlock();
        readonly TextBox loginInfo = new TextBox { IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        readonly TextBox googleFile = Input(), microsoftId = Input(), naverAddress = Input(), imapAddress = Input(), imapHost = Input(), imapPort = Input();
        readonly PasswordBox naverPassword = new PasswordBox(), imapPassword = new PasswordBox();
        readonly CheckBox startTls = new CheckBox { Content = "STARTTLS (보통 143 포트)", Margin = new Thickness(0, 8, 0, 8) }, details = new CheckBox { Content = "알림에 제목·발신자 표시", Margin = new Thickness(0, 8, 0, 8) };
        readonly TabControl tabs = new TabControl();
        readonly Button connect = Button("선택한 메일 연결"), cancel = Button("로그인 취소"), check = Button("지금 확인");
        readonly Button googleLogin = Button("Google로 로그인"), microsoftLogin = Button("Microsoft로 로그인");
        readonly TextBlock googleReady = new TextBlock(), microsoftReady = new TextBlock();
        readonly Expander googleDeveloper = new Expander(), microsoftDeveloper = new Expander();
        readonly CancellationTokenSource lifetime = new CancellationTokenSource(); CancellationTokenSource connecting;
        bool refreshing, busy;
        public MailWindow(Window owner, MailService service, string folder)
        {
            this.service = service; this.folder = folder; appConfig = new OAuthAppConfig(folder);
            if (owner.IsLoaded) Owner = owner;
            Title = "메일 연결 · 트윙클"; Width = 860; Height = 730; MinWidth = 800; MinHeight = 650; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true; Topmost = owner.Topmost; FontFamily = owner.FontFamily; Background = PetWindow.ColorBrush("#FAF8F3");
            var root = new DockPanel { Margin = new Thickness(24), Background = Background }; Content = root;
            var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
            top.Children.Add(new TextBlock { Text = "새 메일이 오면, 내가 알려줄게", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = PetWindow.ColorBrush("#4C4359") });
            top.Children.Add(new TextBlock { Text = "Gmail · 네이버 · Outlook / 회사 메일을 여러 개 연결할 수 있어요.", FontSize = 13, Margin = new Thickness(0, 8, 0, 16), Foreground = Brushes.DimGray });
            var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
            loginInfo.TextWrapping = TextWrapping.Wrap; loginInfo.Foreground = PetWindow.ColorBrush("#7060F7"); loginInfo.Margin = new Thickness(0, 8, 0, 4); loginInfo.FontSize = 14; bottom.Children.Add(loginInfo);
            status.TextWrapping = TextWrapping.Wrap; status.FontSize = 12; status.Foreground = Brushes.DimGray; status.Margin = new Thickness(0, 8, 0, 8); bottom.Children.Add(status);
            var actions = new StackPanel { Orientation = Orientation.Horizontal }; bottom.Children.Add(actions); actions.Children.Add(connect); actions.Children.Add(cancel); actions.Children.Add(check);
            var help = Button("처음 연결하는 방법"); actions.Children.Add(help); help.Click += delegate { Process.Start(new ProcessStartInfo(Path.Combine(folder, "메일_연결_안내.txt")) { UseShellExecute = true }); };
            cancel.IsEnabled = false; cancel.Click += delegate { if (connecting != null) connecting.Cancel(); };
            connect.Click += async delegate { await ConnectSelected(); };
            check.Click += async delegate
            {
                if (busy) return; SetBusy(true); status.Text = "연결한 계정의 새 메일을 확인하고 있어요…";
                try { await service.CheckNow(lifetime.Token); status.Text = "확인을 마쳤어요. 계정별 상태를 확인해 주세요."; }
                catch (Exception error) { status.Text = MailUtil.Error(error); }
                finally { SetBusy(false); Refresh(); }
            };
            var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) }); root.Children.Add(body);
            tabs.Margin = new Thickness(0, 0, 18, 0); body.Children.Add(tabs);
            var gmail = Page("Gmail");
            Explain(gmail, "Gmail 알림 연결", "Google 계정을 선택하고 새 메일 알림을 허용해 주세요. 로그인은 Google의 공식 화면에서 진행해요.");
            googleLogin.Margin = new Thickness(0, 12, 0, 12); gmail.Children.Add(googleLogin); googleLogin.Click += async delegate { await ConnectSelected(); };
            ReadyNote(gmail, googleReady);
            Note(gmail, "1. Google로 로그인\n2. 사용할 계정 선택\n3. 메일 알림 권한 허용\n\n연결한 뒤에는 자동으로 새 메일을 확인해요. 본문·첨부파일은 가져오지 않아요.");
            var googleSetup = Developer(gmail, googleDeveloper);
            Note(googleSetup, "앱 제작자가 배포 전에 한 번 등록합니다. 일반 사용자는 JSON을 준비할 필요가 없어요.");
            Label(googleSetup, "앱의 Google 데스크톱 OAuth JSON"); googleSetup.Children.Add(googleFile); googleFile.IsReadOnly = true;
            var pick = Button("앱 로그인 설정 등록…"); pick.Margin = new Thickness(0, 8, 0, 12); googleSetup.Children.Add(pick);
            pick.Click += delegate
            {
                var file = new Microsoft.Win32.OpenFileDialog { Title = "앱 제작자용 Google 데스크톱 OAuth 설정", Filter = "OAuth JSON|*.json", CheckFileExists = true };
                if (file.ShowDialog(this) == true) try { appConfig.ImportGoogle(file.FileName); UpdateLoginButtons(); status.Text = "앱의 Google 로그인 설정을 저장했어요. 이후에는 로그인 버튼만 누르면 돼요."; } catch (Exception error) { status.Text = MailUtil.Error(error); }
            };
            var naver = Page("네이버"); Explain(naver, "네이버 앱 비밀번호로 연결", "네이버 메일에서 IMAP 사용을 켜고, 2단계 인증의 애플리케이션 비밀번호를 만들어 주세요.");
            var naverHelp = Button("네이버 설정 방법 열기"); naverHelp.Click += delegate { Process.Start(new ProcessStartInfo("https://help.naver.com/service/5640/contents/8584?lang=ko&osType=COMMONOS") { UseShellExecute = true }); }; naver.Children.Add(naverHelp);
            Label(naver, "이메일 주소"); naver.Children.Add(naverAddress); naverAddress.ToolTip = "네이버 메일 주소를 입력하세요.";
            Label(naver, "애플리케이션 비밀번호"); Password(naverPassword); naver.Children.Add(naverPassword);
            Note(naver, "imap.naver.com · 993 · SSL/TLS\n비밀번호는 이 PC의 암호화 저장소에만 보관해요. 채팅에 비밀번호를 입력할 필요는 없어요.");
            var outlook = Page("Outlook"); Explain(outlook, "Outlook 알림 연결", "Microsoft 계정으로 로그인해 주세요. 개인 Outlook과 Microsoft 365 회사 계정을 연결할 수 있어요.");
            microsoftLogin.Margin = new Thickness(0, 12, 0, 12); outlook.Children.Add(microsoftLogin); microsoftLogin.Click += async delegate { await ConnectSelected(); }; ReadyNote(outlook, microsoftReady);
            Note(outlook, "로그인 버튼을 누르면 확인 코드와 Microsoft 로그인 화면이 열려요. 연결 후에는 자동으로 새 메일을 확인해요.");
            var microsoftSetup = Developer(outlook, microsoftDeveloper); Note(microsoftSetup, "앱 제작자가 배포 전에 한 번 등록합니다. 일반 사용자는 클라이언트 ID를 입력하지 않아요.");
            Label(microsoftSetup, "앱의 Microsoft 클라이언트 ID"); microsoftSetup.Children.Add(microsoftId);
            var saveMicrosoft = Button("앱 로그인 설정 저장"); saveMicrosoft.Margin = new Thickness(0, 8, 0, 8); microsoftSetup.Children.Add(saveMicrosoft);
            saveMicrosoft.Click += delegate { try { appConfig.SetMicrosoft(microsoftId.Text); UpdateLoginButtons(); status.Text = "앱의 Microsoft 로그인 설정을 저장했어요."; } catch (Exception error) { status.Text = MailUtil.Error(error); } };
            var imap = Page("회사 IMAP"); Explain(imap, "회사 메일 서버로 연결", "Microsoft 365가 아닌 회사 메일은 IMAP 서버와 해당 서비스의 앱 비밀번호로 연결할 수 있어요.");
            Label(imap, "이메일 / 로그인 계정"); imap.Children.Add(imapAddress);
            Label(imap, "IMAP 서버"); imap.Children.Add(imapHost);
            Label(imap, "포트"); imapPort.Text = "993"; imap.Children.Add(imapPort); imap.Children.Add(startTls);
            Label(imap, "앱 비밀번호"); Password(imapPassword); imap.Children.Add(imapPassword);
            Note(imap, "SSL/TLS 또는 필수 STARTTLS만 사용해요. 회사 서버에서 비밀번호 로그인을 허용해야 해요.");
            var side = new DockPanel(); Grid.SetColumn(side, 1); body.Children.Add(side);
            var sideTop = new StackPanel(); DockPanel.SetDock(sideTop, Dock.Top); side.Children.Add(sideTop);
            sideTop.Children.Add(new TextBlock { Text = "연결한 계정", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) }); sideTop.Children.Add(details);
            details.Click += delegate { if (!refreshing) try { service.Details(details.IsChecked == true); } catch (Exception error) { status.Text = MailUtil.Error(error); } };
            side.Children.Add(new ScrollViewer { Content = accounts, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            service.Changed += Changed; Closed += delegate { lifetime.Cancel(); if (connecting != null) connecting.Cancel(); service.Changed -= Changed; };
            tabs.SelectionChanged += delegate(object sender, SelectionChangedEventArgs e) { if (e.Source == tabs) UpdateLoginButtons(); };
            status.Text = "연결 후 약 60초마다 받은편지함을 확인해요. 알림을 다시 켜면 그 시점 이후 새 메일부터 알려줘요."; UpdateLoginButtons(); Refresh();
        }
        static void ReadyNote(StackPanel panel, TextBlock note) { note.FontSize = 12; note.TextWrapping = TextWrapping.Wrap; note.Foreground = Brushes.DimGray; note.Margin = new Thickness(0, 0, 0, 10); panel.Children.Add(note); }
        static StackPanel Developer(StackPanel panel, Expander expander)
        {
            var content = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; expander.Header = "개발자 설정 · 배포 전 1회"; expander.IsExpanded = false; expander.Margin = new Thickness(0, 22, 0, 0); expander.FontSize = 12; expander.Content = content; panel.Children.Add(expander); return content;
        }
        void UpdateLoginButtons()
        {
            bool google = appConfig.GoogleReady, microsoft = appConfig.MicrosoftId.Length > 0;
            googleLogin.IsEnabled = !busy && google; microsoftLogin.IsEnabled = !busy && microsoft;
            googleLogin.Opacity = google ? 1 : .45; microsoftLogin.Opacity = microsoft ? 1 : .45;
            googleReady.Text = google ? "등록한 Google 앱으로 로그인합니다. 앱의 테스트·공개 상태에 따라 계정 제한이나 재로그인이 필요할 수 있어요." : "공개본에는 Google 앱 등록 값이 포함되어 있지 않아요. 개발자 설정에서 본인의 앱을 등록해 주세요.";
            microsoftReady.Text = microsoft ? "Microsoft 공식 로그인으로 연결합니다." : "Outlook 연결 서비스가 아직 준비되지 않았어요. 앱 제작자의 초기 설정이 필요해요.";
            googleFile.Text = google ? appConfig.GoogleFile : "";
            if (microsoft) microsoftId.Text = appConfig.MicrosoftId;
            connect.Visibility = tabs.SelectedIndex == 0 || tabs.SelectedIndex == 2 ? Visibility.Collapsed : Visibility.Visible;
        }
        static TextBox Input() { return new TextBox { FontSize = 13, Padding = new Thickness(8, 7, 8, 7), MinHeight = 34 }; }
        static Button Button(string text) { var b = PetWindow.RoundButton(); b.Content = text; b.MinWidth = Math.Max(90, text.Length * 13 + 28); b.Margin = new Thickness(0, 0, 8, 0); b.Background = PetWindow.ColorBrush("#7060F7"); b.Foreground = Brushes.White; return b; }
        static void Password(PasswordBox password) { password.FontSize = 13; password.Padding = new Thickness(8, 7, 8, 7); password.MinHeight = 34; }
        StackPanel Page(string title) { var panel = new StackPanel { Margin = new Thickness(16) }; tabs.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }); return panel; }
        static void Explain(StackPanel panel, string title, string description) { panel.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap }); Note(panel, description); }
        static void Label(StackPanel panel, string text) { panel.Children.Add(new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(0, 12, 0, 5), Foreground = Brushes.DimGray }); }
        static void Note(StackPanel panel, string text) { panel.Children.Add(new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 8), LineHeight = 20 }); }
        void Changed() { if (!lifetime.IsCancellationRequested) Dispatcher.BeginInvoke(new Action(Refresh)); }
        void Refresh()
        {
            if (lifetime.IsCancellationRequested) return;
            refreshing = true; var state = service.Snapshot(); details.IsChecked = state.ShowDetails; accounts.Children.Clear();
            if (state.Accounts.Count == 0) accounts.Children.Add(new TextBlock { Text = "아직 연결한 계정이 없어요.\n왼쪽에서 메일을 선택해 연결해 주세요.", TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Brushes.Gray, Margin = new Thickness(0, 16, 0, 0) });
            foreach (var account in state.Accounts)
            {
                var panel = new StackPanel(); var toggle = new CheckBox { Content = new TextBlock { Text = account.Display, TextWrapping = TextWrapping.Wrap, FontSize = 13 }, IsChecked = account.Enabled };
                string id = account.Id; toggle.Click += delegate { try { service.Enable(id, toggle.IsChecked == true); } catch (Exception error) { status.Text = MailUtil.Error(error); } }; panel.Children.Add(toggle);
                panel.Children.Add(new TextBlock { Text = account.Enabled ? account.Status : "알림 꺼짐", FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18, 6, 0, 9) });
                var remove = Button("연결 해제"); remove.FontSize = 11; remove.Height = 27; remove.Background = PetWindow.ColorBrush("#EEEAF3"); remove.Foreground = Brushes.DimGray; remove.HorizontalAlignment = HorizontalAlignment.Left;
                remove.Click += delegate { try { service.Remove(id); } catch (Exception error) { status.Text = MailUtil.Error(error); } }; panel.Children.Add(remove);
                accounts.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10), Child = panel });
            }
            if (service.LoadError != null) status.Text = service.LoadError;
            refreshing = false;
        }
        void SetBusy(bool value) { busy = value; connect.IsEnabled = !value; tabs.IsEnabled = !value; check.IsEnabled = !value; cancel.IsEnabled = value && connecting != null; UpdateLoginButtons(); }
        async Task ConnectSelected()
        {
            if (busy) return;
            string provider = tabs.SelectedIndex == 0 ? "gmail" : tabs.SelectedIndex == 1 ? "naver" : tabs.SelectedIndex == 2 ? "microsoft" : "imap";
            string setting = provider == "gmail" ? appConfig.GoogleFile : appConfig.MicrosoftId;
            if (provider == "gmail" && !appConfig.GoogleReady || provider == "microsoft" && setting.Length == 0) { status.Text = "이 앱의 로그인 서비스가 아직 준비되지 않았어요. 앱 제작자가 초기 설정을 등록해야 해요."; return; }
            int port = 993; if (provider == "imap" && !Int32.TryParse(imapPort.Text, out port)) { status.Text = "IMAP 포트 숫자를 확인해 주세요."; return; }
            connecting = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); SetBusy(true); status.Text = "메일 연결을 확인하고 있어요…"; loginInfo.Text = "";
            string address = provider == "naver" ? naverAddress.Text : imapAddress.Text, password = provider == "naver" ? naverPassword.Password : imapPassword.Password;
            naverPassword.Clear(); imapPassword.Clear();
            try
            {
                await service.Connect(provider, setting, address, password, imapHost.Text, port, startTls.IsChecked == true,
                    delegate(string url, string instruction) { Dispatcher.Invoke(new Action(delegate { loginInfo.Text = instruction; Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); })); }, connecting.Token);
                loginInfo.Text = "연결했어요. 이후 도착하는 새 메일부터 알려줄게요.";
                status.Text = "연결한 계정의 알림이 켜졌어요.";
            }
            catch (Exception error) { status.Text = MailUtil.Error(error); }
            finally { password = null; connecting.Dispose(); connecting = null; SetBusy(false); Refresh(); }
        }
        public void ValidateUi()
        {
            if (tabs.Items.Count != 4 || accounts.Children.Count == 0 || connect.Content.ToString() != "선택한 메일 연결") throw new Exception("Mail settings UI incomplete");
            if (googleDeveloper.IsExpanded || microsoftDeveloper.IsExpanded || googleLogin.IsEnabled != appConfig.GoogleReady || microsoftLogin.IsEnabled != (appConfig.MicrosoftId.Length > 0)) throw new Exception("OAuth consumer/developer UI incomplete");
        }
    }
}
