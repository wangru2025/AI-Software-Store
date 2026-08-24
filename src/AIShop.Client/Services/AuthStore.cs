using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using AIShop.Shared;

namespace AIShop.Client.Services
{
    public sealed class AuthStore
    {
        private readonly string _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AI软件商店",
            "auth.json");

        public PersistedAuth Load()
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var json = File.ReadAllText(_path);
            return JsonConvert.DeserializeObject<PersistedAuth>(json);
        }

        public void Save(string token, UserSession user)
        {
            if (string.IsNullOrWhiteSpace(token) || user == null)
            {
                Clear();
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var payload = new PersistedAuth
            {
                Token = token,
                User = user
            };
            File.WriteAllText(_path, JsonConvert.SerializeObject(payload, Formatting.Indented));
        }

        public void Clear()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }
}
