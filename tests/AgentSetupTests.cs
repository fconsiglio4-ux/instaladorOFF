using System;
using System.IO;
using Maquim.OfficeInstaller;

internal static class AgentSetupTests
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static void Bad(Action action, string message) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, message); }
    public static void Run() {
        var config = new MonitorConfig { machine_id = Guid.NewGuid().ToString("D"), installation_id = Guid.NewGuid().ToString("D"), serial_number = "S-01", asset_tag = "000123" };
        var update = AgentSetupPlan.Create(true, config, new TechnicianInput { SerialNumber = "IGNORAR", AssetTag = "IGNORAR" });
        Check(update.Update && update.Existing.machine_id == config.machine_id && update.Existing.installation_id == config.installation_id, "Atualizacao nao pode gerar IDs novos");
        Check(update.Input.SerialNumber == "S-01" && update.Input.AssetTag == "000123", "Atualizacao deve usar identificacao existente");
        Check(update.Confirmation().Contains("000123") && update.Confirmation().Contains("filas"), "Confirmacao da atualizacao incompleta");
        Bad(() => AgentSetupPlan.Create(true, null, new TechnicianInput()), "Atualizacao sem configuracao aceita");
        Bad(() => AgentSetupPlan.Create(false, config, new TechnicianInput()), "Instalacao nova sobrescreve agente existente");
        Bad(() => AgentSetupPlan.Create(false, null, new TechnicianInput { SerialNumber = " ", AssetTag = "A" }), "Serie somente espacos aceita");
        Bad(() => AgentSetupPlan.Create(false, null, new TechnicianInput { SerialNumber = "S", AssetTag = " " }), "Patrimonio somente espacos aceito");
        var install = AgentSetupPlan.Create(false, null, new TechnicianInput { SerialNumber = " S-01 ", AssetTag = " MQ-00123 " });
        Check(install.Input.SerialNumber == "S-01" && install.Input.AssetTag == "MQ-00123", "Identificacao alfanumerica alterada");
        Check(!AgentSetupPlan.SameIdentification(config, new MonitorConfig { machine_id = config.machine_id, installation_id = Guid.NewGuid().ToString(), serial_number = config.serial_number, asset_tag = config.asset_tag }), "Mudanca entre confirmacao e execucao deve ser detectada");
        using (var form = new AgentSetupForm(false, () => null)) {
            form.serial.Text = " S-01 "; form.asset.Text = " 000123 "; form.GoForward();
            Check(form.confirmation.Text.Contains("000123") && form.confirmation.Text.Contains("Instalar somente"), "Agente exige midia/termos Office ou nao confirma campos");
            form.GoBack();
            Check(form.serial.Text == "S-01" && form.asset.Text == "000123", "Voltar apagou campos do agente");
            form.serial.Text = " "; Bad(form.GoForward, "Interface do agente aceitou serie vazia");
        }
        using (var form = new AgentSetupForm(true, () => config)) {
            Check(form.serial.ReadOnly && form.asset.ReadOnly && form.asset.Text == "000123", "Atualizacao deve carregar e proteger identificacao");
            form.GoForward(); Check(form.confirmation.Text.Contains("Atualizar/reparar") && form.confirmation.Text.Contains("S-01"), "Confirmacao de atualizacao incompleta");
            form.GoBack(); Check(form.asset.Text == "000123", "Atualizacao perdeu patrimonio ao voltar");
        }
        string root = Path.Combine(Path.GetTempPath(), "AgentPlan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { File.WriteAllText(Path.Combine(root, "config.json"), "invalid"); Bad(() => AgentSetupPlan.ReadExisting(root), "Configuracao corrompida foi tratada como agente novo"); }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("OK: " + checks + " verificacoes dos modos independentes; sem Office e sem criacao de usuarios.");
    }
}
