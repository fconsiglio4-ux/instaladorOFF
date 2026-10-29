using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Win32;

namespace Maquim.OfficeInstaller
{
    internal static class MonitorInstaller
    {
        const string Service = "MaquimComputerMonitor";
        internal static void UpdateTokenIfInstalled(string token) {
            var store = new MonitorStore();
            if (File.Exists(Path.Combine(store.Root, "config.json")) && !String.IsNullOrEmpty(token)) store.SaveToken(token);
        }
        static bool Exists() {
            foreach (var service in ServiceController.GetServices()) using (service) if (service.ServiceName == Service) return true;
            return false;
        }
        static void SC(string args) {
            using (var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe"), args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })) {
                var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(30000)) { p.Kill(); throw new InvalidOperationException("Tempo excedido ao configurar o serviço."); }
                if (p.ExitCode != 0) throw new InvalidOperationException("Falha ao configurar o serviço de monitoramento (código " + p.ExitCode + ").");
            }
        }
        static byte[] Resource(string name) {
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
                if (resource == null) throw new InvalidOperationException("Recurso ausente no pacote: " + name);
                using (var memory = new MemoryStream()) { resource.CopyTo(memory); return memory.ToArray(); }
            }
        }
        static void Exclusive(Action action) {
            using (var mutex = new Mutex(false, @"Global\MaquimComputerMonitor.Setup")) {
                bool locked = false;
                try { try { locked = mutex.WaitOne(0); } catch (AbandonedMutexException) { locked = true; }
                    if (!locked) throw new InvalidOperationException("Outra instalação/atualização do agente está em execução.");
                    action();
                } finally { if (locked) mutex.ReleaseMutex(); }
            }
        }
        // Fluxo do Office: somente apos confirmar a instalacao real.
        public static void Install(Guid installationId, TechnicianInput input, string token) {
            input.Validate();
            Exclusive(() => Deploy(new MonitorStore(), () => new MonitorStore().Configure(installationId, input, token)));
        }
        public static void InstallStandalone(TechnicianInput input, string token) {
            input.Validate();
            Exclusive(() => {
                if (AgentSetupPlan.ReadExisting() != null || Exists()) throw new InvalidOperationException("Já existe agente ou configuração. Use Atualizar/reparar; não serão criados novos IDs sobre os dados existentes.");
                // Vinculo de provisionamento; nao representa nem cria uma instalacao de Office.
                var id = Guid.NewGuid(); var store = new MonitorStore();
                Deploy(store, () => store.Configure(id, input, token));
            });
        }
        public static void UpdateExisting(MonitorConfig expected, string token = null) {
            Exclusive(() => {
                var store = new MonitorStore(); var current = store.Config();
                if (!AgentSetupPlan.SameIdentification(current, expected)) throw new InvalidOperationException("A identificação do agente mudou desde a conferência. Volte e confira novamente.");
                // Nao chama Configure: config, IDs, series, filas e estado permanecem intactos.
                Deploy(store, () => { if (!String.IsNullOrWhiteSpace(token)) store.SaveToken(token); });
            });
        }
        static void Deploy(MonitorStore store, Action configure) {
            // Valida todos os recursos antes de parar um servico funcionando.
            var serviceBytes = Resource("Maquim-ComputerMonitor.exe"); var helperBytes = Resource("Maquim-ComputerLocation.exe");
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Maquim", "ComputerMonitor");
            string exe = Path.Combine(directory, "Maquim-ComputerMonitor.exe");
            string helper = Path.Combine(directory, "Maquim-ComputerLocation-" + Release.Version + ".exe");
            bool exists = Exists(), wasRunning = false, replaced = false;
            byte[] previous = File.Exists(exe) ? File.ReadAllBytes(exe) : null;
            object previousRun;
            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
            using (var run = machine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")) {
                previousRun = run.GetValue("MaquimComputerLocation");
                if (exists) using (var controller = new ServiceController(Service)) {
                    wasRunning = controller.Status != ServiceControllerStatus.Stopped;
                    if (wasRunning) { controller.Stop(); controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(45)); }
                }
                try {
                    configure();
                    Directory.CreateDirectory(directory);
                    if (previous == null || !previous.SequenceEqual(serviceBytes)) { ComputerQueue.AtomicWrite(exe, serviceBytes, true); replaced = true; }
                    if (!File.Exists(helper) || !File.ReadAllBytes(helper).SequenceEqual(helperBytes)) ComputerQueue.AtomicWrite(helper, helperBytes, true);
                    run.SetValue("MaquimComputerLocation", "\"" + helper + "\"", RegistryValueKind.String);
                    string parameters = " binPath= \"\\\"" + exe + "\\\"\" start= delayed-auto obj= \"NT AUTHORITY\\LocalService\" DisplayName= \"Maquim - Monitor de Computadores\"";
                    SC((exists ? "config " : "create ") + Service + parameters);
                    SC("description " + Service + " \"Informa IP, identificação, discos e localização ao Roteiro Maquim.\"");
                    SC("failure " + Service + " reset= 86400 actions= restart/60000/restart/60000/restart/60000");
                    using (var controller = new ServiceController(Service)) { controller.Start(); controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(45)); }
                } catch {
                    // Melhor esforco para recuperar o binario anterior, sem apagar configuracao ou fila.
                    if (exists) try {
                        using (var controller = new ServiceController(Service)) {
                            if (controller.Status != ServiceControllerStatus.Stopped) { controller.Stop(); controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(45)); }
                            if (replaced && previous != null) ComputerQueue.AtomicWrite(exe, previous, true);
                            if (previousRun == null) run.DeleteValue("MaquimComputerLocation", false); else run.SetValue("MaquimComputerLocation", previousRun);
                            if (wasRunning) controller.Start();
                        }
                    } catch { }
                    throw;
                }
            }
        }
    }
}
