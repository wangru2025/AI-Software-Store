using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIShop.Shared;
using Newtonsoft.Json;

namespace AIShop.Client.Services
{
    public sealed class InstalledPackageStore
    {
        private readonly string _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AI软件商店",
            "installed.json");

        public IReadOnlyList<InstalledPackage> ReadAll()
        {
            if (!File.Exists(_path))
            {
                return new List<InstalledPackage>();
            }

            var text = File.ReadAllText(_path);
            return JsonConvert.DeserializeObject<List<InstalledPackage>>(text) ?? new List<InstalledPackage>();
        }

        public InstalledPackage Find(string id)
        {
            return ReadAll().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public void Save(InstalledPackage package)
        {
            var list = ReadAll().ToList();
            list.RemoveAll(x => string.Equals(x.Id, package.Id, StringComparison.OrdinalIgnoreCase));
            list.Add(package);
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, JsonConvert.SerializeObject(list, Formatting.Indented));
        }

        public void Remove(string id)
        {
            var list = ReadAll().Where(x => !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, JsonConvert.SerializeObject(list, Formatting.Indented));
        }
    }
}
