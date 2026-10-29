using System;
using System.IO;

namespace Maquim.OfficeInstaller
{
    internal sealed class AgentSetupPlan
    {
        public bool Update { get; private set; }
        public MonitorConfig Existing { get; private set; }
        public TechnicianInput Input { get; private set; }
        public static AgentSetupPlan Create(bool update, MonitorConfig existing, TechnicianInput input) {
            if (update) {
                if (existing == null) throw new InvalidOperationException("Não foi encontrada uma configuração do agente. Escolha Instalar somente agente.");
                existing.Validate();
                return new AgentSetupPlan { Update = true, Existing = existing, Input = new TechnicianInput { SerialNumber = existing.serial_number, AssetTag = existing.asset_tag } };
            }
            if (existing != null) throw new InvalidOperationException("O agente já possui configuração. Escolha Atualizar agente para preservar seus dados.");
            input.Validate(); return new AgentSetupPlan { Update = false, Input = input };
        }
        public static MonitorConfig ReadExisting(string root = null) {
            var store = new MonitorStore(root);
            if (!File.Exists(Path.Combine(store.Root, "config.json"))) return null;
            return store.Config(); // Configuracao invalida nunca e substituida por novos IDs.
        }
        internal static bool SameIdentification(MonitorConfig a, MonitorConfig b) {
            return a != null && b != null && a.machine_id == b.machine_id && a.installation_id == b.installation_id && a.serial_number == b.serial_number && a.asset_tag == b.asset_tag;
        }
        public string Confirmation() {
            return (Update ? "Atualizar/reparar agente Maquim" : "Instalar somente o agente Maquim") + "\n\nComputador: " + Environment.MachineName + "\nNúmero de série: " + Input.SerialNumber + "\nPatrimônio: " + Input.AssetTag + "\nVersão do pacote: " + Release.Version +
                (Update ? "\n\nIdentidade, vínculo, token e filas existentes serão preservados. O serviço será parado brevemente e iniciado ao concluir." : "\n\nSerá criado um identificador próprio para o agente, sem registrar instalação de Office.") +
                "\n\nServiço LocalService automático; status a cada 12 horas e ao mudar IP. Localização Windows com permissão; alternativa aproximada por IP público. O auxiliar de localização inicia no login do usuário.";
        }
    }
}
