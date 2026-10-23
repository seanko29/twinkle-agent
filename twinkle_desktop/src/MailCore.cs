using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

namespace TwinkleDesktop
{
    public sealed class OAuthAppConfig
    {
        readonly string folder;
        public OAuthAppConfig(string root) { folder = Path.Combine(root, "app_config"); }
        public string GoogleFile { get { return Path.Combine(folder, "google_desktop.json"); } }
        public bool GoogleReady
        {
            get { try { ValidateGoogle(File.ReadAllText(GoogleFile)); return true; } catch { return false; } }
        }
        public string MicrosoftId
        {
            get { try { Guid value; string id = File.ReadAllText(Path.Combine(folder, "microsoft_client_id.txt")).Trim(); return Guid.TryParse(id, out value) ? value.ToString() : ""; } catch { return ""; } }
        }
        static Dictionary<string, string> ValidateGoogle(string text)
        {
            if (text.Length > 65536) throw new MailConnectionException("Google 데스크톱 OAuth JSON을 확인해 주세요.");
            var all = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(text); var installed = MailUtil.Obj(MailUtil.Get(all, "installed"));
            string id = MailUtil.Str(installed, "client_id"), secret = MailUtil.Str(installed, "client_secret");
            if (!id.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal) || String.IsNullOrWhiteSpace(secret)) throw new MailConnectionException("Google의 '데스크톱 앱' 유형 OAuth JSON만 등록할 수 있어요.");
            return new Dictionary<string, string> { { "client_id", id }, { "client_secret", secret } };
        }
        void Write(string path, string content)
        {
            Directory.CreateDirectory(folder); string temporary = path + ".tmp"; File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        public void ImportGoogle(string source)
        {
            if (new FileInfo(source).Length > 65536) throw new MailConnectionException("Google 설정 파일이 너무 커요.");
            var installed = ValidateGoogle(File.ReadAllText(source));
            Write(GoogleFile, new JavaScriptSerializer().Serialize(new { installed = installed }));
        }
        public void SetMicrosoft(string value)
        {
            Guid id; if (!Guid.TryParse(value.Trim(), out id)) throw new MailConnectionException("Microsoft 앱의 클라이언트 ID를 확인해 주세요.");
            Write(Path.Combine(folder, "microsoft_client_id.txt"), id.ToString());
        }
    }
    public sealed class MailAccount
    {
        public string Id { get; set; }
        public string Provider { get; set; }
        public string Address { get; set; }
        public bool Enabled { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public string ExpiresUtc { get; set; }
        public string Password { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public bool StartTls { get; set; }
        public string HistoryId { get; set; }
        public uint UidValidity { get; set; }
        public uint LastUid { get; set; }
        public string LastCheckedUtc { get; set; }
        public string ConnectedUtc { get; set; }
        public List<string> Seen { get; set; }
        public string Status { get; set; }
        public bool NeedsBaseline { get; set; }
        public int Revision { get; set; }
        public MailAccount() { Id = Guid.NewGuid().ToString("N"); Enabled = true; Port = 993; Seen = new List<string>(); Status = "연결됨"; ConnectedUtc = MailUtil.UtcNow(); }
        public string Display { get { return MailUtil.ProviderLabel(Provider) + "  ·  " + Address; } }
    }
    public sealed class MailState
    {
        public List<MailAccount> Accounts { get; set; }
        public bool ShowDetails { get; set; }
        public MailState() { Accounts = new List<MailAccount>(); }
    }
    public sealed class NewMail
    {
        public string Id, Subject, From;
    }
    public static class MailUtil
    {
        public static string ProviderLabel(string provider) { return provider == "gmail" ? "Gmail" : provider == "naver" ? "네이버" : provider == "microsoft" ? "Outlook" : "회사 IMAP"; }
        public static string Str(IDictionary<string, object> obj, string key) { object value; return obj != null && obj.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : ""; }
        public static Dictionary<string, object> Obj(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        public static object Get(IDictionary<string, object> obj, string key) { object value; return obj != null && obj.TryGetValue(key, out value) ? value : null; }
        public static IEnumerable<object> Items(object value) { return (value as IEnumerable ?? new object[0]).Cast<object>(); }
        public static string UtcNow() { return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture); }
        public static DateTime Date(string value) { DateTime date; return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out date) ? date.ToUniversalTime() : DateTime.MinValue; }
        public static T Clone<T>(T value) { var json = new JavaScriptSerializer(); return json.Deserialize<T>(json.Serialize(value)); }
        public static string Query(IEnumerable<KeyValuePair<string, string>> data) { return String.Join("&", data.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value ?? ""))); }
        public static string RandomToken() { var bytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); return Base64Url(bytes); }
        public static string Base64Url(byte[] bytes) { return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        public static string Error(Exception error)
        {
            if (error is OperationCanceledException) return "연결이 취소되었거나 응답 시간이 초과됐어요.";
            if (error is MailConnectionException) return error.Message;
            if (error is MailKit.Security.AuthenticationException) return "로그인에 실패했어요. IMAP 사용 설정과 앱 비밀번호를 확인해 주세요.";
            if (error is MailKit.Security.SslHandshakeException) return "서버 인증서를 확인할 수 없어요. 서버 주소·TLS 설정을 확인해 주세요.";
            if (error is IOException || error is HttpRequestException || error is SocketException) return "메일 서버에 연결하지 못했어요. 네트워크와 서버 설정을 확인해 주세요.";
            return "연결 설정을 확인해 주세요. (" + error.GetType().Name + ")";
        }
        public static PetNotification Notification(MailAccount account, List<NewMail> messages, bool details)
        {
            return new PetNotification { source = MailUtil.ProviderLabel(account.Provider), title = MailUtil.ProviderLabel(account.Provider) + " 새 메일 " + messages.Count + "통", message = details ? String.Join("\n", messages.Take(3).Select(m => m.From + " · " + m.Subject)) : "받은편지함에 새 메일이 도착했어요." };
        }
    }
    public sealed class MailConnectionException : Exception
    {
        public int StatusCode;
        public string Code;
        public MailConnectionException(string message, int status = 0, string code = "") : base(message) { StatusCode = status; Code = code; }
    }
    public sealed class MailVault
    {
        readonly string path;
        static readonly byte[] entropy = Encoding.UTF8.GetBytes("TwinkleDesktop.Mail.v1");
        public MailVault(string root) { path = Path.Combine(root, "user_mail", "accounts.dat"); }
        public MailState Load()
        {
            if (!File.Exists(path)) return new MailState();
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path), entropy, DataProtectionScope.CurrentUser);
            try
            {
                var value = new JavaScriptSerializer().Deserialize<MailState>(Encoding.UTF8.GetString(plain));
                if (value == null || value.Accounts == null) throw new InvalidDataException(); return value;
            }
            finally { Array.Clear(plain, 0, plain.Length); }
        }
        public void Save(MailState state)
        {
            byte[] plain = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(state));
            byte[] cipher;
            try { cipher = ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser); }
            finally { Array.Clear(plain, 0, plain.Length); }
            Directory.CreateDirectory(Path.GetDirectoryName(path)); string temporary = path + ".tmp"; File.WriteAllBytes(temporary, cipher);
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
    }
    public sealed class MailHttp : IDisposable
    {
        readonly HttpClient client;
        public MailHttp(HttpMessageHandler handler = null)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            client = handler == null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(25);
        }
        public async Task<Dictionary<string, object>> Request(string url, string token, Dictionary<string, string> form, CancellationToken cancel)
        {
            var uri = new Uri(url);
            if (uri.Scheme != "https" || !new[] { "gmail.googleapis.com", "oauth2.googleapis.com", "login.microsoftonline.com", "graph.microsoft.com" }.Contains(uri.Host)) throw new MailConnectionException("허용되지 않은 인증/메일 서버입니다.");
            using (var request = new HttpRequestMessage(form == null ? HttpMethod.Get : HttpMethod.Post, uri))
            {
                if (!String.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (uri.Host == "graph.microsoft.com") request.Headers.TryAddWithoutValidation("Prefer", "IdType=\"ImmutableId\"");
                if (form != null) request.Content = new FormUrlEncodedContent(form);
                using (var response = await client.SendAsync(request, cancel).ConfigureAwait(false))
                {
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Dictionary<string, object> json;
                    try { json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(text); }
                    catch { throw new MailConnectionException("메일 서버 응답을 읽지 못했어요."); }
                    if (json == null) throw new MailConnectionException("메일 서버 응답이 비어 있어요.");
                    if (!response.IsSuccessStatusCode)
                    {
                        string code = MailUtil.Get(json, "error") is string ? MailUtil.Str(json, "error") : MailUtil.Str(MailUtil.Obj(MailUtil.Get(json, "error")), "code");
                        string message = (int)response.StatusCode == 401 || code == "invalid_grant" ? "로그인이 만료됐어요. 연결을 해제한 뒤 다시 로그인해 주세요." : (int)response.StatusCode == 403 ? "메일 접근 권한과 앱 등록 설정을 확인해 주세요." : "메일 서비스 요청에 실패했어요. (HTTP " + (int)response.StatusCode + ")";
                        throw new MailConnectionException(message, (int)response.StatusCode, code);
                    }
                    return json;
                }
            }
        }
        public void Dispose() { client.Dispose(); }
    }
    public sealed class MailProviders
    {
        public const string GoogleScope = "https://www.googleapis.com/auth/gmail.metadata";
        public const string MicrosoftScope = "offline_access User.Read Mail.ReadBasic";
        readonly MailHttp http;
        public MailProviders(MailHttp http) { this.http = http; }
        static void SetToken(MailAccount account, Dictionary<string, object> value)
        {
            account.AccessToken = MailUtil.Str(value, "access_token");
            if (String.IsNullOrEmpty(account.AccessToken)) throw new MailConnectionException("인증 토큰을 받지 못했어요.");
            string refresh = MailUtil.Str(value, "refresh_token"); if (refresh.Length > 0) account.RefreshToken = refresh;
            double seconds; if (!Double.TryParse(MailUtil.Str(value, "expires_in"), NumberStyles.Any, CultureInfo.InvariantCulture, out seconds)) seconds = 3600;
            account.ExpiresUtc = DateTime.UtcNow.AddSeconds(seconds).ToString("o");
        }
        async Task Access(MailAccount account, CancellationToken cancel)
        {
            if (!String.IsNullOrEmpty(account.AccessToken) && MailUtil.Date(account.ExpiresUtc) > DateTime.UtcNow.AddMinutes(2)) return;
            if (String.IsNullOrEmpty(account.RefreshToken)) throw new MailConnectionException("다시 로그인해 주세요.");
            var form = new Dictionary<string, string> { { "client_id", account.ClientId }, { "grant_type", "refresh_token" }, { "refresh_token", account.RefreshToken } };
            if (account.Provider == "gmail") form["client_secret"] = account.ClientSecret;
            else form["scope"] = MicrosoftScope;
            SetToken(account, await http.Request(account.Provider == "gmail" ? "https://oauth2.googleapis.com/token" : "https://login.microsoftonline.com/common/oauth2/v2.0/token", null, form, cancel).ConfigureAwait(false));
        }
        public async Task<MailAccount> ConnectGoogle(string file, Action<string, string> showLogin, CancellationToken cancel)
        {
            if (new FileInfo(file).Length > 65536) throw new MailConnectionException("OAuth 설정 JSON을 확인해 주세요.");
            var config = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(file)); var installed = MailUtil.Obj(MailUtil.Get(config, "installed"));
            var account = new MailAccount { Provider = "gmail", ClientId = MailUtil.Str(installed, "client_id"), ClientSecret = MailUtil.Str(installed, "client_secret") };
            if (!account.ClientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal)) throw new MailConnectionException("Google의 '데스크톱 앱' 유형 OAuth JSON이 필요해요.");
            string verifier = MailUtil.RandomToken(), state = MailUtil.RandomToken(), code;
            string challenge; using (var sha = SHA256.Create()) challenge = MailUtil.Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            string redirect = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/oauth2callback";
            try
            {
                string url = "https://accounts.google.com/o/oauth2/v2/auth?" + MailUtil.Query(new Dictionary<string, string> { { "client_id", account.ClientId }, { "redirect_uri", redirect }, { "response_type", "code" }, { "scope", GoogleScope }, { "access_type", "offline" }, { "prompt", "consent select_account" }, { "state", state }, { "code_challenge", challenge }, { "code_challenge_method", "S256" } });
                showLogin(url, "브라우저에서 Google 계정으로 로그인해 주세요.");
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                {
                    timeout.CancelAfter(TimeSpan.FromMinutes(4));
                    using (timeout.Token.Register(delegate { listener.Stop(); }))
                    {
                        while (true)
                        {
                            TcpClient connection;
                            try { connection = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                            catch (Exception) { timeout.Token.ThrowIfCancellationRequested(); throw; }
                            using (connection)
                            {
                                connection.ReceiveTimeout = 4000; connection.SendTimeout = 4000;
                                var stream = connection.GetStream(); var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                                string line = reader.ReadLine() ?? ""; string target = line.Split(' ').Length > 1 ? line.Split(' ')[1] : "/";
                                if (!target.StartsWith("/oauth2callback?", StringComparison.Ordinal)) { WriteCallback(stream, "요청을 확인할 수 없습니다.", 404); continue; }
                                var query = ParseQuery(target.Substring(target.IndexOf('?') + 1));
                                if (MailUtil.Str(query, "state") != state) { WriteCallback(stream, "로그인 요청이 일치하지 않습니다.", 400); throw new MailConnectionException("로그인 상태가 일치하지 않아요. 다시 시도해 주세요."); }
                                if (!String.IsNullOrEmpty(MailUtil.Str(query, "error"))) { WriteCallback(stream, "로그인이 취소됐습니다.", 400); throw new MailConnectionException("Google 로그인이 취소됐어요."); }
                                code = MailUtil.Str(query, "code"); WriteCallback(stream, "로그인을 받았습니다. 이 창을 닫고 트윙클로 돌아가 주세요.", 200); break;
                            }
                        }
                    }
                }
                SetToken(account, await http.Request("https://oauth2.googleapis.com/token", null, new Dictionary<string, string> { { "client_id", account.ClientId }, { "client_secret", account.ClientSecret }, { "code", code }, { "code_verifier", verifier }, { "redirect_uri", redirect }, { "grant_type", "authorization_code" } }, cancel).ConfigureAwait(false));
            }
            finally { listener.Stop(); }
            var profile = await http.Request("https://gmail.googleapis.com/gmail/v1/users/me/profile", account.AccessToken, null, cancel).ConfigureAwait(false);
            account.Address = MailUtil.Str(profile, "emailAddress"); account.HistoryId = MailUtil.Str(profile, "historyId"); account.LastCheckedUtc = MailUtil.UtcNow(); return account;
        }
        internal static Dictionary<string, object> ParseQuery(string query)
        {
            var result = new Dictionary<string, object>(); foreach (string part in query.Split('&')) { var pair = part.Split(new[] { '=' }, 2); result[Uri.UnescapeDataString(pair[0].Replace('+', ' '))] = pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : ""; } return result;
        }
        static void WriteCallback(Stream stream, string text, int status)
        {
            byte[] body = Encoding.UTF8.GetBytes("<!doctype html><meta charset=utf-8><title>트윙클 로그인</title><p>" + text + "</p>");
            byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + (status == 200 ? "OK" : "Error") + "\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n"); stream.Write(header, 0, header.Length); stream.Write(body, 0, body.Length);
        }
        public async Task<MailAccount> ConnectMicrosoft(string clientId, Action<string, string> showLogin, CancellationToken cancel)
        {
            Guid parsed; if (!Guid.TryParse(clientId, out parsed)) throw new MailConnectionException("Azure 앱 등록의 애플리케이션(클라이언트) ID를 입력해 주세요.");
            var account = new MailAccount { Provider = "microsoft", ClientId = parsed.ToString() };
            var device = await http.Request("https://login.microsoftonline.com/common/oauth2/v2.0/devicecode", null, new Dictionary<string, string> { { "client_id", account.ClientId }, { "scope", MicrosoftScope } }, cancel).ConfigureAwait(false);
            string login = MailUtil.Str(device, "verification_uri"), userCode = MailUtil.Str(device, "user_code");
            if (!Uri.IsWellFormedUriString(login, UriKind.Absolute) || new Uri(login).Scheme != "https") throw new MailConnectionException("로그인 주소를 확인할 수 없어요.");
            showLogin(login, "Microsoft 로그인 코드: " + userCode + "\n열린 브라우저에 이 코드를 입력해 주세요.");
            int interval, expiry; if (!Int32.TryParse(MailUtil.Str(device, "interval"), out interval)) interval = 5; if (!Int32.TryParse(MailUtil.Str(device, "expires_in"), out expiry)) expiry = 900;
            DateTime until = DateTime.UtcNow.AddSeconds(Math.Min(900, expiry));
            while (true)
            {
                if (DateTime.UtcNow > until) throw new MailConnectionException("로그인 코드가 만료됐어요. 다시 연결해 주세요.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, interval)), cancel).ConfigureAwait(false);
                try { SetToken(account, await http.Request("https://login.microsoftonline.com/common/oauth2/v2.0/token", null, new Dictionary<string, string> { { "grant_type", "urn:ietf:params:oauth:grant-type:device_code" }, { "client_id", account.ClientId }, { "device_code", MailUtil.Str(device, "device_code") } }, cancel).ConfigureAwait(false)); break; }
                catch (MailConnectionException error) { if (error.Code == "authorization_pending") continue; if (error.Code == "slow_down") { interval += 5; continue; } if (error.Code == "authorization_declined" || error.Code == "expired_token") throw new MailConnectionException("Microsoft 로그인이 취소되었거나 만료됐어요."); throw; }
            }
            var me = await http.Request("https://graph.microsoft.com/v1.0/me?$select=mail,userPrincipalName", account.AccessToken, null, cancel).ConfigureAwait(false);
            account.Address = MailUtil.Str(me, "mail"); if (String.IsNullOrEmpty(account.Address)) account.Address = MailUtil.Str(me, "userPrincipalName");
            // Verify mail permission before saving, while suppressing historical notifications.
            await http.Request("https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?$select=id&$top=1", account.AccessToken, null, cancel).ConfigureAwait(false);
            account.LastCheckedUtc = MailUtil.UtcNow(); account.ConnectedUtc = account.LastCheckedUtc; return account;
        }
        public async Task<List<NewMail>> Poll(MailAccount account, bool baseline, CancellationToken cancel)
        {
            string checkedAt = MailUtil.UtcNow();
            List<NewMail> result;
            if (account.Provider == "gmail" || account.Provider == "microsoft")
            {
                await Access(account, cancel).ConfigureAwait(false);
                result = account.Provider == "gmail" ? await Gmail(account, baseline, cancel).ConfigureAwait(false) : await Microsoft(account, baseline, cancel).ConfigureAwait(false);
            }
            else result = await Imap(account, baseline, cancel).ConfigureAwait(false);
            account.LastCheckedUtc = checkedAt; account.NeedsBaseline = false; return result;
        }
        internal async Task<List<NewMail>> Gmail(MailAccount account, bool baseline, CancellationToken cancel)
        {
            var result = new List<NewMail>();
            if (baseline || String.IsNullOrEmpty(account.HistoryId))
            {
                account.HistoryId = MailUtil.Str(await http.Request("https://gmail.googleapis.com/gmail/v1/users/me/profile", account.AccessToken, null, cancel).ConfigureAwait(false), "historyId"); return result;
            }
            string page = "", history = account.HistoryId; var ids = new HashSet<string>();
            do
            {
                Dictionary<string, object> data = null;
                try { data = await http.Request("https://gmail.googleapis.com/gmail/v1/users/me/history?startHistoryId=" + Uri.EscapeDataString(account.HistoryId) + "&historyTypes=messageAdded&labelId=INBOX&maxResults=100" + (page.Length == 0 ? "" : "&pageToken=" + Uri.EscapeDataString(page)), account.AccessToken, null, cancel).ConfigureAwait(false); }
                catch (MailConnectionException error) { if (error.StatusCode != 404) throw; }
                if (data == null) return await Gmail(account, true, cancel).ConfigureAwait(false);
                foreach (var h in MailUtil.Items(MailUtil.Get(data, "history"))) foreach (var added in MailUtil.Items(MailUtil.Get(MailUtil.Obj(h), "messagesAdded")))
                {
                    var message = MailUtil.Obj(MailUtil.Get(MailUtil.Obj(added), "message")); string id = MailUtil.Str(message, "id");
                    if (id.Length > 0) ids.Add(id);
                }
                history = MailUtil.Str(data, "historyId"); page = MailUtil.Str(data, "nextPageToken");
            } while (page.Length > 0);
            foreach (string id in ids.Where(id => !account.Seen.Contains(id)))
            {
                Dictionary<string, object> data;
                try { data = await http.Request("https://gmail.googleapis.com/gmail/v1/users/me/messages/" + Uri.EscapeDataString(id) + "?format=metadata&metadataHeaders=From&metadataHeaders=Subject", account.AccessToken, null, cancel).ConfigureAwait(false); }
                catch (MailConnectionException error) { if (error.StatusCode == 404) continue; throw; }
                if (!MailUtil.Items(MailUtil.Get(data, "labelIds")).Any(label => Convert.ToString(label) == "INBOX")) continue;
                var headers = MailUtil.Items(MailUtil.Get(MailUtil.Obj(MailUtil.Get(data, "payload")), "headers")).Select(MailUtil.Obj).ToList();
                result.Add(new NewMail { Id = id, Subject = Header(headers, "Subject"), From = Header(headers, "From") });
            }
            account.HistoryId = history; return Filter(account, result);
        }
        static string Header(List<Dictionary<string, object>> headers, string name) { return headers.Where(h => String.Equals(MailUtil.Str(h, "name"), name, StringComparison.OrdinalIgnoreCase)).Select(h => MailUtil.Str(h, "value")).FirstOrDefault() ?? ""; }
        internal async Task<List<NewMail>> Microsoft(MailAccount account, bool baseline, CancellationToken cancel)
        {
            var result = new List<NewMail>(); if (baseline || String.IsNullOrEmpty(account.LastCheckedUtc)) { account.ConnectedUtc = MailUtil.UtcNow(); return result; }
            DateTime since = MailUtil.Date(account.LastCheckedUtc).AddSeconds(-120); DateTime connected = MailUtil.Date(account.ConnectedUtc);
            if (connected > since) since = connected;
            string url = "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?$select=id,subject,from,receivedDateTime&$top=100&$orderby=receivedDateTime%20desc&$filter=" + Uri.EscapeDataString("receivedDateTime ge " + since.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            do
            {
                var data = await http.Request(url, account.AccessToken, null, cancel).ConfigureAwait(false);
                foreach (var raw in MailUtil.Items(MailUtil.Get(data, "value")))
                {
                    var m = MailUtil.Obj(raw); if (MailUtil.Get(m, "@removed") != null) continue;
                    if (MailUtil.Date(MailUtil.Str(m, "receivedDateTime")) < connected) continue;
                    string id = MailUtil.Str(m, "id"); var from = MailUtil.Obj(MailUtil.Get(MailUtil.Obj(MailUtil.Get(m, "from")), "emailAddress"));
                    if (id.Length > 0 && !account.Seen.Contains(id)) result.Add(new NewMail { Id = id, Subject = MailUtil.Str(m, "subject"), From = MailUtil.Str(from, "name") + " " + MailUtil.Str(from, "address") });
                }
                url = MailUtil.Str(data, "@odata.nextLink");
            } while (url.Length > 0);
            return Filter(account, result);
        }
        internal static List<NewMail> Filter(MailAccount account, List<NewMail> items)
        {
            var seen = new HashSet<string>(account.Seen ?? new List<string>()); var result = items.Where(m => seen.Add(m.Id)).ToList();
            account.Seen = (account.Seen ?? new List<string>()).Concat(result.Select(m => m.Id)).Distinct().Reverse().Take(2000).Reverse().ToList(); return result;
        }
        public async Task<List<NewMail>> Imap(MailAccount account, bool baseline, CancellationToken cancel)
        {
            if (account.Provider == "naver") { account.Host = "imap.naver.com"; account.Port = 993; account.StartTls = false; }
            if (String.IsNullOrWhiteSpace(account.Host) || Uri.CheckHostName(account.Host) == UriHostNameType.Unknown || account.Port < 1 || account.Port > 65535) throw new MailConnectionException("IMAP 서버 주소와 포트를 확인해 주세요.");
            using (var client = new ImapClient())
            {
                client.Timeout = 20000;
                await client.ConnectAsync(account.Host, account.Port, account.StartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect, cancel).ConfigureAwait(false);
                await client.AuthenticateAsync(account.Address, account.Password, cancel).ConfigureAwait(false);
                var inbox = client.Inbox; await inbox.OpenAsync(FolderAccess.ReadOnly, cancel).ConfigureAwait(false);
                var result = await ReadInbox(inbox, account, baseline, cancel).ConfigureAwait(false);
                await client.DisconnectAsync(true, cancel).ConfigureAwait(false); return result;
            }
        }
        internal static async Task<List<NewMail>> ReadInbox(IMailFolder inbox, MailAccount account, bool baseline, CancellationToken cancel)
        {
                var result = new List<NewMail>();
                if (baseline || account.UidValidity != inbox.UidValidity)
                {
                    account.UidValidity = inbox.UidValidity;
                    if (inbox.UidNext.HasValue) account.LastUid = inbox.UidNext.Value.Id - 1;
                    else { var ids = await inbox.SearchAsync(SearchQuery.All, cancel).ConfigureAwait(false); account.LastUid = ids.Count > 0 ? ids[ids.Count - 1].Id : 0; }
                }
                else if (account.LastUid < UInt32.MaxValue)
                {
                    var ids = await inbox.SearchAsync(SearchQuery.Uids(new UniqueIdRange(new UniqueId(account.LastUid + 1), UniqueId.MaxValue)), cancel).ConfigureAwait(false);
                    // IMAP servers can return the last UID for an empty reversed '*' range; filter it explicitly.
                    var fresh = ids.Where(id => id.Id > account.LastUid).ToList();
                    if (fresh.Count > 0)
                    {
                        foreach (var message in await inbox.FetchAsync(fresh, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope, cancel).ConfigureAwait(false))
                            result.Add(new NewMail { Id = account.UidValidity + ":" + message.UniqueId.Id, Subject = message.Envelope.Subject ?? "", From = message.Envelope.From.ToString() });
                        account.LastUid = fresh.Max(id => id.Id);
                    }
                }
                return Filter(account, result);
        }
    }
    public sealed class MailService : IDisposable
    {
        readonly object gate = new object(); readonly MailVault vault; readonly MailHttp http; readonly MailProviders providers;
        readonly CancellationTokenSource stop = new CancellationTokenSource(); readonly SemaphoreSlim network = new SemaphoreSlim(1, 1);
        readonly Action<PetNotification> notify; MailState state; bool started;
        public event Action Changed;
        public string LoadError { get; private set; }
        public MailService(string root, Action<PetNotification> notify, MailHttp transport = null)
        {
            vault = new MailVault(root); http = transport ?? new MailHttp(); providers = new MailProviders(http); this.notify = notify;
            try { state = vault.Load(); } catch { state = new MailState(); LoadError = "저장된 메일 설정을 열지 못했어요. 원본 accounts.dat는 보존했으며 새 설정으로 덮어쓰지 않아요."; }
        }
        public MailState Snapshot() { lock (gate) return MailUtil.Clone(state); }
        void Change() { var handler = Changed; if (handler != null && !stop.IsCancellationRequested) handler(); }
        void Save() { if (LoadError != null) throw new MailConnectionException(LoadError); vault.Save(state); }
        public void Start() { if (started) return; started = true; Task.Run((Func<Task>)Loop); }
        async Task Loop()
        {
            while (!stop.IsCancellationRequested)
            {
                try { await CheckNow(stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { if (stop.IsCancellationRequested) return; }
                catch { }
                try { await Task.Delay(TimeSpan.FromSeconds(60), stop.Token).ConfigureAwait(false); } catch { return; }
            }
        }
        public async Task CheckNow(CancellationToken cancel)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, stop.Token))
            {
                await network.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    foreach (var copy in Snapshot().Accounts.Where(a => a.Enabled))
                    {
                        List<NewMail> messages = null;
                        try { messages = await providers.Poll(copy, copy.NeedsBaseline, linked.Token).ConfigureAwait(false); copy.Status = "연결됨 · " + DateTime.Now.ToString("HH:mm") + " 확인"; }
                        catch (OperationCanceledException) { if (linked.Token.IsCancellationRequested) throw; copy.Status = "서버 응답이 늦어지고 있어요. 다음 주기에 다시 확인할게요."; }
                        catch (Exception error) { copy.Status = MailUtil.Error(error); }
                        lock (gate)
                        {
                            int index = state.Accounts.FindIndex(a => a.Id == copy.Id); if (index < 0 || !state.Accounts[index].Enabled || state.Accounts[index].Revision != copy.Revision) continue;
                            state.Accounts[index] = copy; Save();
                            if (messages != null && messages.Count > 0) notify(MailUtil.Notification(copy, messages, state.ShowDetails));
                        }
                        Change();
                    }
                }
                finally { network.Release(); }
            }
        }
        public async Task Connect(string provider, string setting, string address, string password, string host, int port, bool startTls, Action<string, string> login, CancellationToken cancel)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, stop.Token))
            {
                await network.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    if (LoadError != null) throw new MailConnectionException(LoadError);
                    MailAccount account;
                    if (provider == "gmail") account = await providers.ConnectGoogle(setting, login, linked.Token).ConfigureAwait(false);
                    else if (provider == "microsoft") account = await providers.ConnectMicrosoft(setting.Trim(), login, linked.Token).ConfigureAwait(false);
                    else
                    {
                        if (String.IsNullOrWhiteSpace(address) || String.IsNullOrEmpty(password)) throw new MailConnectionException("이메일 주소와 앱 비밀번호를 입력해 주세요.");
                        account = new MailAccount { Provider = provider, Address = address.Trim(), Password = password, Host = host.Trim(), Port = port, StartTls = startTls };
                        await providers.Poll(account, true, linked.Token).ConfigureAwait(false);
                    }
                    lock (gate) { state.Accounts.RemoveAll(a => a.Provider == account.Provider && String.Equals(a.Address, account.Address, StringComparison.OrdinalIgnoreCase)); state.Accounts.Add(account); Save(); }
                    Change();
                }
                finally { network.Release(); }
            }
        }
        public void Remove(string id) { lock (gate) { state.Accounts.RemoveAll(a => a.Id == id); Save(); } Change(); }
        public void Enable(string id, bool enabled) { lock (gate) { var a = state.Accounts.FirstOrDefault(v => v.Id == id); if (a != null) { a.Enabled = enabled; a.Revision++; if (enabled) a.NeedsBaseline = true; Save(); } } Change(); }
        public void Details(bool value) { lock (gate) { state.ShowDetails = value; Save(); } Change(); }
        public void Dispose() { stop.Cancel(); }
    }
}
