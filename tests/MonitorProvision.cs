using System;
using Maquim.OfficeInstaller;
static class MonitorProvision
{
    static void Main(string[] args) {
        // Runner/VM descartavel: token vazio impede qualquer POST.
        if (args.Length > 0 && args[0] == "--update") { MonitorInstaller.UpdateExisting(new MonitorStore().Config()); return; }
        if (args.Length > 0 && args[0] == "--wrong-identity") {
            var expected = new MonitorStore().Config(); expected.machine_id = Guid.NewGuid().ToString("D");
            bool rejected = false; try { MonitorInstaller.UpdateExisting(expected); } catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception("Identidade alterada nao foi rejeitada."); return;
        }
        MonitorInstaller.InstallStandalone( new TechnicianInput { SerialNumber = "CI-TEST-SERIAL", AssetTag = "CI-000123" }, "");
    }
}
