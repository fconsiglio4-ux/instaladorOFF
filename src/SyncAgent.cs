using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Maquim.OfficeInstaller
{
    internal static class SyncAgent
    {
        internal static string InstallFiles(ComputerQueue queue)
        {
            string directory = Path.Combine(queue.Root, "agent"); Directory.CreateDirectory(directory);
            foreach (string name in new[] { "Maquim-ComputerSync.exe", "Register-ComputerSync.ps1" })
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var memory = new MemoryStream())
            {
                if (stream == null) throw new InvalidOperationException("Componente de reenvio ausente: " + name);
                stream.CopyTo(memory); byte[] bytes = memory.ToArray(); string path = Path.Combine(directory, name);
                if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes)) ComputerQueue.AtomicWrite(path, bytes, true);
            }
            return directory;
        }
        public static void Register(ComputerQueue queue)
        {
            string agent = InstallFiles(queue);
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"), "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + Path.Combine(agent, "Register-ComputerSync.ps1") + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = new Process { StartInfo = start })
            {
                p.OutputDataReceived += (s,e) => { }; p.ErrorDataReceived += (s,e) => { };
                p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
                if (!p.WaitForExit(30000)) { p.Kill(); throw new InvalidOperationException("Tempo esgotado ao registrar reenvio agendado."); }
                p.WaitForExit();
                if (p.ExitCode != 0) throw new InvalidOperationException("Não foi possível registrar a tarefa de reenvio nesta conta Windows. Consulte a política do Agendador de Tarefas.");
            }
        }
    }
}
