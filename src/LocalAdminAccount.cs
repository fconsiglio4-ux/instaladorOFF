using System;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;

namespace Maquim.OfficeInstaller
{
    internal interface ILocalAccountStore : IDisposable
    {
        bool Exists();
        void Create(string password);
        void EnsureAdministrator();
        void DeleteCreatedAccount();
    }

    internal static class LocalAdminAccount
    {
        internal const string Name = "Maquim";
        internal static string EmbeddedPassword()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Maquim.LocalAdminPassword"))
            {
                if (stream == null) throw new InvalidOperationException("Senha da conta Maquim não foi incorporada nesta compilação.");
                using (var reader = new StreamReader(stream, new UTF8Encoding(false, true))) {
                    string value = reader.ReadToEnd();
                    if (String.IsNullOrEmpty(value)) throw new InvalidOperationException("Senha da conta Maquim ausente na compilação.");
                    return value;
                }
            }
        }

        internal static string Ensure(ILocalAccountStore store, Func<string> getPassword)
        {
            bool created = false;
            try {
                bool exists = store.Exists();
                if (!exists) {
                    string password = getPassword();
                    if (String.IsNullOrEmpty(password)) throw new InvalidOperationException("Senha da conta Maquim ausente na compilação.");
                    store.Create(password); created = true;
                }
                store.EnsureAdministrator();
                return exists ? "Conta local Maquim já existente: senha preservada e associação a Administradores verificada." : "Conta local Maquim criada e associação a Administradores verificada.";
            }
            catch {
                if (created) { try { store.DeleteCreatedAccount(); } catch { throw new InvalidOperationException("Falha ao conceder administração à conta Maquim e ao remover a conta recém-criada. Verifique os usuários locais."); } }
                throw new InvalidOperationException("Não foi possível preparar a conta local Maquim. Verifique permissões de administrador, política de senhas e estado da conta. A instalação do Office não foi iniciada.");
            }
        }

        public static string Ensure()
        {
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw new InvalidOperationException("Execute o instalador como administrador para criar a conta Maquim.");
            using (var store = new WindowsLocalAccountStore()) return Ensure(store, EmbeddedPassword);
        }
    }

    internal sealed class WindowsLocalAccountStore : ILocalAccountStore
    {
        readonly PrincipalContext context = new PrincipalContext(ContextType.Machine, Environment.MachineName);
        public bool Exists() { using (var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, LocalAdminAccount.Name)) return user != null; }
        public void Create(string password)
        {
            using (var user = new UserPrincipal(context, LocalAdminAccount.Name, password, true)) {
                user.Description = "Conta local de suporte Maquim";
                user.Save();
            }
        }
        public void EnsureAdministrator()
        {
            using (var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, LocalAdminAccount.Name))
            using (var administrators = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, "S-1-5-32-544"))
            {
                if (user == null || administrators == null || user.Enabled != true) throw new InvalidOperationException("Conta Maquim ausente/desabilitada ou grupo Administradores indisponível.");
                if (!administrators.Members.Contains(user)) { administrators.Members.Add(user); administrators.Save(); }
                if (!user.IsMemberOf(administrators)) throw new InvalidOperationException("Associação a Administradores não confirmada.");
            }
        }
        public void DeleteCreatedAccount() { using (var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, LocalAdminAccount.Name)) if (user != null) user.Delete(); }
        public void Dispose() { context.Dispose(); }
    }
}
