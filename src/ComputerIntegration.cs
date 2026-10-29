using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Maquim.OfficeInstaller
{
    internal static class Release
    {
        public const string Version = "1.1.0";
    }

    internal sealed class TechnicianInput
    {
        public string SerialNumber = "";
        public string AssetTag = "";
        public void Validate()
        {
            SerialNumber = Required(SerialNumber, 255, "Número de série do computador");
            AssetTag = Required(AssetTag, 100, "Número de patrimônio");
        }
        internal static string Required(string value, int maximum, string name)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0) throw new InvalidOperationException("Preencha o campo " + name + ".");
            if (value.Length > maximum) throw new InvalidOperationException(name + ": máximo de " + maximum + " caracteres.");
            return value;
        }
    }

    // Estas sao as unicas propriedades serializadas no contrato publico.
    internal sealed class ComputerInstallation
    {
        public ComputerInstallation() { }
        public string installation_id { get; set; }
        public string installed_at { get; set; }
        public string computer_name { get; set; }
        public string os_version { get; set; }
        public string office_version { get; set; }
        public string serial_number { get; set; }
        public string asset_tag { get; set; }
        public string installer_version { get; set; }

        public void Validate()
        {
            Guid id;
            if (!Guid.TryParseExact(installation_id, "D", out id) || id == Guid.Empty) throw new InvalidDataException("UUID inválido.");
            DateTimeOffset time;
            if (String.IsNullOrEmpty(installed_at) || !Regex.IsMatch(installed_at, @"T.*(?:Z|[+-]\d{2}:\d{2})$") || !DateTimeOffset.TryParse(installed_at, CultureInfo.InvariantCulture, DateTimeStyles.None, out time)) throw new InvalidDataException("Data final sem fuso horário válido.");
            var input = new TechnicianInput { SerialNumber = serial_number, AssetTag = asset_tag }; input.Validate();
            if (input.SerialNumber != serial_number || input.AssetTag != asset_tag) throw new InvalidDataException("Identificação não normalizada.");
            TechnicianInput.Required(computer_name, 255, "Nome do computador");
            TechnicianInput.Required(os_version, 255, "Sistema operacional");
            TechnicianInput.Required(office_version, 255, "Office instalado");
            TechnicianInput.Required(installer_version, 100, "Versão do instalador");
        }
        public byte[] Bytes()
        {
            Validate();
            var bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(this));
            if (bytes.Length > 16 * 1024) throw new InvalidDataException("Registro excede 16 KiB.");
            return bytes;
        }
    }

    internal sealed class MachineSnapshot
    {
        public string ComputerName;
        public string OSVersion;
        public string OfficeVersion;
        public ComputerInstallation Record(Guid id, TechnicianInput input, DateTimeOffset finalTime)
        {
            input.Validate();
            var record = new ComputerInstallation { installation_id = id.ToString("D"), installed_at = finalTime.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture), computer_name = ComputerName, os_version = OSVersion, office_version = OfficeVersion, serial_number = input.SerialNumber, asset_tag = input.AssetTag, installer_version = Release.Version };
            record.Validate(); return record;
        }
    }

    internal static class MachineCollector
    {
        public static string OSDescription()
        {
            string caption = null, build = null, version = null;
            using (var search = new ManagementObjectSearcher("SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem"))
            using (var results = search.Get())
                foreach (ManagementObject os in results) using (os) { caption = Convert.ToString(os["Caption"]); build = Convert.ToString(os["BuildNumber"]); version = Convert.ToString(os["Version"]); break; }
            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
            using (var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                string display = key == null ? "" : Convert.ToString(key.GetValue("DisplayVersion", ""));
                string ubr = key == null ? "" : Convert.ToString(key.GetValue("UBR", ""));
                if (String.IsNullOrWhiteSpace(caption) || !Regex.IsMatch(build ?? "", @"^\d+$") || String.IsNullOrWhiteSpace(version)) throw new InvalidDataException("Não foi possível confirmar o Windows instalado.");
                return TechnicianInput.Required(caption + (display.Length > 0 ? " " + display : " " + version) + " (build " + build + (ubr.Length > 0 ? "." + ubr : "") + ")", 255, "Sistema operacional");
            }
        }

        internal static string OfficeDescription(string products, string build, string platform, string executableVersion, ushort executableMachine)
        {
            if (!(products ?? "").Split(',').Select(p => p.Trim()).Contains("O365ProPlusRetail")) throw new InvalidDataException("O365ProPlusRetail não foi confirmado após a instalação.");
            if (!Regex.IsMatch(build ?? "", @"^16\.0\.\d+\.\d+$") || !Regex.IsMatch(executableVersion ?? "", @"^16\.0\.\d+\.\d+$")) throw new InvalidDataException("Não foi possível confirmar a versão real do Office.");
            if (!((platform == "x64" && executableMachine == 0x8664) || (platform == "x86" && executableMachine == 0x014c))) throw new InvalidDataException("Arquitetura do executável Office não corresponde ao registro.");
            return TechnicianInput.Required("Microsoft 365 Apps para Grandes Empresas (O365ProPlusRetail) — build " + build + " / Word " + executableVersion + " — " + (platform == "x64" ? "64" : "32") + " bits", 255, "Office instalado");
        }

        internal static ushort PEMachine(string path)
        {
            using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new BinaryReader(file))
            {
                if (reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Executável Office inválido.");
                file.Position = 0x3c; int offset = reader.ReadInt32();
                if (offset < 0x40 || offset > file.Length - 6) throw new InvalidDataException("Cabeçalho Office inválido.");
                file.Position = offset;
                if (reader.ReadUInt32() != 0x00004550) throw new InvalidDataException("Formato Office inválido.");
                return reader.ReadUInt16();
            }
        }

        public static MachineSnapshot Capture()
        {
            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
            using (var office = machine.OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration"))
            {
                if (office == null) throw new InvalidDataException("Registro do Office instalado não encontrado.");
                string products = Convert.ToString(office.GetValue("ProductReleaseIds", ""));
                string platform = Convert.ToString(office.GetValue("Platform", ""));
                string build = Convert.ToString(office.GetValue("VersionToReport", ""));
                string installationPath = Convert.ToString(office.GetValue("InstallationPath", ""));
                if (String.IsNullOrWhiteSpace(installationPath)) throw new InvalidDataException("Caminho real do Office não confirmado.");
                string[] candidates = { Path.Combine(installationPath, "root", "Office16", "WINWORD.EXE"), Path.Combine(installationPath, "Office16", "WINWORD.EXE") };
                string word = candidates.FirstOrDefault(File.Exists);
                if (word == null) throw new InvalidDataException("WINWORD.EXE não encontrado no caminho instalado. Instalação não confirmada.");
                var fileVersion = FileVersionInfo.GetVersionInfo(word);
                string actualVersion = String.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}.{3}", fileVersion.FileMajorPart, fileVersion.FileMinorPart, fileVersion.FileBuildPart, fileVersion.FilePrivatePart);
                return new MachineSnapshot { ComputerName = TechnicianInput.Required(Environment.MachineName, 255, "Nome do computador"), OSVersion = OSDescription(), OfficeVersion = OfficeDescription(products, build, platform, actualVersion, PEMachine(word)) };
            }
        }
    }

    internal sealed class HttpOutcome
    {
        public int Status;
        public string Body;
        public HttpOutcome(int status, string body) { Status = status; Body = body; }
    }

    internal static class ComputerTransport
    {
        public const string Endpoint = "https://roteiro-maquim.lovable.app/api/public/computer-installations";
        internal static HttpWebRequest Request(int length, string token)
        {
            var request = (HttpWebRequest)WebRequest.Create(Endpoint);
            request.Method = "POST"; request.ContentType = "application/json";
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
            request.AllowAutoRedirect = false; request.Timeout = 30000; request.ReadWriteTimeout = 30000;
            request.ContentLength = length; request.UseDefaultCredentials = false;
            return request;
        }
        public static HttpOutcome Send(byte[] bytes, string token)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            // Nao ha callbacks de certificado nem redirecionamentos autenticados.
            var request = Request(bytes.Length, token);
            using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            HttpWebResponse response;
            try { response = (HttpWebResponse)request.GetResponse(); }
            catch (WebException ex) { response = ex.Response as HttpWebResponse; if (response == null) throw; }
            using (response)
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var buffer = new char[16385]; int count = 0, read;
                while (count < buffer.Length && (read = reader.Read(buffer, count, buffer.Length - count)) > 0) count += read;
                return new HttpOutcome((int)response.StatusCode, count > 16384 ? "" : new string(buffer, 0, count));
            }
        }
    }

    internal sealed class ComputerQueue
    {
        public static string DefaultRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Maquim", "ComputerInstallations"); } }
        readonly string root;
        public string Root { get { return root; } }
        public string QueueDirectory { get { return Path.Combine(root, "queue"); } }
        public ComputerQueue(string path = null)
        {
            root = path ?? DefaultRoot;
            var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WindowsIdentity.GetCurrent().User, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.CreateDirectory(root, acl); Directory.CreateDirectory(QueueDirectory);
        }
        internal static void AtomicWrite(string path, byte[] bytes, bool replace)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (replace && File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public string Enqueue(ComputerInstallation record)
        {
            var bytes = record.Bytes(); var path = Path.Combine(QueueDirectory, record.installation_id + ".json");
            if (File.Exists(path)) { if (!File.ReadAllBytes(path).SequenceEqual(bytes)) throw new InvalidDataException("O mesmo UUID já possui outro conteúdo na fila."); return path; }
            AtomicWrite(path, bytes, false); return path;
        }
        public bool IsPending(string id) { return File.Exists(Path.Combine(QueueDirectory, id + ".json")); }
        public string LoadToken()
        {
            string path = Path.Combine(root, "token.dpapi");
            if (!File.Exists(path)) return "";
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser));
        }
        public void SaveToken(string value)
        {
            string token = (value ?? "").Trim();
            if (token.Length == 0 || token.Length > 4096 || token.Any(Char.IsControl)) throw new InvalidDataException("Token vazio ou inválido.");
            AtomicWrite(Path.Combine(root, "token.dpapi"), ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser), true);
            string paused = Path.Combine(root, "auth-paused"); if (File.Exists(paused)) File.Delete(paused);
        }
        public void ImportEnvironmentToken()
        {
            string value = Environment.GetEnvironmentVariable("INSTALLATIONS_INGEST_TOKEN");
            if (!String.IsNullOrWhiteSpace(value) && value.Trim() != LoadToken()) SaveToken(value);
        }
        public void Note(string message)
        {
            // Somente mensagens internas; nunca inclui excecoes de HTTP, respostas ou headers.
            AtomicWrite(Path.Combine(root, "status.txt"), Encoding.UTF8.GetBytes(DateTimeOffset.Now.ToString("o") + " " + message), true);
        }
        public void RetryBlocked()
        {
            using (var lease = new FileStream(Path.Combine(root, "sync.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                foreach (var file in Directory.GetFiles(QueueDirectory, "*.blocked")) File.Delete(file);
        }
        internal static bool Acknowledged(HttpOutcome response, string id)
        {
            if (response == null || (response.Status != 200 && response.Status != 201)) return false;
            try {
                var data = new JavaScriptSerializer().DeserializeObject(response.Body ?? "") as Dictionary<string, object>;
                object ok, confirmed;
                return data != null && data.TryGetValue("ok", out ok) && ok is bool && (bool)ok && data.TryGetValue("installation_id", out confirmed) && confirmed is string && String.Equals((string)confirmed, id, StringComparison.OrdinalIgnoreCase);
            } catch { return false; }
        }
        public void Retry(Func<byte[], string, HttpOutcome> transport = null, string preferredId = null)
        {
            FileStream lease;
            try { lease = new FileStream(Path.Combine(root, "sync.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { return; }
            using (lease)
            {
                string token;
                try { token = LoadToken(); } catch { Note("Envio pendente: configure o token nesta conta Windows."); return; }
                if (token.Length == 0) { Note("Envio pendente: configure INSTALLATIONS_INGEST_TOKEN em Configurar integração."); return; }
                if (File.Exists(Path.Combine(root, "auth-paused"))) { Note("Envio pausado: token rejeitado (401). Corrija em Configurar integração."); return; }
                Func<byte[], string, HttpOutcome> send = transport ?? new Func<byte[], string, HttpOutcome>(ComputerTransport.Send);
                foreach (var path in Directory.GetFiles(QueueDirectory, "*.json").Where(p => !File.Exists(p + ".blocked")).OrderBy(p => Path.GetFileNameWithoutExtension(p) == preferredId ? 0 : 1).ThenBy(p => p, StringComparer.Ordinal).Take(20))
                {
                    if (File.Exists(path + ".blocked")) continue;
                    byte[] bytes; ComputerInstallation record;
                    try {
                        bytes = File.ReadAllBytes(path);
                        if (bytes.Length > 16384) throw new InvalidDataException();
                        var raw = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
                        string[] keys = { "installation_id", "installed_at", "computer_name", "os_version", "office_version", "serial_number", "asset_tag", "installer_version" };
                        if (raw == null || raw.Count != keys.Length || keys.Any(k => !raw.ContainsKey(k) || !(raw[k] is string))) throw new InvalidDataException();
                        record = new JavaScriptSerializer().Deserialize<ComputerInstallation>(Encoding.UTF8.GetString(bytes)); record.Validate();
                        if (Path.GetFileNameWithoutExtension(path) != record.installation_id) throw new InvalidDataException();
                    } catch { Block(path, "Registro local inválido. Revisão manual necessária."); continue; }
                    HttpOutcome response;
                    try { response = send(bytes, token); }
                    catch { Note("Instalação concluída; envio dos dados pendente. Falha de rede ou timeout."); break; }
                    if (Acknowledged(response, record.installation_id)) { File.Delete(path); Note("Dados recebidos pelo Roteiro Maquim: " + record.installation_id); continue; }
                    if (response != null && response.Status == 401) { if (LoadToken() == token) AtomicWrite(Path.Combine(root, "auth-paused"), Encoding.UTF8.GetBytes("401"), true); Note("Envio pausado: token rejeitado (401). Corrija em Configurar integração."); break; }
                    if (response != null && new[] {400,413,415,422}.Contains(response.Status)) { Block(path, "HTTP " + response.Status + ": formato/tamanho rejeitado; reenvio automático pausado para este registro."); continue; }
                    Note("Instalação concluída; envio dos dados pendente. Confirmação ausente ou erro temporário.");
                    break;
                }
            }
        }
        void Block(string path, string reason) { AtomicWrite(path + ".blocked", Encoding.UTF8.GetBytes(reason), true); Note(reason); }
    }
}
