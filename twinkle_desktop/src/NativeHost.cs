using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TwinkleDesktop
{
    public static class NativeHost
    {
        public static int Main(string[] args)
        {
            object result;
            try
            {
                var input = new BinaryReader(Console.OpenStandardInput(), Encoding.UTF8);
                int length = input.ReadInt32(); if (length < 2 || length > 16384) throw new InvalidDataException("메시지 크기가 올바르지 않습니다.");
                byte[] bytes = input.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException();
                var item = new JavaScriptSerializer().Deserialize<PetNotification>(Encoding.UTF8.GetString(bytes));
                if (item == null) throw new InvalidDataException(); NotificationBridge.Send(item);
                result = new { ok = true, message = "캐릭터에 알림을 보냈어요." };
            }
            catch (Exception error) { result = new { ok = false, message = error.Message }; }
            byte[] reply = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(result));
            using (var output = new BinaryWriter(Console.OpenStandardOutput(), Encoding.UTF8)) { output.Write(reply.Length); output.Write(reply); output.Flush(); }
            return 0;
        }
    }
}
