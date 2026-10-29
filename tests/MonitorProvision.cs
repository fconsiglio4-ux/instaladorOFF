using System;
using Maquim.OfficeInstaller;
static class MonitorProvision
{
    static void Main() {
        // Runner/VM descartavel: token vazio impede qualquer POST.
        MonitorInstaller.Install(Guid.NewGuid(), new TechnicianInput { SerialNumber = "CI-TEST-SERIAL", AssetTag = "CI-000123" }, "");
    }
}
