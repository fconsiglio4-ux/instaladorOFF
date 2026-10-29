using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Maquim.OfficeInstaller
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerForm());
        }
    }

    sealed class InstallerForm : Form
    {
        readonly TextBox media = new TextBox { Dock = DockStyle.Fill };
        readonly ComboBox architecture = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        readonly ComboBox language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        readonly CheckBox activate = new CheckBox { Text = "Ativar após instalar — Ohook 3.12 (requer internet)", AutoSize = true, Checked = true };
        readonly CheckBox accept = new CheckBox { Text = "Li e aceito os termos de licença da Microsoft para instalar o Office.", AutoSize = true };
        readonly Button install = new Button { Text = "Instalar Office", AutoSize = true, Height = 36 };
        readonly Button logs = new Button { Text = "Abrir registros", AutoSize = true, Enabled = false };
        readonly TextBox output = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9) };
        readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Blocks };
        readonly Panel options = new Panel { Dock = DockStyle.Fill };
        bool running;
        string work;

        public InstallerForm()
        {
            Text = "Maquim — Instalador Office 365 | 1.0.0";
            Size = new Size(850, 650);
            MinimumSize = new Size(800, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 6 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 184));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.Controls.Add(new Label { Text = "Microsoft 365 Apps\nInstalação pela mídia local • O365ProPlusRetail", AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold) }, 0, 0);
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5 };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            fields.Controls.Add(media, 0, 0);
            var browse = new Button { Text = "Imagem…", AutoSize = true };
            var folder = new Button { Text = "Pasta…", AutoSize = true };
            fields.Controls.Add(browse, 1, 0); fields.Controls.Add(folder, 2, 0);
            var selection = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            architecture.Items.AddRange(new object[] { "64 bits", "32 bits" }); architecture.SelectedIndex = Environment.Is64BitOperatingSystem ? 0 : 1;
            language.Items.AddRange(new object[] { "pt-br", "en-us", "es-es" }); language.SelectedIndex = 0;
            selection.Controls.Add(new Label { Text = "Arquitetura:", AutoSize = true }); selection.Controls.Add(architecture);
            selection.Controls.Add(new Label { Text = "Idioma:", AutoSize = true }); selection.Controls.Add(language);
            fields.Controls.Add(selection, 0, 1); fields.SetColumnSpan(selection, 3);
            fields.Controls.Add(activate, 0, 2); fields.SetColumnSpan(activate, 3);
            fields.Controls.Add(new Label { Text = "Ohook altera componentes de licenciamento do Office. Desmarque para usar sua conta licenciada.", AutoSize = true, ForeColor = Color.DimGray }, 0, 3); fields.SetColumnSpan(fields.GetControlFromPosition(0, 3), 3);
            fields.Controls.Add(accept, 0, 4); fields.SetColumnSpan(accept, 3);
            options.Controls.Add(fields); layout.Controls.Add(options, 0, 1);
            layout.Controls.Add(output, 0, 2); layout.Controls.Add(progress, 0, 3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            buttons.Controls.Add(install); buttons.Controls.Add(logs); layout.Controls.Add(buttons, 0, 4);
            layout.Controls.Add(new Label { Text = "A imagem permanece fora do executável. Não há reinicialização automática.", AutoSize = true, ForeColor = Color.DimGray }, 0, 5);
            Controls.Add(layout);
            browse.Click += delegate { using (var dialog = new OpenFileDialog { Filter = "Mídia Office|*.img;*.iso", InitialDirectory = AppDomain.CurrentDomain.BaseDirectory }) if (dialog.ShowDialog() == DialogResult.OK) media.Text = dialog.FileName; };
            folder.Click += delegate { using (var dialog = new FolderBrowserDialog { Description = "Selecione a pasta que contém Office\\Data" }) if (dialog.ShowDialog() == DialogResult.OK) media.Text = dialog.SelectedPath; };
            logs.Click += delegate { if (work != null) Process.Start("explorer.exe", Quote(work)); };
            install.Click += async delegate { await Install(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (running) { e.Cancel = true; MessageBox.Show(this, "Aguarde a conclusão do processo antes de fechar.", Text); } };
            var candidates = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory).Where(p => new[] { ".img", ".iso" }.Contains(Path.GetExtension(p).ToLowerInvariant())).ToArray();
            if (candidates.Length == 1) media.Text = candidates[0];
            Append("Coloque o IMG ao lado deste executável ou selecione uma pasta extraída que contenha Office\\Data.");
        }

        static string Quote(string value) { return "\"" + value + "\""; }
        void Append(string text)
        {
            if (String.IsNullOrEmpty(text) || IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(Append), text); return; }
            output.AppendText(text + Environment.NewLine);
        }

        async Task Install()
        {
            if (!File.Exists(media.Text) && !Directory.Exists(media.Text)) { MessageBox.Show(this, "Selecione a mídia do Office.", Text); return; }
            if (!accept.Checked) { MessageBox.Show(this, "É necessário aceitar os termos de licença para instalar.", Text); return; }
            running = true; options.Enabled = false; install.Enabled = false; progress.Style = ProgressBarStyle.Marquee;
            try
            {
                work = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MaquimOffice", "Runs", Guid.NewGuid().ToString("N"));
                var security = new DirectorySecurity();
                security.SetAccessRuleProtection(true, false);
                foreach (var sid in new[] { new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), WindowsIdentity.GetCurrent().User })
                    security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                Directory.CreateDirectory(work, security);
                foreach (var file in new[] { "Install-Office.ps1", "OfficeMedia.psm1", "setup.exe" })
                    using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(file))
                    using (var target = File.Create(Path.Combine(work, file)))
                    { if (resource == null) throw new InvalidOperationException("Recurso ausente: " + file); resource.CopyTo(target); }
                logs.Enabled = true;
                var arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote(Path.Combine(work, "Install-Office.ps1")) + " -MediaPath " + Quote(Path.GetFullPath(media.Text)) + " -WorkDirectory " + Quote(work) + " -Architecture " + (architecture.SelectedIndex == 0 ? "64" : "32") + " -Language " + language.SelectedItem;
                if (activate.Checked) arguments += " -Activate";
                var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"), arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = work };
                int exit = await Task.Run(() => {
                    using (var process = new Process { StartInfo = start }) {
                        process.OutputDataReceived += (s, e) => Append(e.Data);
                        process.ErrorDataReceived += (s, e) => Append(e.Data);
                        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit(); return process.ExitCode;
                    }
                });
                Append("Registros: " + work);
                MessageBox.Show(this, exit == 0 ? "Processo concluído. Consulte o resultado no registro." : exit == 3010 ? "Office instalado. Reinicie o Windows antes da ativação." : "O processo encontrou um erro. Consulte os registros.", Text, MessageBoxButtons.OK, exit == 0 || exit == 3010 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex) { Append("ERRO: " + ex.Message); MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { running = false; options.Enabled = true; install.Enabled = true; progress.Style = ProgressBarStyle.Blocks; }
        }
    }
}
