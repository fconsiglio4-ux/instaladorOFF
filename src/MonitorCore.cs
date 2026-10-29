using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

namespace Maquim.OfficeInstaller
{
    internal sealed class MonitorConfig
    {
        public string machine_id { get; set; }
        public string installation_id { get; set; }
        public string serial_number { get; set; }
        public string asset_tag { get; set; }
        public void Validate() {
            Guid a, b;
            if (!Guid.TryParse(machine_id, out a) || a == Guid.Empty || !Guid.TryParse(installation_id, out b) || b == Guid.Empty) throw new InvalidDataException("Identificação do agente inválida.");
            var input = new TechnicianInput { SerialNumber = serial_number, AssetTag = asset_tag }; input.Validate();
            if (input.SerialNumber != serial_number || input.AssetTag != asset_tag) throw new InvalidDataException("Identificação não normalizada.");
        }
    }
    internal sealed class DiskStatus
    {
        public string name { get; set; }
        public long total_bytes { get; set; }
        public long free_bytes { get; set; }
    }
    internal sealed class StatusReport
    {
        public string event_id { get; set; }
        public string machine_id { get; set; }
        public string installation_id { get; set; }
        public long sequence_number { get; set; }
        public string observed_at { get; set; }
        public string computer_name { get; set; }
        public string serial_number { get; set; }
        public string asset_tag { get; set; }
        public string agent_version { get; set; }
        public string[] ip_addresses { get; set; }
        public DiskStatus[] disks { get; set; }
        public LocationSnapshot location { get; set; }
        public void Validate() {
            if (location != null) location.Validate();
            new MonitorConfig { machine_id = machine_id, installation_id = installation_id, serial_number = serial_number, asset_tag = asset_tag }.Validate();
            Guid id; DateTimeOffset time;
            if (!Guid.TryParse(event_id, out id) || id == Guid.Empty || sequence_number < 1 || sequence_number > 9007199254740991L) throw new InvalidDataException("Evento inválido.");
            if (!DateTimeOffset.TryParseExact(observed_at, "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out time)) throw new InvalidDataException("Data inválida.");
            TechnicianInput.Required(computer_name, 255, "Computador"); TechnicianInput.Required(agent_version, 100, "Versão do agente");
            if (ip_addresses == null || ip_addresses.Length > 32 || !ip_addresses.SequenceEqual(NormalizeIPs(ip_addresses))) throw new InvalidDataException("IPs inválidos.");
            if (disks == null || disks.Length == 0 || disks.Length > 32 || disks.Select(d => d.name).Distinct().Count() != disks.Length) throw new InvalidDataException("Discos não confirmados.");
            foreach (var d in disks) if (String.IsNullOrWhiteSpace(d.name) || d.name.Length > 100 || d.total_bytes < 1 || d.total_bytes > 9007199254740991L || d.free_bytes < 0 || d.free_bytes > d.total_bytes) throw new InvalidDataException("Espaço de disco inválido.");
        }
        internal static string[] NormalizeIPs(IEnumerable<string> ips) {
            return ips.Select(v => { IPAddress ip; if (!IPAddress.TryParse(v, out ip) || ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip) || v.StartsWith("169.254.") || v == "0.0.0.0") throw new InvalidDataException("IPv4 inválido."); return ip.ToString(); }).Distinct().OrderBy(v => v, StringComparer.Ordinal).ToArray();
        }
        public byte[] Bytes() { Validate(); byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(this)); if (bytes.Length > 16384) throw new InvalidDataException("Status excede 16 KiB."); return bytes; }
    }
    internal sealed class MonitorState
    {
        public long sequence_number { get; set; }
        public string observed_at { get; set; }
        public string[] ip_addresses { get; set; }
        public string location_source { get; set; }
        public string agent_version { get; set; }
    }
    internal static class MonitorCollector
    {
        public static string[] IPs() {
            return StatusReport.NormalizeIPs(NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip) && !ip.ToString().StartsWith("169.254.") && ip.ToString() != "0.0.0.0").Select(ip => ip.ToString()));
        }
        public static DiskStatus[] Disks() {
            // Uma falha de leitura nao vira espaco livre zero presumido.
            return DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => new DiskStatus { name = d.Name, total_bytes = d.TotalSize, free_bytes = d.AvailableFreeSpace }).OrderBy(d => d.name, StringComparer.Ordinal).ToArray();
        }
    }
    internal sealed class MonitorStore
    {
        public static string DefaultRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Maquim", "ComputerMonitor"); } }
        public readonly string Root;
        public string QueueDirectory { get { return Path.Combine(Root, "queue"); } }
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        public MonitorStore(string root = null) { Root = root ?? DefaultRoot; }
        public void Prepare() {
            var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null) })
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            if (Directory.Exists(Root)) Directory.SetAccessControl(Root, security); else Directory.CreateDirectory(Root, security);
            Directory.CreateDirectory(QueueDirectory);
        }
        public MonitorConfig Config() { var c = Json.Deserialize<MonitorConfig>(File.ReadAllText(Path.Combine(Root, "config.json"))); c.Validate(); return c; }
        public void Configure(Guid installationId, TechnicianInput input, string token) {
            input.Validate(); Prepare();
            string config = Path.Combine(Root, "config.json");
            var c = new MonitorConfig { machine_id = File.Exists(config) ? Config().machine_id : Guid.NewGuid().ToString("D"), installation_id = installationId.ToString("D"), serial_number = input.SerialNumber, asset_tag = input.AssetTag }; c.Validate();
            ComputerQueue.AtomicWrite(config, Encoding.UTF8.GetBytes(Json.Serialize(c)), true);
            if (!String.IsNullOrEmpty(token)) SaveToken(token);
        }
        public void SaveToken(string token) {
            token = (token ?? "").Trim();
            if (token.Length < 1 || token.Length > 1017 || token.Any(Char.IsControl)) throw new InvalidDataException("Token inválido.");
            ComputerQueue.AtomicWrite(Path.Combine(Root, "token.dpapi"), ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.LocalMachine), true);
            string paused = Path.Combine(Root, "auth-paused"); if (File.Exists(paused)) File.Delete(paused);
        }
        public string Token() { string p = Path.Combine(Root, "token.dpapi"); return File.Exists(p) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(p), null, DataProtectionScope.LocalMachine)) : ""; }
        public MonitorState State() {
            string path = Path.Combine(Root, "state.json");
            var state = File.Exists(path) ? Json.Deserialize<MonitorState>(File.ReadAllText(path)) : null; bool recovered = false;
            // Recupera o intervalo entre enqueue e state em caso de queda/reinicio.
            foreach (var file in Directory.GetFiles(QueueDirectory, "*.json").OrderByDescending(p => p, StringComparer.Ordinal)) {
                var r = Json.Deserialize<StatusReport>(File.ReadAllText(file)); r.Validate();
                if (state == null || r.sequence_number > state.sequence_number) { state = new MonitorState { sequence_number = r.sequence_number, observed_at = r.observed_at, ip_addresses = r.ip_addresses, location_source = r.location == null ? null : r.location.source, agent_version = r.agent_version }; recovered = true; }
                break;
            }
            if (recovered) ComputerQueue.AtomicWrite(path, Encoding.UTF8.GetBytes(Json.Serialize(state)), true);
            return state;
        }
        public void Enqueue(StatusReport r) {
            byte[] bytes = r.Bytes();
            string path = Path.Combine(QueueDirectory, r.sequence_number.ToString("D16", CultureInfo.InvariantCulture) + "-" + r.event_id + ".json");
            ComputerQueue.AtomicWrite(path, bytes, false);
            ComputerQueue.AtomicWrite(Path.Combine(Root, "state.json"), Encoding.UTF8.GetBytes(Json.Serialize(new MonitorState { sequence_number = r.sequence_number, observed_at = r.observed_at, ip_addresses = r.ip_addresses, location_source = r.location == null ? null : r.location.source, agent_version = r.agent_version })), true);
        }
        public void Note(string text) { ComputerQueue.AtomicWrite(Path.Combine(Root, "status.txt"), Encoding.UTF8.GetBytes(DateTimeOffset.Now.ToString("o") + " " + text), true); }
        internal static bool Ack(HttpOutcome response, string id) {
            if (response == null || (response.Status != 200 && response.Status != 201)) return false;
            try { var data = Json.DeserializeObject(response.Body) as Dictionary<string, object>; return data != null && data.ContainsKey("ok") && data["ok"] is bool && (bool)data["ok"] && data.ContainsKey("event_id") && data["event_id"] is string && String.Equals((string)data["event_id"], id, StringComparison.OrdinalIgnoreCase); } catch { return false; }
        }
        public void Retry(Func<byte[], string, HttpOutcome> send) {
            string token = Token();
            if (String.IsNullOrEmpty(token) || File.Exists(Path.Combine(Root, "auth-paused"))) { Note("Envio pendente: configure o token no instalador."); return; }
            foreach (var path in Directory.GetFiles(QueueDirectory, "*.json").OrderBy(p => p, StringComparer.Ordinal).Where(p => !File.Exists(p + ".blocked")).Take(20)) {
                var bytes = File.ReadAllBytes(path); StatusReport r;
                try { if (bytes.Length > 16384) throw new InvalidDataException(); r = Json.Deserialize<StatusReport>(Encoding.UTF8.GetString(bytes)); r.Validate(); }
                catch { ComputerQueue.AtomicWrite(path + ".blocked", Encoding.UTF8.GetBytes("Registro local inválido."), true); continue; }
                HttpOutcome result;
                try { result = send(bytes, token); } catch { Note("Status pendente: sem conexão ou timeout."); return; }
                if (Ack(result, r.event_id)) { File.Delete(path); Note("Status confirmado pelo Roteiro Maquim."); continue; }
                if (result != null && result.Status == 401) { if (Token() == token) ComputerQueue.AtomicWrite(Path.Combine(Root, "auth-paused"), Encoding.UTF8.GetBytes("401"), true); Note("Token rejeitado (401). Corrija em Configurar integração."); return; }
                if (result != null && new[] {400, 413, 415, 422}.Contains(result.Status)) { ComputerQueue.AtomicWrite(path + ".blocked", Encoding.UTF8.GetBytes("Formato/tamanho rejeitado: " + result.Status), true); Note("Registro bloqueado por formato/tamanho; revisão necessária."); continue; }
                Note("Status pendente: confirmação ausente ou erro temporário."); return;
            }
        }
    }
    internal sealed class MonitorEngine
    {
        readonly MonitorStore store;
        DateTimeOffset retryAt = DateTimeOffset.MinValue;
        public MonitorEngine(MonitorStore store) { this.store = store; }
        internal static bool Due(MonitorState state, string[] ips, DateTimeOffset now) {
            if (state == null || state.agent_version != Release.Version) return true;
            DateTimeOffset previous = DateTimeOffset.Parse(state.observed_at, CultureInfo.InvariantCulture);
            return now >= previous.AddHours(12) || now < previous.AddMinutes(-5) || !ips.SequenceEqual(state.ip_addresses ?? new string[0]);
        }
        public void Tick(DateTimeOffset now, Func<string[]> getIPs, Func<DiskStatus[]> getDisks, Func<byte[], string, HttpOutcome> send, Func<DateTimeOffset, string[], LocationSnapshot> getLocation = null) {
            var c = store.Config(); var state = store.State(); var ips = StatusReport.NormalizeIPs(getIPs());
            bool captured = false;
            bool locationChanged = false;
            if (getLocation != null && state != null) {
                bool windowsFresh = false;
                try { string p = Path.Combine(store.Root, "windows-location.json"); if (File.Exists(p)) { var v = new JavaScriptSerializer().Deserialize<LocationSnapshot>(File.ReadAllText(p)); windowsFresh = v.source == "windows" && LocationSnapshot.Fresh(v, now); } } catch { }
                locationChanged = windowsFresh != (state.location_source == "windows");
            }
            if ((Due(state, ips, now) || locationChanged) && Directory.GetFiles(store.QueueDirectory, "*.json").Length < 1000) {
                var r = new StatusReport { event_id = Guid.NewGuid().ToString("D"), machine_id = c.machine_id, installation_id = c.installation_id, sequence_number = state == null ? 1 : checked(state.sequence_number + 1), observed_at = now.ToString("o"), computer_name = Environment.MachineName, serial_number = c.serial_number, asset_tag = c.asset_tag, agent_version = Release.Version, ip_addresses = ips, disks = getDisks() };
                if (getLocation != null) {
                    try { r.location = getLocation(now, ips); if (r.location != null) r.location.Validate(); }
                    catch { r.location = LocationSnapshot.Unavailable("error"); }
                }
                store.Enqueue(r); captured = true;
            }
            if (captured || now >= retryAt) { retryAt = now.AddMinutes(15); store.Retry(send); }
        }
    }
    internal static class MonitorTransport
    {
        public const string Endpoint = "https://roteiro-maquim.lovable.app/api/public/computer-status";
        internal static HttpWebRequest Request(int length, string token) {
            var request = (HttpWebRequest)WebRequest.Create(Endpoint); request.Method = "POST"; request.ContentType = "application/json";
            request.AllowAutoRedirect = false; request.Timeout = 30000; request.ReadWriteTimeout = 30000; request.UseDefaultCredentials = false;
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token; request.ContentLength = length; return request;
        }
        public static HttpOutcome Send(byte[] bytes, string token) {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var request = Request(bytes.Length, token);
            using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            HttpWebResponse response;
            try { response = (HttpWebResponse)request.GetResponse(); } catch (WebException ex) { response = ex.Response as HttpWebResponse; if (response == null) throw; }
            using (response) using (var stream = response.GetResponseStream()) using (var reader = new StreamReader(stream, Encoding.UTF8)) {
                char[] buffer = new char[16385]; int count = 0, read;
                while (count < buffer.Length && (read = reader.Read(buffer, count, buffer.Length - count)) > 0) count += read;
                return new HttpOutcome((int)response.StatusCode, count > 16384 ? "" : new string(buffer, 0, count));
            }
        }
    }
}
