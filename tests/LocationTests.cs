using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Maquim.OfficeInstaller;

static class LocationTests
{
    static int checks;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    static void Bad(Action action, string message) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, message); }
    internal static void Run(string root) {
        Directory.CreateDirectory(root);
        var json = new JavaScriptSerializer();
        var now = DateTimeOffset.Parse("2026-10-29T10:30:00-03:00");
        const string ipFixture = "{\"success\":true,\"ip\":\"198.51.100.5\",\"latitude\":-23.2,\"longitude\":-45.9,\"city\":\"Teste\",\"region\":\"SP\",\"country\":\"Brasil\"}";
        try {
            int calls = 0;
            var resolver = new LocationResolver(root, () => { calls++; return ipFixture; });
            var ip = resolver.Resolve(now, new[] { "10.0.0.1" });
            Check(ip.source == "public_ip" && ip.windows_status == "no_session" && ip.public_ip == "198.51.100.5" && ip.accuracy_meters == null, "sem sessao usa IP publico sem precisao inventada");
            Check(ip.captured_at == now.ToString("o") && ip.latitude == -23.2, "coordenadas e horario reais da consulta");
            Check(resolver.Resolve(now.AddMinutes(2), new[] { "10.0.0.1" }).captured_at == ip.captured_at && calls == 1, "cache preserva horario e reduz consultas");
            Check(resolver.Resolve(now.AddMinutes(2), new[] { "10.0.0.2" }).source == "unavailable" && calls == 1, "troca de rede nao reutiliza coordenadas antigas enquanto consulta limitada");
            var win = new LocationSnapshot { source = "windows", latitude = -23.12345, longitude = -45.54321, accuracy_meters = 25, captured_at = now.ToString("o"), windows_status = "available" };
            var path = Path.Combine(root, "windows-location.json");
            File.WriteAllText(path, json.Serialize(win));
            var selected = resolver.Resolve(now.AddMinutes(5), new[] { "10.0.0.1" });
            Check(selected.source == "windows" && selected.accuracy_meters == 25 && calls == 1, "Windows recente tem prioridade e nao consulta provedor");
            selected = resolver.Resolve(now.AddMinutes(16), new[] { "10.0.0.1" });
            Check(selected.source == "public_ip" && selected.windows_status == "stale" && calls == 2, "posicao Windows antiga aciona fallback");
            File.WriteAllText(path, json.Serialize(LocationSnapshot.Unavailable("permission_denied")));
            Check(resolver.Resolve(now.AddMinutes(17), new[] { "10.0.0.1" }).windows_status == "permission_denied", "permissao negada aparece na origem alternativa");
            var offline = new LocationResolver(root, () => { throw new IOException(); });
            Check(offline.Resolve(now.AddMinutes(32), new[] { "10.0.0.1" }).source == "unavailable", "falha nas duas fontes nao fabrica posicao");
            Check(!LocationSnapshot.Fresh(win, now.AddMinutes(16)) && !LocationSnapshot.Fresh(win, now.AddMinutes(-3)), "coordenadas antigas ou futuras rejeitadas como recentes");
            win.latitude = 91; Bad(win.Validate, "latitude fora do intervalo rejeitada"); win.latitude = 0;
            win.longitude = Double.NaN; Bad(win.Validate, "NaN rejeitado"); win.longitude = 0;
            win.accuracy_meters = -1; Bad(win.Validate, "precisao negativa rejeitada"); win.accuracy_meters = 10;
            Check(LocationSnapshot.Fresh(win, now), "zero zero e coordenada valida, nao placeholder");
            var unavailable = LocationSnapshot.Unavailable("error"); unavailable.latitude = 0;
            Bad(unavailable.Validate, "indisponivel nao pode ter coordenadas");
            Bad(() => LocationResolver.FromIP("{\"success\":false}", now, "no_session"), "erro do provedor nao gera posicao");
            Bad(() => LocationResolver.FromIP(ipFixture.Replace("-23.2", "\"-23.2\""), now, "no_session"), "coordenada textual do provedor rejeitada");
            var request = PublicIPLocation.Request();
            Check(request.RequestUri.Scheme == "https" && !request.AllowAutoRedirect && request.Headers["Authorization"] == null && request.Timeout == 15000, "consulta externa usa HTTPS sem token Maquim e sem redirects");
            var limiterRoot = Path.Combine(root, "limiter"); Directory.CreateDirectory(limiterRoot);
            int limitedCalls = 0;
            var limit = new LocationResolver(limiterRoot, () => { limitedCalls++; throw new IPLookupLimitException(86400); });
            Check(limit.Resolve(now, new string[0]).source == "unavailable", "429 preserva status indisponivel");
            limit.Resolve(now.AddHours(1), new string[0]);
            Check(limitedCalls == 1, "limite do provedor sobrevive entre coletas");
            Check(new LocationResolver(Path.Combine(root, "nonexistent"), () => { throw new Exception("nao deve consultar"); }).Resolve(now, new string[0], false).source == "unavailable", "sem token nao consulta provedor nos testes do servico");
            // Ponte local real: auxiliar nao necessita acesso ao token/configuracao.
            win.captured_at = DateTimeOffset.Now.ToString("o");
            using (var host = new LocationPipeHost(root)) {
                host.Start(); Thread.Sleep(100);
                Check(LocationPipeHost.Publish(win), "auxiliar publica coordenadas por pipe local");
                Check(json.Deserialize<LocationSnapshot>(File.ReadAllText(path)).accuracy_meters == 10, "servico persiste cache validado");
            }
            Console.WriteLine(checks + " verificacoes de localizacao passaram.");
        } finally { Directory.Delete(root, true); }
    }
}
