using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Maquim.OfficeInstaller
{
    internal sealed class AgentSetupForm : Form
    {
        internal readonly ComboBox operation = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        internal readonly TextBox serial = new TextBox { MaxLength = 255, Dock = DockStyle.Fill };
        internal readonly TextBox asset = new TextBox { MaxLength = 100, Dock = DockStyle.Fill };
        readonly TextBox token = new TextBox { MaxLength = 1017, UseSystemPasswordChar = true, Dock = DockStyle.Fill };
        internal readonly TextBox confirmation = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Visible = false };
        readonly TextBox status = new TextBox { Multiline = true, ReadOnly = true, Height = 85, Dock = DockStyle.Fill };
        readonly TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9 };
        readonly Panel content = new Panel { Dock = DockStyle.Fill };
        readonly Button next = new Button { Text = "Avançar", AutoSize = true };
        readonly Button back = new Button { Text = "Voltar", AutoSize = true, Visible = false };
        readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill };
        readonly Func<MonitorConfig> load;
        MonitorConfig existing;
        AgentSetupPlan plan;
        bool running;
        public AgentSetupForm(bool update = false, Func<MonitorConfig> load = null) {
            this.load = load ?? (() => AgentSetupPlan.ReadExisting());
            Text = "Maquim — Agente de Computadores | " + Release.Version; Size = new Size(800, 650); MinimumSize = new Size(760, 620);
            Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterScreen;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.Controls.Add(new Label { Text = "Agente Maquim\nInstalação independente e atualização", AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold) }, 0, 0);
            operation.Items.AddRange(new object[] { "Instalar somente agente", "Atualizar/reparar agente instalado" });
            Control[] items = { new Label { Text = "Operação:", AutoSize = true }, operation, new Label { Text = "Número de série do computador (obrigatório):", AutoSize = true }, serial, new Label { Text = "Número de patrimônio (obrigatório):", AutoSize = true }, asset, new Label { Text = "Token de comunicação (opcional; vazio mantém o token existente):", AutoSize = true }, token, new Label { Text = "Sem token ou internet, o status fica na fila. Para configurar depois, execute este programa em Atualizar agente e informe o token.\nNa atualização, a identificação será carregada e preservada.", AutoSize = true, MaximumSize = new Size(710, 0) } };
            for (int i = 0; i < items.Length; i++) fields.Controls.Add(items[i], 0, i);
            content.Controls.Add(fields); content.Controls.Add(confirmation); layout.Controls.Add(content, 0, 1);
            layout.Controls.Add(status, 0, 2); layout.Controls.Add(progress, 0, 3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0,8,0,0) }; buttons.Controls.Add(next); buttons.Controls.Add(back); layout.Controls.Add(buttons,0,4);
            Controls.Add(layout);
            operation.SelectedIndexChanged += (s,e) => LoadMode(); operation.SelectedIndex = update ? 1 : 0;
            next.Click += async (s,e) => { if (plan == null) { try { GoForward(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); } } else await Execute(); };
            back.Click += (s,e) => GoBack();
            FormClosing += (s,e) => { if (running) { e.Cancel = true; MessageBox.Show(this, "Aguarde concluir a operação do agente.", Text); } };
        }
        void LoadMode() {
            if (running) return;
            try {
                existing = load();
                bool update = operation.SelectedIndex == 1;
                serial.ReadOnly = asset.ReadOnly = update;
                if (update && existing != null) { serial.Text = existing.serial_number; asset.Text = existing.asset_tag; }
                status.Text = update ? (existing == null ? "Agente não encontrado. Escolha Instalar somente agente." : "Configuração encontrada. IDs, identificação, token, estado e filas serão preservados.") : (existing == null ? "Preencha e confira série e patrimônio para instalar o agente." : "Já existe configuração. Selecione Atualizar/reparar para preservar os dados.");
            } catch { existing = null; status.Text = "Configuração existente inválida ou inacessível. A operação será bloqueada para preservar os dados."; }
        }
        internal void GoForward() {
            existing = load();
            plan = AgentSetupPlan.Create(operation.SelectedIndex == 1, existing, new TechnicianInput { SerialNumber = serial.Text, AssetTag = asset.Text });
            serial.Text = plan.Input.SerialNumber; asset.Text = plan.Input.AssetTag;
            confirmation.Text = plan.Confirmation() + "\n\nToken: " + (String.IsNullOrWhiteSpace(token.Text) ? "manter o existente; se não houver, enviar depois da configuração." : "atualizar com o valor informado nesta execução.");
            fields.Visible = false; confirmation.Visible = true; confirmation.BringToFront(); back.Visible = true; next.Text = plan.Update ? "Atualizar agente" : "Instalar agente";
        }
        internal void GoBack() { plan = null; confirmation.Visible = false; fields.Visible = true; fields.BringToFront(); back.Visible = false; next.Text = "Avançar"; }
        async Task Execute() {
            if (running || plan == null) return;
            running = true; content.Enabled = false; back.Enabled = next.Enabled = false; progress.Style = ProgressBarStyle.Marquee;
            string supplied = token.Text.Trim(); var selected = plan;
            try {
                await Task.Run(() => { if (selected.Update) MonitorInstaller.UpdateExisting(selected.Existing, supplied); else MonitorInstaller.InstallStandalone(selected.Input, supplied); });
                string result = selected.Update ? "Agente atualizado e em execução. Identificação e filas preservadas." : "Agente instalado e em execução.";
                if (String.IsNullOrEmpty(new MonitorStore().Token())) result += "\nEnvio pendente: configure o token em Atualizar agente.";
                result += "\nLocalização do Windows pode ser configurada pelo auxiliar no próximo login.\nConfira o recebimento em Instalações → Computadores → Monitoramento.";
                status.Text = result; MessageBox.Show(this, result, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                GoBack(); operation.SelectedIndex = 1; LoadMode();
            } catch (Exception ex) { status.Text = "Não foi possível concluir a operação do agente. " + ex.Message; MessageBox.Show(this, status.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { supplied = null; token.Clear(); running = false; content.Enabled = true; back.Enabled = next.Enabled = true; progress.Style = ProgressBarStyle.Blocks; if (plan == null) LoadMode(); }
        }
    }
    internal sealed class SetupModeDialog : Form
    {
        public int SelectedMode { get; private set; }
        public SetupModeDialog() {
            Text = "Maquim — Escolher operação"; ClientSize = new Size(680, 300); StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            Controls.Add(new Label { Left = 20, Top = 18, Width = 640, Height = 40, Text = "Selecione o que deseja executar neste computador:" });
            string[] labels = { "Instalar Office + agente", "Instalar somente agente", "Atualizar/reparar agente instalado" };
            for (int i = 0; i < labels.Length; i++) { int selected = i; var button = new Button { Left = 20, Top = 65 + i * 65, Width = 640, Height = 48, Text = labels[i] }; button.Click += (s,e) => { SelectedMode = selected; DialogResult = DialogResult.OK; Close(); }; Controls.Add(button); }
        }
    }
}
