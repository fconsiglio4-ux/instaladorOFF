using System;
using System.Linq;

namespace Maquim.OfficeInstaller
{
    internal static class ComputerSyncProgram
    {
        [STAThread]
        static int Main(string[] args)
        {
            try {
                var queue = new ComputerQueue();
                queue.ImportEnvironmentToken();
                if (args.Contains("--retry-blocked")) queue.RetryBlocked();
                queue.Retry(); return 0;
            } catch { return 1; } // Nunca registra excecoes que possam carregar headers ou token.
        }
    }
}
