using System;
using System.Drawing;
using System.Windows.Forms;

namespace Maquim.OfficeInstaller
{
    internal sealed class AccountPasswordDialog : Form
    {
        internal readonly TextBox password = new TextBox { UseSystemPasswordChar = true, MaxLength = 256, Dock = DockStyle.Fill };
        internal readonly TextBox confirmation = new TextBox { UseSystemPasswordChar = true, MaxLength = 256, Dock = DockStyle.Fill };
        string selectedPassword;

        public AccountPasswordDialog()
        {
            Text = "Criar conta local Maquim — Administrador";
            Size = new Size(630, 360); FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 10);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 6 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "A conta local Maquim será criada com permissões de administrador.\nInforme e confirme a senha para este computador.\nEla será usada apenas nesta execução e não será salva pelo instalador." }, 0, 0);
            layout.Controls.Add(new Label { Text = "Senha:", AutoSize = true }, 0, 1); layout.Controls.Add(password, 0, 2);
            layout.Controls.Add(new Label { Text = "Confirme a senha:", AutoSize = true }, 0, 3); layout.Controls.Add(confirmation, 0, 4);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0,12,0,0) };
            var submit = new Button { Text = "Criar conta e continuar", AutoSize = true };
            var cancel = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
            submit.Click += delegate { try { PrepareSubmission(); DialogResult = DialogResult.OK; } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); } };
            buttons.Controls.Add(submit); buttons.Controls.Add(cancel); layout.Controls.Add(buttons,0,5);
            Controls.Add(layout); AcceptButton = submit; CancelButton = cancel;
        }
        internal static void ValidatePassword(string value, string repeated)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Informe uma senha para criar a conta Maquim.");
            if (value.Length > 256) throw new InvalidOperationException("A senha deve ter até 256 caracteres.");
            if (!String.Equals(value, repeated, StringComparison.Ordinal)) throw new InvalidOperationException("A confirmação da senha não corresponde.");
        }
        internal void PrepareSubmission() { ValidatePassword(password.Text, confirmation.Text); selectedPassword = password.Text; password.Clear(); confirmation.Clear(); }
        internal string TakePassword() { string result = selectedPassword; selectedPassword = null; return result; }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { selectedPassword = null; if (!password.IsDisposed) password.Clear(); if (!confirmation.IsDisposed) confirmation.Clear(); }
            base.Dispose(disposing);
        }
    }
}
