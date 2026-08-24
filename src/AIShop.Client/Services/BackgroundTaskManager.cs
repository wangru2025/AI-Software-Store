using System;
using System.Collections.Generic;
using System.Linq;

namespace AIShop.Client.Services
{
    public static class BackgroundTaskManager
    {
        private static readonly object SyncRoot = new object();
        private static readonly List<BackgroundTask> Tasks = new List<BackgroundTask>();

        public static event EventHandler Changed;

        public static IReadOnlyList<BackgroundTask> All()
        {
            lock (SyncRoot)
            {
                return Tasks.ToList();
            }
        }

        public static bool HasRunningTasks()
        {
            lock (SyncRoot)
            {
                return Tasks.Any(x => !x.IsFinished);
            }
        }

        public static void Add(BackgroundTask task)
        {
            lock (SyncRoot)
            {
                if (!Tasks.Contains(task))
                {
                    Tasks.Add(task);
                }
            }
            NotifyChanged();
        }

        public static void NotifyChanged()
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
