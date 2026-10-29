using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Maquim.OfficeInstaller;

static class MonitorTests
{
    static int checks;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    static HttpOutcome OK(byte[] bytes, string token) {
        var r = new JavaScriptSerializer().Deserialize<StatusReport>(Encoding.UTF8.GetString(bytes));
        return new HttpOutcome(201, "{\"ok\":true,\"event_id\":\"" + r.event_id + "\"}");
    }
    static void Main() {
        string root = Path.Combine(Path.GetTempPath(), "MaquimMonitorTests-" + Guid.NewGuid().ToString("N"));
        try {
            var store = new MonitorStore(root); var installationId = Guid.NewGuid();
            store.Configure(installationId, new TechnicianInput { SerialNumber = " S-01 ", AssetTag = " 000123 " }, "test-runtime-token-not-for-production");
            var config = store.Config();
            Check(config.serial_number == "S-01" && config.asset_tag == "000123", "identificação técnica preservada sem espaços nas extremidades");
            Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, "token.dpapi"))).Contains("test-runtime-token"), "token protegido por DPAPI da máquina");
            Check(store.Token() == "test-runtime-token-not-for-production", "token pode ser recuperado pelo serviço");
            var engine = new MonitorEngine(store); var now = DateTimeOffset.Parse("2026-10-29T10:30:00-03:00");
            Func<string[]> ip = () => new[] { "192.168.1.20" };
            Func<DiskStatus[]> disks = () => new[] { new DiskStatus { name = "C:\\", total_bytes = 256000000000, free_bytes = 128000000000 } };
            int sent = 0;
            Func<byte[], string, HttpOutcome> offline = (b,t) => { sent++; throw new IOException(); };
            engine.Tick(now, ip, disks, offline);
            var file = Directory.GetFiles(store.QueueDirectory, "*.json").Single(); var original = File.ReadAllBytes(file);
            var report = new JavaScriptSerializer().Deserialize<StatusReport>(Encoding.UTF8.GetString(original));
            Check(report.computer_name == Environment.MachineName && report.installation_id == installationId.ToString("D"), "nome real e vínculo da instalação");
            Check(report.serial_number == "S-01" && report.asset_tag == "000123", "payload reaproveita série e patrimônio");
            Check(report.disks[0].free_bytes == 128000000000 && report.ip_addresses[0] == "192.168.1.20", "espaço e IP coletados");
            Check(sent == 1 && File.Exists(file), "offline preserva fila antes do envio");
            engine.Tick(now.AddMinutes(1), ip, disks, offline);
            Check(sent == 1 && Directory.GetFiles(store.QueueDirectory, "*.json").Length == 1, "intervalo de reenvio evita loop de rede");
            Check(!MonitorEngine.Due(store.State(), ip(), now.AddHours(11.99)), "sem coleta extra antes de 12 horas");
            Check(MonitorEngine.Due(store.State(), ip(), now.AddHours(12)), "coleta periódica a cada 12 horas");
            Check(MonitorEngine.Due(store.State(), new[] { "10.0.0.20" }, now.AddMinutes(1)), "mudança de IP antecipa coleta");
            var reopened = new MonitorStore(root);
            Check(reopened.Config().machine_id == config.machine_id && reopened.State().sequence_number == 1 && File.ReadAllBytes(file).SequenceEqual(original), "fila e identidade sobrevivem à reabertura");
            store.Retry((b,t) => new HttpOutcome(201, "{\"ok\":true,\"event_id\":\"" + Guid.NewGuid() + "\"}"));
            Check(File.Exists(file) && File.ReadAllBytes(file).SequenceEqual(original), "confirmação com outro ID preserva bytes");
            store.Retry((b,t) => new HttpOutcome(200, "{\"duplicate\":true,\"event_id\":\"" + report.event_id + "\"}"));
            Check(File.Exists(file), "duplicate sem ok não confirma recebimento");
            store.Retry((b,t) => new HttpOutcome(401, "{}"));
            Check(File.Exists(file) && File.Exists(Path.Combine(root, "auth-paused")), "401 preserva registro e pausa envio");
            store.Retry((b,t) => { throw new Exception("não deve enviar"); });
            store.SaveToken("updated-test-runtime-token");
            Check(!File.Exists(Path.Combine(root, "auth-paused")), "corrigir token retoma reenvio");
            store.Retry((b,t) => new HttpOutcome(503, "{}")); Check(File.Exists(file), "503 preserva evento");
            store.Retry((b,t) => new HttpOutcome(302, "{}")); Check(File.Exists(file), "redirecionamento não confirma");
            store.Retry((b,t) => { Check(b.SequenceEqual(original), "reenvio mantém conteúdo, data e ID"); return new HttpOutcome(200, "{\"ok\":true,\"duplicate\":true,\"event_id\":\"" + report.event_id + "\"}"); });
            Check(!File.Exists(file), "200 com confirmação remove duplicata da fila");
            engine.Tick(now.AddHours(12), ip, disks, OK);
            Check(store.State().sequence_number == 2 && Directory.GetFiles(store.QueueDirectory, "*.json").Length == 0, "segunda coleta usa sequência nova");
            engine.Tick(now.AddHours(12).AddMinutes(1), () => new[] { "10.0.0.1" }, disks, offline);
            Check(store.State().sequence_number == 3 && store.State().ip_addresses[0] == "10.0.0.1", "mudança de IP cria evento próprio");
            file = Directory.GetFiles(store.QueueDirectory, "*.json").Single();
            store.Retry((b,t) => new HttpOutcome(422, "{}"));
            Check(File.Exists(file) && File.Exists(file + ".blocked"), "422 bloqueia o registro sem perda");
            store.Retry((b,t) => { throw new Exception("bloqueado não deve enviar"); });
            File.Delete(file + ".blocked");
            // Simula queda após enqueue, antes de gravar state.
            File.Delete(Path.Combine(root, "state.json"));
            Check(store.State().sequence_number == 3, "recuperação atômica obtém sequência da fila após queda");
            store.Retry(OK);
            // Restaura state a partir do último relatório antes de testar reconfiguração.
            ComputerQueue.AtomicWrite(Path.Combine(root, "state.json"), Encoding.UTF8.GetBytes("{\"sequence_number\":3,\"observed_at\":\"" + now.ToString("o") + "\",\"ip_addresses\":[\"10.0.0.1\"]}"), true);
            store.Configure(Guid.NewGuid(), new TechnicianInput { SerialNumber = "S-01", AssetTag = "MQ-00123" }, "updated-test-runtime-token");
            Check(store.Config().machine_id == config.machine_id && store.Config().asset_tag == "MQ-00123" && store.State().sequence_number == 3, "reinstalação mantém identidade e sequência; patrimônio alfanumérico");
            Check(StatusReport.NormalizeIPs(new[] { "10.0.0.2", "10.0.0.1", "10.0.0.2" }).SequenceEqual(new[] { "10.0.0.1", "10.0.0.2" }), "ordem dos adaptadores não provoca alerta falso");
            var request = MonitorTransport.Request(123, "test");
            Check(request.RequestUri.Scheme == "https" && request.RequestUri.AbsolutePath == "/api/public/computer-status" && !request.AllowAutoRedirect && request.Timeout == 30000 && request.ReadWriteTimeout == 30000, "rota HTTPS separada, timeout e sem redirecionamentos");
            Check(MonitorCollector.IPs().Length <= 32 && MonitorCollector.Disks().Length > 0, "coletores reais executam no Windows CI");
            LocationTests.Run(root + "-location");
            Console.WriteLine(checks + " verificações do monitor passaram.");
        } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
