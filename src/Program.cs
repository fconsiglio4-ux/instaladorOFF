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

    internal sealed class InstallerForm : Form
    {
        internal readonly TextBox media = new TextBox { Dock = DockStyle.Fill };
        internal readonly TextBox serial = new TextBox { Dock = DockStyle.Fill, MaxLength = 255, Name = "serialNumber" };
        internal readonly TextBox asset = new TextBox { Dock = DockStyle.Fill, MaxLength = 100, Name = "assetTag" };
        readonly ComboBox architecture = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        readonly ComboBox language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        readonly CheckBox activate = new CheckBox { Text = "Ativar após instalar — Ohook 3.12 (requer internet)", AutoSize = true, Checked = true };
        internal readonly CheckBox accept = new CheckBox { Text = "Li e aceito os termos de licença da Microsoft para instalar o Office.", AutoSize = true };
        readonly Button install = new Button { Text = "Avançar", AutoSize = true, Height = 36 };
        readonly Button back = new Button { Text = "Voltar", AutoSize = true, Visible = false };
        readonly Button logs = new Button { Text = "Abrir registros", AutoSize = true, Enabled = false };
        readonly Button integration = new Button { Text = "Configurar integração", AutoSize = true };
        readonly TextBox output = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9) };
        readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Blocks };
        readonly Panel options = new Panel { Dock = DockStyle.Fill };
        readonly TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 9 };
        internal readonly TextBox confirmation = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        readonly Label syncStatus = new Label { Dock = DockStyle.Fill, AutoSize = false, ForeColor = Color.DimGray };
        internal bool Confirming { get; private set; }
        bool running, integrationBusy;
        string work;
        ComputerQueue queue;
        string setupProblem;

        public InstallerForm()
        {
            Text = "Maquim — Instalador Office 365 | " + Release.Version;
            Size = new Size(960, 820); MinimumSize = new Size(900, 760);
            StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 7 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 290));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.Controls.Add(new Label { Text = "Microsoft 365 Apps\nIdentificação do computador → conferência → instalação", AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold) }, 0, 0);
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            fields.Controls.Add(media, 0, 0);
            var browse = new Button { Text = "Imagem…", AutoSize = true };
            var folder = new Button { Text = "Pasta…", AutoSize = true };
            fields.Controls.Add(browse, 1, 0); fields.Controls.Add(folder, 2, 0);
            AddFull(new Label { Text = "Número de série do computador (obrigatório):", AutoSize = true }, 1);
            AddFull(serial, 2);
            AddFull(new Label { Text = "Número de patrimônio (obrigatório):", AutoSize = true }, 3);
            AddFull(asset, 4);
            var selection = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            architecture.Items.AddRange(new object[] { "64 bits", "32 bits" }); architecture.SelectedIndex = Environment.Is64BitOperatingSystem ? 0 : 1;
            language.Items.AddRange(new object[] { "pt-br", "en-us", "es-es" }); language.SelectedIndex = 0;
            selection.Controls.Add(new Label { Text = "Arquitetura:", AutoSize = true }); selection.Controls.Add(architecture);
            selection.Controls.Add(new Label { Text = "Idioma:", AutoSize = true }); selection.Controls.Add(language);
            AddFull(selection, 5); AddFull(activate, 6);
            AddFull(new Label { Text = "Ohook altera componentes de licenciamento do Office. Desmarque para usar sua conta licenciada.", AutoSize = true, ForeColor = Color.DimGray }, 7);
            AddFull(accept, 8);
            options.Controls.Add(fields); options.Controls.Add(confirmation); confirmation.Visible = false;
            layout.Controls.Add(options, 0, 1); layout.Controls.Add(output, 0, 2); layout.Controls.Add(progress, 0, 3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            buttons.Controls.Add(install); buttons.Controls.Add(back); buttons.Controls.Add(logs); buttons.Controls.Add(integration);
            layout.Controls.Add(buttons, 0, 4); layout.Controls.Add(syncStatus, 0, 5);
            layout.Controls.Add(new Label { Text = "A imagem permanece fora do executável. Não há reinicialização automática.", AutoSize = true, ForeColor = Color.DimGray }, 0, 6);
            Controls.Add(layout);
            browse.Click += delegate { using (var dialog = new OpenFileDialog { Filter = "Mídia Office|*.img;*.iso", InitialDirectory = AppDomain.CurrentDomain.BaseDirectory }) if (dialog.ShowDialog() == DialogResult.OK) media.Text = dialog.FileName; };
            folder.Click += delegate { using (var dialog = new FolderBrowserDialog { Description = "Selecione a pasta que contém Office\\Data" }) if (dialog.ShowDialog() == DialogResult.OK) media.Text = dialog.SelectedPath; };
            logs.Click += delegate { if (work != null) Process.Start("explorer.exe", Quote(work)); };
            install.Click += async delegate {
                if (!Confirming) { try { GoForward(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); } }
                else await Install();
            };
            back.Click += delegate { GoBack(); };
            integration.Click += async delegate { await ConfigureIntegration(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (running || integrationBusy) { e.Cancel = true; MessageBox.Show(this, "Aguarde a conclusão do processo antes de fechar.", Text); } };
            var candidates = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory).Where(p => new[] { ".img", ".iso" }.Contains(Path.GetExtension(p).ToLowerInvariant())).ToArray();
            if (candidates.Length == 1) media.Text = candidates[0];
            Append("Informe o número de série do computador e o patrimônio. A versão real do Office será verificada ao concluir.");
            Shown += async delegate { await InitializeIntegration(); };
        }
        void AddFull(Control control, int row) { fields.Controls.Add(control, 0, row); fields.SetColumnSpan(control, 3); }
        internal void GoForward()
        {
            var values = new TechnicianInput { SerialNumber = serial.Text, AssetTag = asset.Text }; values.Validate();
            if (!File.Exists(media.Text) && !Directory.Exists(media.Text)) throw new InvalidOperationException("Selecione a mídia do Office.");
            if (!accept.Checked) throw new InvalidOperationException("É necessário aceitar os termos de licença para instalar.");
            serial.Text = values.SerialNumber; asset.Text = values.AssetTag;
            confirmation.Text = "Confira os dados antes de instalar:\n\nNúmero de série do computador: " + values.SerialNumber + "\nNúmero de patrimônio: " + values.AssetTag + "\n\nComputador: " + Environment.MachineName + "\nMídia: " + media.Text + "\nOffice: O365ProPlusRetail / " + architecture.SelectedItem + " / " + language.SelectedItem + "\nAtivação após instalar: " + (activate.Checked ? "selecionada" : "não selecionada") + "\n\nUse Voltar para corrigir os dados. A edição e a versão efetivas serão coletadas após a instalação.";
            fields.Visible = false; confirmation.Visible = true; confirmation.BringToFront();
            Confirming = true; back.Visible = true; install.Text = "Instalar Office";
        }
        internal void GoBack()
        {
            Confirming = false; confirmation.Visible = false; fields.Visible = true; fields.BringToFront();
            back.Visible = false; install.Text = "Avançar";
        }
        static string Quote(string value) { return "\"" + value + "\""; }
        void Append(string text)
        {
            if (String.IsNullOrEmpty(text) || IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(Append), text); return; }
            output.AppendText(text + Environment.NewLine);
        }
        void RefreshStatus()
        {
            if (queue == null) { syncStatus.Text = setupProblem ?? "Integração aguardando configuração."; return; }
            string statusFile = Path.Combine(queue.Root, "status.txt");
            syncStatus.Text = File.Exists(statusFile) ? File.ReadAllText(statusFile) : "Integração configurada. Conta Windows: " + WindowsIdentity.GetCurrent().Name;
            int pending = Directory.GetFiles(queue.QueueDirectory, "*.json").Length;
            int blocked = Directory.GetFiles(queue.QueueDirectory, "*.blocked").Length;
            syncStatus.Text += "\nPendentes: " + pending + "; bloqueados por formato/tamanho: " + blocked + (File.Exists(Path.Combine(queue.Root, "auth-paused")) ? "; token rejeitado: corrija a configuração." : ".");
            if (!String.IsNullOrEmpty(setupProblem)) syncStatus.Text = setupProblem;
        }
        async Task InitializeIntegration()
        {
            integrationBusy = true; install.Enabled = false; integration.Enabled = false;
            try {
                queue = new ComputerQueue();
                await Task.Run(() => { queue.ImportEnvironmentToken(); SyncAgent.Register(queue); queue.Retry(); });
                setupProblem = null;
                Append("Token incorporado configurado automaticamente quando não há substituição salva. Reenvio a cada 15 minutos e no login, sob " + WindowsIdentity.GetCurrent().Name + ".");
            } catch { setupProblem = "Não foi possível preparar o reenvio. Use Configurar integração antes de instalar."; Append(setupProblem); }
            finally { integrationBusy = false; install.Enabled = true; integration.Enabled = true; RefreshStatus(); }
        }
        async Task ConfigureIntegration()
        {
            if (running || integrationBusy) return;
            using (var dialog = new Form { Text = "Integração com Roteiro Maquim", StartPosition = FormStartPosition.CenterParent, Size = new Size(690, 310), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
            {
                var label = new Label { Left = 18, Top = 15, Width = 640, Height = 70, Text = "O token padrão é incorporado durante a compilação. Este campo permite substituí-lo.\nSerá protegido para a conta Windows atual. Deixe vazio para manter o token salvo.\nDestino: Instalações → Computadores." };
                var token = new TextBox { Left = 18, Top = 90, Width = 640, UseSystemPasswordChar = true, MaxLength = 4096 };
                var save = new Button { Left = 430, Top = 200, Width = 230, Text = "Salvar e tentar enviar", DialogResult = DialogResult.OK };
                var retryBlocked = new CheckBox { Left = 18, Top = 130, Width = 630, Height = 50, Text = "Reavaliar registros bloqueados por formato/tamanho (após corrigir a causa)." };
                dialog.Controls.AddRange(new Control[] { label, token, save, retryBlocked }); dialog.AcceptButton = save;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                integrationBusy = true; integration.Enabled = false; install.Enabled = false;
                try {
                    if (queue == null) queue = new ComputerQueue();
                    string supplied = token.Text; bool unblock = retryBlocked.Checked;
                    await Task.Run(() => { if (!String.IsNullOrWhiteSpace(supplied)) queue.SaveToken(supplied); else queue.ImportEnvironmentToken(); SyncAgent.Register(queue); if (unblock) queue.RetryBlocked(); queue.Retry(); });
                    token.Clear(); setupProblem = null;
                } catch { setupProblem = "Falha na configuração da integração. Verifique permissões da conta e do Agendador de Tarefas."; }
                finally { integrationBusy = false; integration.Enabled = true; install.Enabled = true; RefreshStatus(); }
            }
        }
        async Task Install()
        {
            if (!Confirming || running) return;
            var input = new TechnicianInput { SerialNumber = serial.Text, AssetTag = asset.Text };
            try { input.Validate(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); return; }
            if (queue == null || setupProblem != null) { MessageBox.Show(this, "Prepare o reenvio em Configurar integração antes de iniciar. O token pode ser configurado depois; a fila será preservada offline.", Text); return; }
            running = true; options.Enabled = false; install.Enabled = false; back.Enabled = false; integration.Enabled = false; progress.Style = ProgressBarStyle.Marquee;
            Guid installationId = Guid.NewGuid();
            bool officeConfirmed = false, queued = false;
            try
            {
                work = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MaquimOffice", "Runs", installationId.ToString("N"));
                var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
                foreach (var sid in new[] { new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), WindowsIdentity.GetCurrent().User })
                    security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                Directory.CreateDirectory(work, security);
                foreach (var file in new[] { "Install-Office.ps1", "OfficeMedia.psm1", "setup.exe" })
                    using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(file))
                    using (var target = File.Create(Path.Combine(work, file)))
                    { if (resource == null) throw new InvalidOperationException("Recurso ausente: " + file); resource.CopyTo(target); }
                logs.Enabled = true;
                var arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote(Path.Combine(work, "Install-Office.ps1")) + " -MediaPath " + Quote(Path.GetFullPath(media.Text)) + " -WorkDirectory " + Quote(work) + " -Architecture " + (architecture.SelectedIndex == 0 ? "64" : "32") + " -Language " + language.SelectedItem + " -InstallationId " + installationId.ToString("D");
                if (activate.Checked) arguments += " -Activate";
                var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"), arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = work };
                int exit = await Task.Run(() => {
                    using (var process = new Process { StartInfo = start }) {
                        process.OutputDataReceived += (s, e) => Append(e.Data); process.ErrorDataReceived += (s, e) => Append(e.Data);
                        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit(); return process.ExitCode;
                    }
                });
                string flag = Path.Combine(work, "office-installed.id");
                if (!File.Exists(flag) || File.ReadAllText(flag).Trim() != installationId.ToString("D")) throw new InvalidOperationException("A instalação não foi confirmada pelo instalador Microsoft. Nenhum registro concluído foi enviado. Consulte os registros.");
                var snapshot = await Task.Run(() => MachineCollector.Capture());
                officeConfirmed = true;
                var record = snapshot.Record(installationId, input, DateTimeOffset.Now);
                // A fila duravel e gravada antes da primeira tentativa de rede.
                queue.Enqueue(record);
                queued = true;
                Append("Office confirmado: " + snapshot.OfficeVersion);
                Append("Registro salvo na fila de computadores: " + installationId);
                try { await Task.Run(() => queue.Retry(null, record.installation_id)); } catch { Append("Instalação concluída; envio dos dados pendente."); }
                string result = queue.IsPending(record.installation_id) ? "Instalação concluída; envio dos dados pendente" : "Instalação concluída; dados recebidos pelo Roteiro Maquim.";
                Append(result); Append("Registros: " + work); RefreshStatus();
                if (exit == 3010) result += "\nReinicie o Windows antes da ativação.";
                else if (exit != 0) result += "\nA etapa posterior à instalação encontrou um erro. Consulte installation.log.";
                MessageBox.Show(this, result, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                GoBack();
            }
            catch (Exception ex) {
                string message = queued ? "Instalação concluída; envio dos dados pendente. O registro está preservado na fila. " + ex.Message : officeConfirmed ? "Office instalado, mas não foi possível salvar os dados na fila. Preserve esta tela e os registros. " + ex.Message : "Não foi possível confirmar o resultado completo da instalação; nenhum registro concluído foi enviado. " + ex.Message;
                Append("ERRO: " + message); MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { running = false; options.Enabled = true; install.Enabled = true; back.Enabled = true; integration.Enabled = true; progress.Style = ProgressBarStyle.Blocks; }
        }
    }
}
