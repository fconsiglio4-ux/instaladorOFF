using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Maquim.OfficeInstaller;

internal static class IntegrationTests
{
    static int assertions;
    static string temporary;
    static void Assert(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
    static void Throws(Action action, string message) { bool thrown = false; try { action(); } catch { thrown = true; } Assert(thrown, message); }
    static ComputerInstallation Record()
    {
        return new MachineSnapshot { ComputerName = "TEST-PC", OSVersion = "Windows teste (build 123)", OfficeVersion = "Office teste / 64 bits" }
            .Record(Guid.NewGuid(), new TechnicianInput { SerialNumber = "  SN-001  ", AssetTag = "  000123  " }, new DateTimeOffset(2026, 10, 29, 10, 30, 0, TimeSpan.FromHours(-3)));
    }
    static ComputerQueue Queue()
    {
        var queue = new ComputerQueue(Path.Combine(temporary, Guid.NewGuid().ToString("N")));
        queue.SaveToken("TEST-TOKEN-NEVER-SENT"); return queue;
    }
    static string Ack(string id, bool duplicate = false) { return "{\"ok\":true,\"installation_id\":\"" + id + "\",\"duplicate\":" + (duplicate ? "true" : "false") + "}"; }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--verify-persist") {
            try {
                var q = new ComputerQueue(args[1]);
                string content = File.ReadAllText(Path.Combine(q.QueueDirectory, args[2] + ".json"));
                var record = new JavaScriptSerializer().Deserialize<ComputerInstallation>(content);
                return record.installation_id == args[2] && record.asset_tag == "000123" && record.serial_number == "SN-001" && q.LoadToken() == "TEST-TOKEN-NEVER-SENT" ? 0 : 2;
            } catch { return 3; }
        }
        temporary = Path.Combine(Path.GetTempPath(), "Maquim-computer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try {
            TestInputsAndNavigation(); TestDetection(); TestContract(); TestAcknowledgements(); TestQueue(); TestAuthenticationAndBlocks(); TestTransport(); TestEmbeddedToken();
            Console.WriteLine("OK: " + assertions + " verificacoes; transporte sempre simulado; nenhum POST em producao."); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
        finally { Directory.Delete(temporary, true); }
    }

    static void TestInputsAndNavigation()
    {
        foreach (string empty in new[] { null, "", " ", "\t\r\n" }) {
            Throws(() => new TechnicianInput { SerialNumber = empty, AssetTag = "000123" }.Validate(), "Serial vazio aceito");
            Throws(() => new TechnicianInput { SerialNumber = "SN-001", AssetTag = empty }.Validate(), "Patrimonio vazio aceito");
        }
        Throws(() => new TechnicianInput { SerialNumber = new string('a', 256), AssetTag = "x" }.Validate(), "Serial longo aceito");
        Throws(() => new TechnicianInput { SerialNumber = "x", AssetTag = new string('a', 101) }.Validate(), "Patrimonio longo aceito");
        var limits = new TechnicianInput { SerialNumber = new string('s',255), AssetTag = new string('0',100) }; limits.Validate();
        var input = new TechnicianInput { SerialNumber = "  SERIAL  01  ", AssetTag = "  MQ-00123  " }; input.Validate();
        Assert(input.SerialNumber == "SERIAL  01" && input.AssetTag == "MQ-00123", "Trim alterou espacos internos/letras");
        string image = Path.Combine(temporary, "office.img"); File.WriteAllText(image, "fixture");
        using (var form = new InstallerForm()) {
            form.media.Text = image; form.accept.Checked = true;
            form.asset.Text = "000123";
            Throws(() => form.GoForward(), "Interface avancou sem serial");
            form.serial.Text = "SN-001"; form.asset.Text = " ";
            Throws(() => form.GoForward(), "Interface avancou sem patrimonio");
            form.serial.Text = " SN-001 "; form.asset.Text = " 000123 "; form.GoForward();
            Assert(form.Confirming && form.confirmation.Text.Contains("SN-001") && form.confirmation.Text.Contains("000123"), "Conferencia omitiu identificacao");
            form.GoBack();
            Assert(!form.Confirming && form.serial.Text == "SN-001" && form.asset.Text == "000123", "Voltar perdeu valores");
            form.asset.Text = "MQ-00123"; form.GoForward(); form.GoBack();
            Assert(form.asset.Text == "MQ-00123" && form.serial.Text == "SN-001", "Patrimonio foi copiado/convertido");
            Assert(form.serial.MaxLength == 255 && form.asset.MaxLength == 100, "Limites da interface incorretos");
        }
    }

    static void TestDetection()
    {
        string version = "16.0.17928.20148";
        string text = MachineCollector.OfficeDescription("O365ProPlusRetail", version, "x64", "16.0.17928.20114", 0x8664);
        Assert(text.Contains(version) && text.Contains("16.0.17928.20114") && text.Contains("64 bits") && text.Contains("O365ProPlusRetail"), "Versoes reais nao preservadas");
        Assert(MachineCollector.OfficeDescription("VisioProRetail,O365ProPlusRetail", version, "x86", version, 0x014c).Contains("32 bits"), "Office x86 incorreto");
        Throws(() => MachineCollector.OfficeDescription("O365ProPlusRetail", "", "x64", version, 0x8664), "Versao ausente presumida");
        Throws(() => MachineCollector.OfficeDescription("O365ProPlusRetail", version, "", version, 0x8664), "Platform ausente presumido");
        Throws(() => MachineCollector.OfficeDescription("OtherOffice", version, "x64", version, 0x8664), "Edicao incorreta aceita");
        Throws(() => MachineCollector.OfficeDescription("O365ProPlusRetail", version, "x64", version, 0x014c), "Arquitetura conflitante aceita");
        string pe = Path.Combine(temporary,"word.exe"); var data = new byte[256]; data[0] = 0x4d; data[1] = 0x5a; data[0x3c] = 0x80; data[0x80] = 0x50; data[0x81] = 0x45; data[0x84] = 0x64; data[0x85] = 0x86;
        File.WriteAllBytes(pe, data); Assert(MachineCollector.PEMachine(pe) == 0x8664, "Leitura PE incorreta");
        File.WriteAllText(pe, "not PE"); Throws(() => MachineCollector.PEMachine(pe), "Executavel invalido confirmado");
        Assert(!String.IsNullOrWhiteSpace(Environment.MachineName) && Environment.MachineName.Length <=255, "Nome real indisponivel");
        string os = MachineCollector.OSDescription(); Assert(os.Contains("Windows") && os.Contains("build") && os.Length <=255, "Windows real nao confirmado no runner");
        Throws(() => new MachineSnapshot { ComputerName = "x", OSVersion = "y", OfficeVersion = "" }.Record(Guid.NewGuid(), new TechnicianInput { SerialNumber = "s", AssetTag = "a" }, DateTimeOffset.Now), "Snapshot incompleto aceito");
    }

    static void TestContract()
    {
        var record = Record(); var bytes = record.Bytes();
        var json = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
        string[] keys = { "installation_id", "installed_at", "computer_name", "os_version", "office_version", "serial_number", "asset_tag", "installer_version" };
        Assert(json.Count == 8 && keys.All(json.ContainsKey), "Campos extras/ausentes no JSON");
        Assert((string)json["asset_tag"] == "000123" && (string)json["serial_number"] == "SN-001", "Valores do tecnico incorretos");
        Assert((string)json["installed_at"] == "2026-10-29T10:30:00-03:00", "Data/fuso incorretos");
        Assert((string)json["installer_version"] == Release.Version && bytes.Length <=16384, "Versao/tamanho incorretos");
        Assert(Record().installation_id != record.installation_id, "Nova execucao reutilizou UUID");
        record.computer_name = new string('x',256); Throws(() => record.Bytes(), "Nome longo aceito");
        record = Record(); record.installed_at = "2026-10-29T10:30:00"; Throws(() => record.Bytes(), "Data sem fuso aceita");
    }

    static void TestAcknowledgements()
    {
        string id = Record().installation_id;
        Assert(ComputerQueue.Acknowledged(new HttpOutcome(201,Ack(id)),id), "201 valido rejeitado");
        Assert(ComputerQueue.Acknowledged(new HttpOutcome(200,Ack(id,true)),id), "200 duplicado rejeitado");
        foreach (var response in new[] {new HttpOutcome(201,"{}"), new HttpOutcome(200,"not JSON"), new HttpOutcome(200,Ack(Guid.NewGuid().ToString())), new HttpOutcome(500,Ack(id)), new HttpOutcome(200,"{\"duplicate\":true}"), new HttpOutcome(200,"{\"ok\":\"true\",\"installation_id\":\""+id+"\"}"), new HttpOutcome(200,"{\"ok\":false,\"installation_id\":\""+id+"\"}"), new HttpOutcome(302,Ack(id))})
            Assert(!ComputerQueue.Acknowledged(response,id), "Confirmacao insegura aceita");
    }

    static void TestQueue()
    {
        var queue = Queue(); var record = Record(); string path = queue.Enqueue(record); byte[] original = File.ReadAllBytes(path);
        queue.Retry((b,t) => { Assert(b.SequenceEqual(original), "Conteudo alterado antes do envio"); throw new WebException("timeout simulado"); });
        Assert(queue.IsPending(record.installation_id), "Falha de rede perdeu registro");
        Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(queue.Root,"token.dpapi"))).Contains("TEST-TOKEN"), "Token nao protegido");
        using (var p = Process.Start(new ProcessStartInfo(typeof(IntegrationTests).Assembly.Location, "--verify-persist \"" + queue.Root + "\" " + record.installation_id) {UseShellExecute=false,CreateNoWindow=true})) { p.WaitForExit(); Assert(p.ExitCode == 0, "Outro processo nao recuperou fila/token"); }
        queue = new ComputerQueue(queue.Root);
        queue.Retry((b,t) => { Assert(b.SequenceEqual(original), "Retry alterou ID/data/conteudo"); return new HttpOutcome(200,Ack(record.installation_id,true)); });
        Assert(!queue.IsPending(record.installation_id), "Duplicado confirmado nao removido");
        queue.Enqueue(record); queue.Enqueue(record); Assert(Directory.GetFiles(queue.QueueDirectory,"*.json").Length == 1, "Enqueue duplicou arquivo");
        record.asset_tag = "MQ-00123"; Throws(() => queue.Enqueue(record), "Mesmo ID sobrescreveu conteudo");
        Assert(File.ReadAllBytes(path).SequenceEqual(original), "Registro original modificado");
        int sent = 0;
        using (var lease = new FileStream(Path.Combine(queue.Root,"sync.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) queue.Retry((b,t)=> {sent++; return new HttpOutcome(503,"");});
        Assert(sent == 0, "Dois remetentes concorreram");
        foreach (int status in new[] {200,201,302,503,504}) {
            var q = Queue(); var r = Record(); q.Enqueue(r);
            q.Retry((b,t) => new HttpOutcome(status,"{}"));
            Assert(q.IsPending(r.installation_id), "Resposta sem confirmacao apagou registro");
        }
        var offline = new ComputerQueue(Path.Combine(temporary,"offline-no-token")); var offlineRecord = Record(); offline.Enqueue(offlineRecord);
        offline.Retry((b,t)=> {throw new Exception("Nao deveria enviar sem token");});
        Assert(offline.IsPending(offlineRecord.installation_id), "Instalacao offline perdeu registro");
        offline.SaveToken("TEST-TOKEN-NEVER-SENT");
        offline.Retry((b,t)=>new HttpOutcome(201,Ack(offlineRecord.installation_id)));
        Assert(!offline.IsPending(offlineRecord.installation_id), "Reconexao nao reenviou fila");
        Assert(Directory.GetFiles(queue.QueueDirectory,"*.tmp").Length == 0, "Arquivo temporario vazou");
    }

    static void TestAuthenticationAndBlocks()
    {
        var queue = Queue(); var record = Record(); queue.Enqueue(record); int calls = 0;
        queue.Retry((b,t)=> {calls++; return new HttpOutcome(401,"");});
        new ComputerQueue(queue.Root).Retry((b,t)=> {calls++; return new HttpOutcome(401,"");});
        Assert(calls == 1 && queue.IsPending(record.installation_id), "401 perdeu dados/repetiu token incorreto");
        queue.SaveToken("CORRECTED-TEST-TOKEN"); queue.Retry((b,t)=>new HttpOutcome(201,Ack(record.installation_id)));
        Assert(!queue.IsPending(record.installation_id), "Corrigir token nao retomou fila");
        foreach (int status in new[] {400,413,415,422}) {
            var q = Queue(); var r = Record(); string path = q.Enqueue(r); byte[] original = File.ReadAllBytes(path); int requests = 0;
            q.Retry((b,t)=> {requests++; return new HttpOutcome(status,"");});
            new ComputerQueue(q.Root).Retry((b,t)=> {requests++; return new HttpOutcome(status,"");});
            Assert(requests == 1 && q.IsPending(r.installation_id) && File.Exists(path+".blocked"), "Erro permanente repetido ou perdido");
            q.RetryBlocked(); q.Retry((b,t)=> {Assert(b.SequenceEqual(original), "Reavaliar mudou payload"); return new HttpOutcome(200,Ack(r.installation_id,true));});
            Assert(!q.IsPending(r.installation_id), "Reavaliacao manual falhou");
        }
        var many = Queue();
        for (int i=0;i<21;i++) { string p = many.Enqueue(Record()); File.WriteAllText(p+".blocked", "fixture"); }
        var active = Record(); many.Enqueue(active);
        many.Retry((b,t)=>new HttpOutcome(201,Ack(active.installation_id)));
        Assert(!many.IsPending(active.installation_id), "Bloqueados impediram fila ativa");
        var invalid = Queue(); var data = Record(); string invalidPath = invalid.Enqueue(data);
        File.WriteAllText(invalidPath,File.ReadAllText(invalidPath).Replace("\"asset_tag\":\"000123\"", "\"asset_tag\":123"));
        int count=0; invalid.Retry((b,t)=> {count++;return new HttpOutcome(201,Ack(data.installation_id));});
        Assert(count==0 && invalid.IsPending(data.installation_id) && File.Exists(invalidPath+".blocked"),"Patrimonio numerico foi reenviado");
    }

    static void TestTransport()
    {
        var request = ComputerTransport.Request(20,"TEST-TOKEN-NEVER-SENT");
        Assert(request.RequestUri.AbsoluteUri == ComputerTransport.Endpoint && request.RequestUri.Scheme == "https", "Rota de computadores incorreta");
        Assert(!request.AllowAutoRedirect && request.Timeout == 30000 && request.ReadWriteTimeout == 30000, "Redirecionamento/timeout inseguros");
        Assert(request.Method == "POST" && request.ContentType == "application/json" && request.Headers[HttpRequestHeader.Authorization] == "Bearer TEST-TOKEN-NEVER-SENT", "Contrato HTTP incorreto");
        request.Abort();
    }

    static void TestEmbeddedToken()
    {
        string previous = Environment.GetEnvironmentVariable("INSTALLATIONS_INGEST_TOKEN");
        try {
            Environment.SetEnvironmentVariable("INSTALLATIONS_INGEST_TOKEN", null);
            Assert(!String.IsNullOrWhiteSpace(Release.EmbeddedIngestToken), "Recurso do token ausente");
            var q = new ComputerQueue(Path.Combine(temporary, "embedded-token"));
            q.ImportEnvironmentToken();
            Assert(q.LoadToken() == Release.EmbeddedIngestToken, "Token incorporado nao configurado automaticamente");
            Assert(new ComputerQueue(q.Root).LoadToken() == Release.EmbeddedIngestToken, "Agente nao recuperou token incorporado");
            q.SaveToken("LOCAL-OVERRIDE-TEST"); q.ImportEnvironmentToken();
            Assert(q.LoadToken() == "LOCAL-OVERRIDE-TEST", "Token incorporado sobrescreveu configuracao explicita");
            Environment.SetEnvironmentVariable("INSTALLATIONS_INGEST_TOKEN", "ENV-OVERRIDE-TEST"); q.ImportEnvironmentToken();
            Assert(q.LoadToken() == "ENV-OVERRIDE-TEST", "Substituicao por ambiente nao funcionou");
        } finally { Environment.SetEnvironmentVariable("INSTALLATIONS_INGEST_TOKEN", previous); }
    }
}
