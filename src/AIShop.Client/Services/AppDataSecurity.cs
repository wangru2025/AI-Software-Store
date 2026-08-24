using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AIShop.Client.Services
{
    public static class AppDataSecurity
    {
        public static void EnsureUsersCanModifyFile(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                    EnsureUsersCanModifyDirectory(dir);
                }

                if (!File.Exists(path))
                {
                    return;
                }

                var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                var security = File.GetAccessControl(path);
                security.AddAccessRule(new FileSystemAccessRule(
                    users,
                    FileSystemRights.Modify,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                File.SetAccessControl(path, security);
            }
            catch
            {
            }
        }

        private static void EnsureUsersCanModifyDirectory(string path)
        {
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var security = Directory.GetAccessControl(path);
            security.AddAccessRule(new FileSystemAccessRule(
                users,
                FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            Directory.SetAccessControl(path, security);
        }
    }
}
