using System;
using System.Windows.Forms;
namespace Maquim.OfficeInstaller
{
    internal static class AgentSetupProgram
    {
        [STAThread]
        static void Main() {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            bool update = false; try { update = AgentSetupPlan.ReadExisting() != null; } catch { update = true; }
            Application.Run(new AgentSetupForm(update));
        }
    }
}
