using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

namespace Maquim.OfficeInstaller
{
    internal sealed class LocationSnapshot
    {
        public string source { get; set; }
        public double? latitude { get; set; }
        public double? longitude { get; set; }
        public double? accuracy_meters { get; set; }
        public string captured_at { get; set; }
        public string public_ip { get; set; }
        public string city { get; set; }
        public string region { get; set; }
        public string country { get; set; }
        public string windows_status { get; set; }
        internal static bool Finite(double n) { return !Double.IsNaN(n) && !Double.IsInfinity(n); }
        internal static DateTimeOffset Time(string value) {
            DateTimeOffset t;
            if (!DateTimeOffset.TryParseExact(value, "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) throw new InvalidDataException("Data da localização inválida.");
            return t;
        }
        public void Validate() {
            if (Array.IndexOf(new[] {"available", "permission_denied", "unavailable", "no_session", "stale", "error"}, windows_status) < 0) throw new InvalidDataException("Estado da localização inválido.");
            if (source == "unavailable") {
                if (latitude != null || longitude != null || accuracy_meters != null || captured_at != null || public_ip != null || city != null || region != null || country != null || windows_status == "available") throw new InvalidDataException("Localização indisponível contém coordenadas presumidas.");
                return;
            }
            if (source != "windows" && source != "public_ip") throw new InvalidDataException("Origem inválida.");
            if (!latitude.HasValue || !longitude.HasValue || !Finite(latitude.Value) || !Finite(longitude.Value) || latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180) throw new InvalidDataException("Coordenadas inválidas.");
            Time(captured_at);
            if (source == "windows") {
                if (windows_status != "available" || !accuracy_meters.HasValue || !Finite(accuracy_meters.Value) || accuracy_meters < 0 || accuracy_meters > 40075017 || public_ip != null || city != null || region != null || country != null) throw new InvalidDataException("Precisão/origem Windows inválida.");
            } else {
                IPAddress ip;
                if (windows_status == "available" || accuracy_meters != null || !IPAddress.TryParse(public_ip, out ip) || IPAddress.IsLoopback(ip)) throw new InvalidDataException("Consulta por IP inválida.");
                foreach (var v in new[] {city, region, country}) if (v != null && (v.Length < 1 || v.Length > 255)) throw new InvalidDataException("Descrição de localização inválida.");
            }
        }
        public static LocationSnapshot Unavailable(string status) { return new LocationSnapshot { source = "unavailable", windows_status = status }; }
        internal static bool Fresh(LocationSnapshot value, DateTimeOffset now) {
            if (value == null || value.source == "unavailable") return false;
            try { value.Validate(); var age = now - Time(value.captured_at); return age >= TimeSpan.FromMinutes(-2) && age <= TimeSpan.FromMinutes(15); } catch { return false; }
        }
    }
    internal sealed class IPLookupState
    {
        public string next_attempt { get; set; }
        public string network { get; set; }
        public LocationSnapshot location { get; set; }
    }
    internal sealed class LocationResolver
    {
        readonly string root;
        readonly Func<string> lookup;
        public LocationResolver(string root, Func<string> lookup = null) { this.root = root; this.lookup = lookup ?? PublicIPLocation.Fetch; }
        internal static LocationSnapshot FromIP(string json, DateTimeOffset now, string windowsStatus) {
            var data = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (data == null || !data.ContainsKey("success") || !(data["success"] is bool) || !(bool)data["success"]) throw new InvalidDataException("Consulta de IP indisponível.");
            Func<string, double> number = key => {
                object v; if (!data.TryGetValue(key, out v) || (!(v is double) && !(v is decimal) && !(v is int) && !(v is long))) throw new InvalidDataException("Coordenada do provedor inválida.");
                return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            };
            Func<string, string> text = key => { object v; return data.TryGetValue(key, out v) && v is string && !String.IsNullOrWhiteSpace((string)v) ? ((string)v).Trim() : null; };
            var result = new LocationSnapshot { source = "public_ip", latitude = number("latitude"), longitude = number("longitude"), captured_at = now.ToString("o"), public_ip = text("ip"), city = text("city"), region = text("region"), country = text("country"), windows_status = windowsStatus };
            result.Validate(); return result;
        }
        public LocationSnapshot Resolve(DateTimeOffset now, string[] ips, bool allowPublicIP = true) {
            var serializer = new JavaScriptSerializer();
            string status = "no_session";
            try {
                string path = Path.Combine(root, "windows-location.json");
                if (File.Exists(path)) {
                    var value = serializer.Deserialize<LocationSnapshot>(File.ReadAllText(path)); value.Validate();
                    if (value.source == "windows" && LocationSnapshot.Fresh(value, now)) return value;
                    status = value.source == "windows" ? "stale" : value.windows_status;
                }
            } catch { status = "error"; }
            if (!allowPublicIP) return LocationSnapshot.Unavailable(status);
            string cachePath = Path.Combine(root, "ip-location.json");
            string network = String.Join(",", ips);
            IPLookupState cache = null;
            try { if (File.Exists(cachePath)) cache = serializer.Deserialize<IPLookupState>(File.ReadAllText(cachePath)); } catch { }
            if (cache != null) {
                if (cache.network == network && LocationSnapshot.Fresh(cache.location, now)) { cache.location.windows_status = status; return cache.location; }
                try { if (now < LocationSnapshot.Time(cache.next_attempt)) return LocationSnapshot.Unavailable(status); } catch { }
            }
            // Reserva persistente antes da rede: no maximo uma consulta a cada 15 min por agente.
            cache = new IPLookupState { next_attempt = now.AddMinutes(15).ToString("o"), network = network };
            ComputerQueue.AtomicWrite(cachePath, Encoding.UTF8.GetBytes(serializer.Serialize(cache)), true);
            try { cache.location = FromIP(lookup(), now, status); }
            catch (IPLookupLimitException ex) { cache.next_attempt = now.AddSeconds(ex.RetryAfterSeconds).ToString("o"); }
            catch { }
            ComputerQueue.AtomicWrite(cachePath, Encoding.UTF8.GetBytes(serializer.Serialize(cache)), true);
            return cache.location ?? LocationSnapshot.Unavailable(status);
        }
    }
    internal sealed class IPLookupLimitException : Exception
    {
        public int RetryAfterSeconds;
        public IPLookupLimitException(int seconds) { RetryAfterSeconds = seconds; }
    }
    internal static class PublicIPLocation
    {
        public const string Endpoint = "https://ipwho.is/?fields=success,ip,latitude,longitude,city,region,country";
        internal static HttpWebRequest Request() {
            var r = (HttpWebRequest)WebRequest.Create(Endpoint); r.Method = "GET"; r.AllowAutoRedirect = false;
            r.Timeout = 15000; r.ReadWriteTimeout = 15000; r.UseDefaultCredentials = false;
            r.UserAgent = "MaquimComputerMonitor/" + Release.Version; return r;
        }
        public static string Fetch() {
            // Sem token Maquim, coordenadas, serie ou patrimonio nesta requisicao.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var request = Request();
            HttpWebResponse response;
            try { response = (HttpWebResponse)request.GetResponse(); }
            catch (WebException ex) {
                var failed = ex.Response as HttpWebResponse;
                if (failed == null) throw;
                using (failed) {
                    if ((int)failed.StatusCode == 429) { int seconds; if (!Int32.TryParse(failed.Headers["Retry-After"], out seconds)) seconds = 86400; throw new IPLookupLimitException(Math.Max(900, Math.Min(86400, seconds))); }
                    throw new InvalidDataException("Consulta de IP indisponível.");
                }
            }
            using (response) {
                if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("Consulta de IP sem confirmação.");
                using (var stream = response.GetResponseStream()) using (var reader = new StreamReader(stream, Encoding.UTF8)) {
                    char[] buffer = new char[16385]; int count = 0, read;
                    while (count < buffer.Length && (read = reader.Read(buffer, count, buffer.Length - count)) > 0) count += read;
                    if (count > 16384) throw new InvalidDataException("Consulta de IP excedeu limite.");
                    return new string(buffer, 0, count);
                }
            }
        }
    }
}
