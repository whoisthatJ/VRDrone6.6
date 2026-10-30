using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GateRush.Input
{
    /// <summary>Per-device radio calibrations (keyed by name + VID/PID), saved as JSON in persistentDataPath/radios.json.</summary>
    public sealed class RadioCalibrationStore
    {
        [Serializable]
        public sealed class Entry
        {
            public string DeviceKey;
            public string Date;
            public RadioMapping Mapping;
        }

        [Serializable]
        private sealed class Data
        {
            public List<Entry> Radios = new List<Entry>();
        }

        private Data _data = new Data();

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, "radios.json");

        public IReadOnlyList<Entry> Entries => _data.Radios;

        public RadioMapping Find(string deviceKey)
        {
            foreach (var e in _data.Radios)
                if (e.DeviceKey == deviceKey) return e.Mapping;
            return null;
        }

        public void Set(string deviceKey, RadioMapping mapping)
        {
            Remove(deviceKey);
            _data.Radios.Add(new Entry { DeviceKey = deviceKey, Mapping = mapping.Clone(), Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm") });
        }

        public bool Remove(string deviceKey) => _data.Radios.RemoveAll(e => e.DeviceKey == deviceKey) > 0;

        public string ToJson() => JsonUtility.ToJson(_data, true);

        public void FromJson(string json)
        {
            _data = string.IsNullOrEmpty(json) ? new Data() : JsonUtility.FromJson<Data>(json) ?? new Data();
            _data.Radios ??= new List<Entry>();
        }

        public void Load(string path)
        {
            try
            {
                FromJson(File.Exists(path) ? File.ReadAllText(path) : null);
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Debug.LogWarning($"[GateRush] Radio calibrations unreadable ({e.Message}); starting fresh");
                _data = new Data();
            }
        }

        public void Save(string path)
        {
            try
            {
                File.WriteAllText(path, ToJson());
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[GateRush] Could not save radio calibrations: {e.Message}");
            }
        }
    }
}
