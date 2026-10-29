using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Maquim.OfficeInstaller
{
    internal sealed class LocationPipeHost : IDisposable
    {
        public const string Name = "MaquimComputerLocation-v1";
        readonly string root;
        Thread worker;
        volatile bool stopping;
        readonly object gate = new object();
        NamedPipeServerStream active;
        public LocationPipeHost(string root) { this.root = root; }
        public void Start() { worker = new Thread(Run) { IsBackground = true, Name = "MaquimLocationBridge" }; worker.Start(); }
        void Run() {
            while (!stopping) {
                try {
                    var security = new PipeSecurity();
                    security.SetAccessRuleProtection(true, false);
                    security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.ReadWrite, AccessControlType.Deny));
                    foreach (var sid in new[] { new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
                        security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
                    security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
                    using (var pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security)) {
                        lock (gate) { if (stopping) return; active = pipe; }
                        pipe.WaitForConnection();
                        var buffer = new byte[4097];
                        int count = 0;
                        // Cliente local: mensagem delimitada por newline, limite 4 KiB e 5 segundos totais.
                        var deadline = DateTime.UtcNow.AddSeconds(5);
                        while (count < buffer.Length) {
                            var read = pipe.BeginRead(buffer, count, buffer.Length - count, null, null);
                            using (read.AsyncWaitHandle) if (!read.AsyncWaitHandle.WaitOne(Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds))) break;
                            int n = pipe.EndRead(read); if (n == 0) break;
                            count += n; if (Array.IndexOf(buffer, (byte)10, 0, count) >= 0) break;
                        }
                        int end = Array.IndexOf(buffer, (byte)10, 0, count);
                        if (end < 0 || end > 4096) continue;
                        var value = new JavaScriptSerializer().Deserialize<LocationSnapshot>(Encoding.UTF8.GetString(buffer, 0, end));
                        value.Validate();
                        if (value.source != "windows" && value.source != "unavailable") continue;
                        if (value.source == "windows" && !LocationSnapshot.Fresh(value, DateTimeOffset.Now)) continue;
                        ComputerQueue.AtomicWrite(Path.Combine(root, "windows-location.json"), Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value)), true);
                        pipe.WriteByte(1);
                    }
                } catch { if (!stopping) Thread.Sleep(1000); }
                finally { lock (gate) active = null; }
            }
        }
        public void Dispose() { stopping = true; lock (gate) { if (active != null) active.Dispose(); } if (worker != null) worker.Join(6000); }
        internal static bool Publish(LocationSnapshot value) {
            value.Validate();
            using (var pipe = new NamedPipeClientStream(".", Name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification)) {
                pipe.Connect(2000);
                byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value) + "\n");
                if (bytes.Length > 4096) throw new InvalidDataException();
                var write = pipe.BeginWrite(bytes, 0, bytes.Length, null, null);
                using (write.AsyncWaitHandle) { if (!write.AsyncWaitHandle.WaitOne(3000)) return false; }
                pipe.EndWrite(write);
                byte[] ack = new byte[1]; var read = pipe.BeginRead(ack, 0, 1, null, null);
                using (read.AsyncWaitHandle) { if (!read.AsyncWaitHandle.WaitOne(3000)) return false; }
                return pipe.EndRead(read) == 1 && ack[0] == 1;
            }
        }
    }
}
