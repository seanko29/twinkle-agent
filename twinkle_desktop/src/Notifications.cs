using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace TwinkleDesktop
{
    // A local completion signal only. No provider keys, account login or network listener.
    public sealed class PetNotification
    {
        public string source { get; set; }
        public string title { get; set; }
        public string message { get; set; }
        public void Validate()
        {
            source = Clean(source, 40); title = Clean(title, 100); message = Clean(message, 600);
            if (String.IsNullOrWhiteSpace(title)) throw new InvalidDataException("알림 제목이 필요합니다.");
            if (String.IsNullOrWhiteSpace(source)) source = "외부 작업";
            if (String.IsNullOrWhiteSpace(message)) message = title;
        }
        static string Clean(string value, int maximum)
        {
            var result = new StringBuilder();
            foreach (char c in value ?? "") if (!Char.IsControl(c) || c == '\n') result.Append(c);
            string text = result.ToString().Trim(); return text.Length <= maximum ? text : text.Substring(0, maximum);
        }
        public static PetNotification FromFile(string path)
        {
            if (new FileInfo(path).Length > 16384) throw new InvalidDataException("알림 파일은 16KB 이하여야 합니다.");
            var item = new JavaScriptSerializer().Deserialize<PetNotification>(File.ReadAllText(path));
            if (item == null) throw new InvalidDataException("알림 형식이 올바르지 않습니다."); item.Validate(); return item;
        }
    }

    public sealed class NotificationBridge : IDisposable
    {
        const string PipeName = "TwinkleDesktopNotifications_20261022";
        readonly object gate = new object();
        readonly Action<PetNotification> receive;
        NamedPipeServerStream active;
        volatile bool stopped;

        public NotificationBridge(Action<PetNotification> receive)
        {
            this.receive = receive;
            var thread = new Thread(Listen) { IsBackground = true, Name = "Twinkle completion notifications" }; thread.Start();
        }
        void Listen()
        {
            var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            while (!stopped)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security))
                    {
                        lock (gate) { if (stopped) return; active = pipe; }
                        pipe.WaitForConnection();
                        var timeout = new Timer(delegate { try { pipe.Dispose(); } catch { } }, null, 3000, Timeout.Infinite);
                        try
                        {
                            using (var reader = new BinaryReader(pipe, Encoding.UTF8))
                            {
                                int length = reader.ReadInt32(); if (length < 2 || length > 16384) throw new InvalidDataException();
                                byte[] bytes = reader.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException();
                                var item = new JavaScriptSerializer().Deserialize<PetNotification>(Encoding.UTF8.GetString(bytes));
                                if (item == null) throw new InvalidDataException(); item.Validate(); if (!stopped) receive(item);
                            }
                        }
                        finally { timeout.Dispose(); }
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                catch (ArgumentException) { }
                catch (UnauthorizedAccessException) { }
                finally { lock (gate) active = null; }
                if (!stopped) Thread.Sleep(30);
            }
        }
        public static void Send(PetNotification item)
        {
            item.Validate(); byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(item));
            using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
            {
                try { client.Connect(2500); }
                catch (TimeoutException) { throw new IOException("캐릭터를 먼저 실행한 뒤 다시 알림을 보내 주세요."); }
                using (var writer = new BinaryWriter(client, Encoding.UTF8)) { writer.Write(bytes.Length); writer.Write(bytes); writer.Flush(); }
            }
        }
        public void Dispose()
        {
            stopped = true; lock (gate) { if (active != null) active.Dispose(); }
        }
    }
}
