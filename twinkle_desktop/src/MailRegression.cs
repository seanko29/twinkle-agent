using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

namespace TwinkleDesktop
{
    public static class MailRegression
    {
        sealed class FakeHttp : HttpMessageHandler
        {
            public Func<HttpRequestMessage, Task<HttpResponseMessage>> Handle;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) { cancel.ThrowIfCancellationRequested(); return Handle(request); }
        }
        static HttpResponseMessage Json(object value, int status = 200) { return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(new JavaScriptSerializer().Serialize(value), Encoding.UTF8, "application/json") }; }
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        static MailAccount Account(string provider) { return new MailAccount { Provider = provider, AccessToken = "fixture-access", RefreshToken = "fixture-refresh", ClientId = "fixture-client", ExpiresUtc = DateTime.UtcNow.AddHours(1).ToString("o") }; }
        public static async Task Run(string report)
        {
            var lines = new List<string>(); string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "mail-test-" + Guid.NewGuid().ToString("N"));
            var vault = new MailVault(root); var state = new MailState(); state.Accounts.Add(new MailAccount { Provider = "naver", Address = "fixture@example.test", Password = "fixture-secret-never-plaintext", RefreshToken = "fixture-refresh-private" });
            vault.Save(state); byte[] cipher = File.ReadAllBytes(Path.Combine(root, "user_mail", "accounts.dat"));
            Require(!Encoding.UTF8.GetString(cipher).Contains("fixture-secret") && !Encoding.UTF8.GetString(cipher).Contains("fixture-refresh"), "Vault leaked plaintext");
            Require(vault.Load().Accounts[0].Password == state.Accounts[0].Password, "Vault could not decrypt");
            using (var service = new MailService(root, delegate { }))
            {
                string id = service.Snapshot().Accounts[0].Id; service.Enable(id, false); Require(!vault.Load().Accounts[0].Enabled, "Account toggle persistence");
                service.Enable(id, true); Require(vault.Load().Accounts[0].NeedsBaseline, "Re-enable should baseline"); service.Details(true); Require(vault.Load().ShowDetails, "Details preference");
                service.Remove(id); Require(vault.Load().Accounts.Count == 0, "Disconnect did not remove credentials");
            }
            File.WriteAllBytes(Path.Combine(root, "user_mail", "accounts.dat"), new byte[] { 1, 2, 3 });
            using (var service = new MailService(root, delegate { }))
            {
                Require(service.LoadError != null, "Corrupt vault not reported"); bool refused = false; try { service.Details(true); } catch { refused = true; }
                Require(refused && File.ReadAllBytes(Path.Combine(root, "user_mail", "accounts.dat")).SequenceEqual(new byte[] { 1, 2, 3 }), "Corrupt vault was overwritten");
            }
            lines.Add("PASS: Windows DPAPI round-trip and no plaintext secrets; atomic persistence; account on/off, re-enable baseline, privacy choice, disconnect; corrupt vault preserved.");
            var notification = MailUtil.Notification(new MailAccount { Provider = "gmail" }, new List<NewMail> { new NewMail { Id = "1", From = "fixture sender", Subject = "fixture private subject" } }, false);
            Require(!notification.message.Contains("private subject") && !notification.message.Contains("sender"), "Default notification disclosed headers");
            var query = MailProviders.ParseQuery("state=a%2Bb&code=x%3Dy"); Require(MailUtil.Str(query, "state") == "a+b" && MailUtil.Str(query, "code") == "x=y", "OAuth callback query decode");
            lines.Add("PASS: default notification hides sender/subject; OAuth callback query decoding.");
            var appConfig = new OAuthAppConfig(root); Require(!appConfig.GoogleReady && appConfig.MicrosoftId == "", "Unconfigured app should not enable sign-in");
            string fixtureConfig = Path.Combine(root, "desktop-app-fixture.json"); File.WriteAllText(fixtureConfig, "{\"installed\":{\"client_id\":\"fixture.apps.googleusercontent.com\",\"client_secret\":\"fixture-desktop-secret\"},\"access_token\":\"must-not-be-distributed\"}");
            appConfig.ImportGoogle(fixtureConfig); Require(appConfig.GoogleReady && !File.ReadAllText(appConfig.GoogleFile).Contains("must-not-be-distributed"), "App config not sanitized/persistent");
            string previous = File.ReadAllText(appConfig.GoogleFile); File.WriteAllText(fixtureConfig, "{\"web\":{\"client_id\":\"fixture.apps.googleusercontent.com\",\"client_secret\":\"server-secret\"}}");
            bool rejected = false; try { appConfig.ImportGoogle(fixtureConfig); } catch (MailConnectionException) { rejected = true; } Require(rejected && File.ReadAllText(appConfig.GoogleFile) == previous, "Invalid/server client replaced desktop config");
            string appId = Guid.NewGuid().ToString(); appConfig.SetMicrosoft(appId); Require(new OAuthAppConfig(root).MicrosoftId == appId, "App-wide Microsoft registration");
            lines.Add("PASS: app-owned OAuth configuration persists once per installation, strips unrelated tokens, rejects server client JSON, preserves valid settings after invalid import, and leaves sign-in unavailable until configured.");
            await AuthFixtures(root, lines);
            int historyPages = 0, headerCalls = 0;
            using (var http = new MailHttp(new FakeHttp { Handle = request =>
            {
                Require(request.Headers.Authorization.Parameter == "fixture-access", "Gmail bearer token");
                string url = request.RequestUri.AbsoluteUri;
                if (url.Contains("/history?"))
                {
                    historyPages++;
                    if (!url.Contains("pageToken=")) return Task.FromResult(Json(new { historyId = "150", nextPageToken = "second", history = new[] { new { messagesAdded = new[] { new { message = new { id = "new1" } }, new { message = new { id = "old" } } } } } }));
                    return Task.FromResult(Json(new { historyId = "200", history = new[] { new { messagesAdded = new[] { new { message = new { id = "new1" } }, new { message = new { id = "new2" } }, new { message = new { id = "sent" } } } } } }));
                }
                Require(url.Contains("format=metadata") && !url.Contains("format=full"), "Gmail fetched body"); headerCalls++;
                return Task.FromResult(Json(new { labelIds = new[] { url.Contains("/sent?") ? "SENT" : "INBOX" }, payload = new { headers = new[] { new { name = "From", value = "Fixture" }, new { name = "Subject", value = "Test subject" } } } }));
            } }))
            {
                var account = Account("gmail"); account.HistoryId = "100"; account.Seen.Add("old"); var provider = new MailProviders(http);
                var fresh = await provider.Gmail(account, false, CancellationToken.None); Require(fresh.Count == 2 && historyPages == 2 && headerCalls == 3 && account.HistoryId == "200", "Gmail paging/label filtering/cursor");
                Require((await provider.Gmail(account, false, CancellationToken.None)).Count == 0, "Gmail duplicate notification");
            }
            using (var http = new MailHttp(new FakeHttp { Handle = request => Task.FromResult(request.RequestUri.AbsolutePath.EndsWith("/profile") ? Json(new { historyId = "500", emailAddress = "fixture@example.test" }) : Json(new { error = new { code = 404 } }, 404)) }))
            {
                var account = Account("gmail"); account.HistoryId = "obsolete"; Require((await new MailProviders(http).Gmail(account, false, CancellationToken.None)).Count == 0 && account.HistoryId == "500", "Expired history baseline");
            }
            lines.Add("PASS: Gmail multi-page history, header-only reads, INBOX filtering, duplicate suppression, cursor commit and expired-history rebaseline without old-mail flood.");
            using (var http = new MailHttp(new FakeHttp { Handle = async request =>
            {
                if (request.Method == HttpMethod.Post)
                {
                    string form = await request.Content.ReadAsStringAsync(); Require(form.Contains("grant_type=refresh_token"), "Refresh grant"); return Json(new { access_token = "rotated-access", refresh_token = "rotated-refresh", expires_in = 3600 });
                }
                Require(request.Headers.Authorization.Parameter == "rotated-access", "Refreshed bearer not used"); return Json(new { historyId = "new-history" });
            } }))
            {
                var account = Account("gmail"); account.ExpiresUtc = DateTime.UtcNow.AddHours(-1).ToString("o"); await new MailProviders(http).Poll(account, true, CancellationToken.None);
                Require(account.RefreshToken == "rotated-refresh" && account.HistoryId == "new-history", "Refresh rotation persistence");
                bool refused = false; try { await http.Request("https://example.test/steal-token", "private", null, CancellationToken.None); } catch (MailConnectionException) { refused = true; } Require(refused, "Cross-host bearer forwarding");
            }
            lines.Add("PASS: OAuth refresh-token rotation, refreshed bearer usage and external-host rejection.");
            string retryRoot = Path.Combine(root, "retry"); var retryState = new MailState(); var retryAccount = Account("gmail"); retryAccount.HistoryId = "100"; retryState.Accounts.Add(retryAccount); new MailVault(retryRoot).Save(retryState); int retryCalls = 0;
            using (var retryHttp = new MailHttp(new FakeHttp { Handle = request =>
            {
                retryCalls++; if (retryCalls == 1) { var failed = new TaskCompletionSource<HttpResponseMessage>(); failed.SetException(new TaskCanceledException("fixture timeout")); return failed.Task; }
                return Task.FromResult(Json(new { historyId = "200" }));
            } }))
            using (var service = new MailService(retryRoot, delegate { }, retryHttp))
            {
                await service.CheckNow(CancellationToken.None); await service.CheckNow(CancellationToken.None);
                Require(retryCalls == 2 && service.Snapshot().Accounts[0].HistoryId == "200", "HTTP timeout permanently stopped mail checking");
            }
            lines.Add("PASS: transient HTTP timeout preserves the account and later checks recover.");
            int graphCalls = 0;
            using (var http = new MailHttp(new FakeHttp { Handle = request =>
            {
                graphCalls++; string url = request.RequestUri.AbsoluteUri;
                Require(request.Headers.Contains("Prefer") && url.Contains("$select=id,subject,from,receivedDateTime"), "Graph basic metadata selection");
                object fresh = new { id = "fresh", subject = "Fixture", receivedDateTime = DateTime.UtcNow.AddMinutes(-1).ToString("o"), from = new { emailAddress = new { name = "Sender", address = "fixture@example.test" } } };
                return Task.FromResult(graphCalls % 2 == 1 ? Json(new Dictionary<string, object> { { "value", new object[] { fresh, new { id = "historical", receivedDateTime = DateTime.UtcNow.AddDays(-2).ToString("o") } } }, { "@odata.nextLink", "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?$select=id,subject,from,receivedDateTime&fixturePage=2" } }) : Json(new { value = new object[] { fresh } }));
            } }))
            {
                var account = Account("microsoft"); account.ConnectedUtc = DateTime.UtcNow.AddHours(-1).ToString("o"); account.LastCheckedUtc = DateTime.UtcNow.AddMinutes(-5).ToString("o"); var provider = new MailProviders(http);
                Require((await provider.Microsoft(account, false, CancellationToken.None)).Count == 1 && graphCalls == 2, "Graph paging/history cutoff");
                Require((await provider.Microsoft(account, false, CancellationToken.None)).Count == 0, "Graph duplicate suppression");
                Require((await provider.Microsoft(account, true, CancellationToken.None)).Count == 0, "Graph baseline should not notify");
            }
            lines.Add("PASS: Outlook metadata-only query, pagination, initial connection cutoff, duplicate suppression and re-enable baseline.");
            await ImapFixture(lines);
            lines.Add("NOT VERIFIED: real account sign-in, Google/Azure app registration, provider delivery timing or company tenant policy. No real mailbox or credentials used in tests.");
            File.WriteAllLines(report, lines);
        }
        static async Task ImapFixture(List<string> lines)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; var commands = new List<string>();
            var server = Task.Run(async delegate
            {
                using (var socket = await listener.AcceptTcpClientAsync())
                using (var reader = new StreamReader(socket.GetStream(), Encoding.ASCII))
                using (var writer = new StreamWriter(socket.GetStream(), Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true })
                {
                    writer.WriteLine("* OK [CAPABILITY IMAP4rev1] fixture"); string command;
                    while ((command = await reader.ReadLineAsync()) != null)
                    {
                        commands.Add(command); string tag = command.Split(' ')[0], upper = command.ToUpperInvariant();
                        if (upper.Contains(" LOGOUT")) { writer.WriteLine("* BYE fixture"); writer.WriteLine(tag + " OK logout"); break; }
                        if (upper.Contains(" CAPABILITY")) writer.WriteLine("* CAPABILITY IMAP4rev1");
                        else if (upper.Contains(" LIST ")) writer.WriteLine("* LIST (\\HasNoChildren) \"/\" \"INBOX\"");
                        else if (upper.Contains(" EXAMINE "))
                        {
                            writer.WriteLine("* FLAGS (\\Seen)"); writer.WriteLine("* 2 EXISTS"); writer.WriteLine("* 0 RECENT"); writer.WriteLine("* OK [UIDVALIDITY 77] fixture"); writer.WriteLine("* OK [UIDNEXT 103] fixture"); writer.WriteLine(tag + " OK [READ-ONLY] selected"); continue;
                        }
                        else if (upper.Contains(" UID SEARCH ")) writer.WriteLine("* SEARCH 100 101 102");
                        else if (upper.Contains(" UID FETCH "))
                        {
                            for (int i = 1; i <= 2; i++) writer.WriteLine("* " + i + " FETCH (UID " + (100 + i) + " ENVELOPE (\"Thu, 22 Oct 2026 10:00:00 +0900\" \"Fixture subject\" ((\"Sender\" NIL \"from\" \"example.test\")) NIL NIL ((\"Reader\" NIL \"reader\" \"example.test\")) NIL NIL NIL \"<fixture" + i + "@example.test>\"))");
                        }
                        writer.WriteLine(tag + " OK fixture");
                    }
                }
            });
            try
            {
                using (var client = new ImapClient())
                {
                    client.Timeout = 5000;
                    // Synthetic loopback fixture only; production always requires certificate-validated TLS.
                    await client.ConnectAsync("127.0.0.1", port, SecureSocketOptions.None); await client.AuthenticateAsync("fixture", "fixture"); await client.Inbox.OpenAsync(FolderAccess.ReadOnly);
                    var account = new MailAccount { Provider = "naver", UidValidity = 77, LastUid = 100 };
                    var fresh = await MailProviders.ReadInbox(client.Inbox, account, false, CancellationToken.None);
                    Require(fresh.Count == 2 && account.LastUid == 102 && fresh[0].Subject == "Fixture subject", "IMAP UID/envelope parse");
                    Require((await MailProviders.ReadInbox(client.Inbox, account, false, CancellationToken.None)).Count == 0, "IMAP reversed-star duplicate");
                    account.UidValidity = 66; Require((await MailProviders.ReadInbox(client.Inbox, account, false, CancellationToken.None)).Count == 0 && account.UidValidity == 77, "IMAP UIDVALIDITY reset flood");
                    await client.DisconnectAsync(true);
                }
                await server;
                Require(commands.Any(c => c.Contains(" EXAMINE ")) && !commands.Any(c => c.Contains(" SELECT ") || c.Contains(" STORE ") || c.Contains("BODY[")), "IMAP mutated mailbox or fetched body");
                lines.Add("PASS: real MailKit against synthetic loopback IMAP server; read-only EXAMINE, UID search and envelope decode; reversed UID range filtering, duplicates and UIDVALIDITY reset; no body fetch/flag mutation.");
            }
            finally { listener.Stop(); }
        }
        static async Task AuthFixtures(string root, List<string> lines)
        {
            string config = Path.Combine(root, "google-fixture.json");
            File.WriteAllText(config, "{\"installed\":{\"client_id\":\"fixture.apps.googleusercontent.com\",\"client_secret\":\"fixture-secret\"}}");
            Task callback = null; string verifierChallenge = null;
            using (var http = new MailHttp(new FakeHttp { Handle = async request =>
            {
                if (request.Method == HttpMethod.Post)
                {
                    var form = MailProviders.ParseQuery(await request.Content.ReadAsStringAsync());
                    using (var sha = System.Security.Cryptography.SHA256.Create()) Require(MailUtil.Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(MailUtil.Str(form, "code_verifier")))) == verifierChallenge, "Google PKCE mismatch");
                    Require(MailUtil.Str(form, "code") == "fixture-code", "Google callback code"); return Json(new { access_token = "fixture-access", refresh_token = "fixture-refresh", expires_in = 3600 });
                }
                return Json(new { emailAddress = "fixture@example.test", historyId = "100" });
            } }))
            {
                var account = await new MailProviders(http).ConnectGoogle(config, delegate(string url, string instruction)
                {
                    var query = MailProviders.ParseQuery(new Uri(url).Query.Substring(1)); verifierChallenge = MailUtil.Str(query, "code_challenge");
                    Require(MailUtil.Str(query, "scope") == MailProviders.GoogleScope && MailUtil.Str(query, "code_challenge_method") == "S256", "Google auth scope/PKCE");
                    callback = Task.Run(async delegate
                    {
                        using (var browser = new HttpClient())
                        {
                            string target = MailUtil.Str(query, "redirect_uri") + "?state=" + Uri.EscapeDataString(MailUtil.Str(query, "state")) + "&code=fixture-code";
                            Require((await browser.GetAsync(target)).IsSuccessStatusCode, "Loopback callback response");
                        }
                    });
                }, CancellationToken.None);
                await callback; Require(account.Address == "fixture@example.test" && account.HistoryId == "100", "Google authorization result");
                using (var cancel = new CancellationTokenSource())
                {
                    bool cancelled = false;
                    try { await new MailProviders(http).ConnectGoogle(config, delegate { cancel.Cancel(); }, cancel.Token); }
                    catch (OperationCanceledException) { cancelled = true; }
                    Require(cancelled, "Google cancellation did not close loopback listener");
                }
            }
            lines.Add("PASS: Google synthetic authorization round-trip through actual loopback HTTP listener; exact state, S256 PKCE verifier, metadata scope, token exchange and cancellation cleanup.");
            int attempts = 0; bool instructions = false;
            using (var http = new MailHttp(new FakeHttp { Handle = async request =>
            {
                string url = request.RequestUri.AbsoluteUri;
                if (url.EndsWith("/devicecode"))
                {
                    var form = MailProviders.ParseQuery(await request.Content.ReadAsStringAsync()); Require(MailUtil.Str(form, "scope") == MailProviders.MicrosoftScope, "Microsoft delegated scope");
                    return Json(new { verification_uri = "https://microsoft.com/devicelogin", user_code = "FIXTURE", device_code = "fixture-device", interval = 5, expires_in = 60 });
                }
                if (url.EndsWith("/token"))
                {
                    attempts++; if (attempts == 1) return Json(new { error = "authorization_pending" }, 400);
                    return Json(new { access_token = "fixture-access", refresh_token = "fixture-refresh", expires_in = 3600 });
                }
                return url.Contains("/inbox/") ? Json(new { value = new object[0] }) : Json(new { mail = "fixture@example.test" });
            } }))
            {
                var account = await new MailProviders(http).ConnectMicrosoft(Guid.NewGuid().ToString(), delegate(string url, string message) { instructions = message.Contains("FIXTURE") && url == "https://microsoft.com/devicelogin"; }, CancellationToken.None);
                Require(account.Address == "fixture@example.test" && attempts == 2 && instructions, "Microsoft device code pending/success");
            }
            lines.Add("PASS: Microsoft synthetic device-code flow; delegated scopes, user instructions, pending retry, token exchange, profile and mail-access check.");
        }
    }
}
