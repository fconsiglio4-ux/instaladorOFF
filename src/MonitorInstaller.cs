using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
using Microsoft.Win32;
using System.Linq;

namespace Maquim.OfficeInstaller
{
    internal static class MonitorInstaller
    {
        const string Service = "MaquimComputerMonitor";
        internal static void UpdateTokenIfInstalled(string token) {
            var store = new MonitorStore();
            if (File.Exists(Path.Combine(store.Root, "config.json")) && !String.IsNullOrEmpty(token)) store.SaveToken(token);
        }
        static void SC(string args) {
            using (var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe"), args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })) {
                var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(30000)) { p.Kill(); throw new InvalidOperationException("Tempo excedido ao configurar o serviço."); }
                if (p.ExitCode != 0) throw new InvalidOperationException("Falha ao configurar o serviço de monitoramento (código " + p.ExitCode + ").");
            }
        }
        public static void Install(Guid installationId, TechnicianInput input, string token) {
            bool exists = false;
            foreach (var service in ServiceController.GetServices()) using (service) { if (service.ServiceName == Service) exists = true; }
            if (exists) using (var controller = new ServiceController(Service)) {
                if (controller.Status != ServiceControllerStatus.Stopped) { controller.Stop(); controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(45)); }
            }
            try {
            var store = new MonitorStore(); store.Configure(installationId, input, token);
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Maquim", "ComputerMonitor");
            Directory.CreateDirectory(directory);
            string exe = Path.Combine(directory, "Maquim-ComputerMonitor.exe");
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Maquim-ComputerMonitor.exe")) {
                if (resource == null) throw new InvalidOperationException("Serviço ausente no instalador.");
                using (var memory = new MemoryStream()) { resource.CopyTo(memory); ComputerQueue.AtomicWrite(exe, memory.ToArray(), true); }
            }
            string helper = Path.Combine(directory, "Maquim-ComputerLocation-" + Release.Version + ".exe");
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Maquim-ComputerLocation.exe")) {
                if (resource == null) throw new InvalidOperationException("Auxiliar de localização ausente.");
                using (var memory = new MemoryStream()) { resource.CopyTo(memory); var bytes = memory.ToArray(); if (!File.Exists(helper) || !File.ReadAllBytes(helper).SequenceEqual(bytes)) ComputerQueue.AtomicWrite(helper, bytes, true); }
            }
            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
            using (var run = machine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"))
                run.SetValue("MaquimComputerLocation", "\"" + helper + "\"", RegistryValueKind.String);
            string parameters = " binPath= \"\\\"" + exe + "\\\"\" start= delayed-auto obj= \"NT AUTHORITY\\LocalService\" DisplayName= \"Maquim - Monitor de Computadores\"";
            SC((exists ? "config " : "create ") + Service + parameters);
            SC("description " + Service + " \"Informa IP, identificação e espaço de disco ao Roteiro Maquim a cada 12 horas e em alterações de IP.\"");
            SC("failure " + Service + " reset= 86400 actions= restart/60000/restart/60000/restart/60000");
            using (var controller = new ServiceController(Service)) { controller.Start(); controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(45)); }
            } catch {
                if (exists) try { using (var controller = new ServiceController(Service)) { if (controller.Status == ServiceControllerStatus.Stopped) controller.Start(); } } catch { }
                throw;
            }
        }
    }
}
