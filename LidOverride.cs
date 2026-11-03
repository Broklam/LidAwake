using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LidAwake
{
    internal enum RefreshResult { Unchanged, TimerExpired, SettingsChanged }

    internal sealed class LidOverride
    {
        private sealed class Snapshot
        {
            internal readonly Guid Plan;
            internal readonly uint PluggedIn;
            internal readonly uint Battery;

            internal Snapshot(Guid plan, uint pluggedIn, uint battery)
            {
                Plan = plan;
                PluggedIn = pluggedIn;
                Battery = battery;
            }

            internal bool AlreadyAwake { get { return PluggedIn == 0 && Battery == 0; } }

            internal static Snapshot Read(Guid plan)
            {
                return new Snapshot(plan, WindowsPower.ReadLid(plan, true), WindowsPower.ReadLid(plan, false));
            }
        }

        private readonly string recoveryFile;
        private readonly Func<ulong> millisecondsSinceBoot;
        private bool recoveryLoaded;
        private Snapshot saved;
        private Guid enabledPlan;
        private ulong? restoreAt;

        internal LidOverride(string recoveryFile) : this(recoveryFile, GetTickCount64) { }

        internal LidOverride(string recoveryFile, Func<ulong> millisecondsSinceBoot)
        {
            this.recoveryFile = recoveryFile;
            this.millisecondsSinceBoot = millisecondsSinceBoot;
        }

        internal bool IsOn { get; private set; }
        internal bool NeedsRestore { get { return !recoveryLoaded || (!IsOn && saved != null); } }
        internal bool HasTimer { get { return restoreAt.HasValue; } }
        internal int MinutesLeft
        {
            get
            {
                ulong now = millisecondsSinceBoot();
                return restoreAt.HasValue && restoreAt.Value > now
                    ? (int)Math.Ceiling((restoreAt.Value - now) / 60000.0)
                    : 0;
            }
        }

        internal void Enable()
        {
            EnableCurrentPlan();
            restoreAt = null;
        }

        internal void EnableForOneHour()
        {
            EnableCurrentPlan();
            restoreAt = millisecondsSinceBoot() + 3600000UL;
        }

        private void EnableCurrentPlan()
        {
            if (IsOn) return;
            Restore();
            Guid plan = WindowsPower.ActivePlan();
            WindowsPower.CheckLidPolicy();
            Snapshot original = Snapshot.Read(plan);
            if (!original.AlreadyAwake)
            {
                Save(original);
                try
                {
                    if (original.PluggedIn != 0) WindowsPower.WriteLid(plan, true, 0);
                    if (original.Battery != 0) WindowsPower.WriteLid(plan, false, 0);
                    WindowsPower.ApplyIfActive(plan);
                    if (!Snapshot.Read(plan).AlreadyAwake)
                        throw new InvalidOperationException("Windows did not keep the new lid settings.");
                }
                catch (Exception changeError)
                {
                    try { Restore(); }
                    catch (Exception restoreError)
                    {
                        throw new InvalidOperationException(changeError.Message +
                            "\r\n\r\nCould not restore the previous settings: " + restoreError.Message, changeError);
                    }
                    throw;
                }
            }
            enabledPlan = plan;
            IsOn = true;
        }

        internal RefreshResult Refresh()
        {
            if (!IsOn) return RefreshResult.Unchanged;
            if (restoreAt.HasValue && millisecondsSinceBoot() >= restoreAt.Value)
            {
                Restore();
                return RefreshResult.TimerExpired;
            }
            if (WindowsPower.ActivePlan() != enabledPlan)
            {
                ulong? deadline = restoreAt;
                Restore();
                EnableCurrentPlan();
                restoreAt = deadline;
            }
            else if (!Snapshot.Read(enabledPlan).AlreadyAwake)
            {
                Restore();
                return RefreshResult.SettingsChanged;
            }
            return RefreshResult.Unchanged;
        }

        internal void Restore()
        {
            IsOn = false;
            restoreAt = null;
            if (!recoveryLoaded)
            {
                saved = Load();
                recoveryLoaded = true;
            }
            if (saved == null) return;

            if (WindowsPower.PlanExists(saved.Plan))
            {
                // Preserve any lid action the user has since selected outside the app.
                Exception acError = TryRestore(saved.Plan, true, saved.PluggedIn);
                Exception dcError = TryRestore(saved.Plan, false, saved.Battery);
                WindowsPower.ApplyIfActive(saved.Plan);
                if (acError != null) throw acError;
                if (dcError != null) throw dcError;
            }
            File.Delete(recoveryFile);
            saved = null;
        }

        private static Exception TryRestore(Guid plan, bool pluggedIn, uint previous)
        {
            try
            {
                if (previous != 0 && WindowsPower.ReadLid(plan, pluggedIn) == 0)
                {
                    WindowsPower.WriteLid(plan, pluggedIn, previous);
                    if (WindowsPower.ReadLid(plan, pluggedIn) != previous)
                        throw new InvalidOperationException("Windows did not restore the previous lid setting.");
                }
                return null;
            }
            catch (Exception error) { return error; }
        }

        private Snapshot Load()
        {
            string contents;
            try { contents = File.ReadAllText(recoveryFile); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }

            string[] fields = contents.Trim().Split(' ');
            Guid plan;
            uint ac, dc;
            if (fields.Length != 3 || !Guid.TryParseExact(fields[0], "D", out plan)
                || !uint.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out ac) || ac > 3
                || !uint.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out dc) || dc > 3)
                throw new InvalidDataException("The saved lid settings could not be read. File: " + recoveryFile);
            return new Snapshot(plan, ac, dc);
        }

        private void Save(Snapshot snapshot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(recoveryFile));
            string temporaryFile = recoveryFile + ".tmp";
            byte[] contents = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture,
                "{0:D} {1} {2}\n", snapshot.Plan, snapshot.PluggedIn, snapshot.Battery));
            using (var stream = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(contents, 0, contents.Length);
                stream.Flush(true);
            }
            File.Move(temporaryFile, recoveryFile);
            saved = snapshot;
        }

        // This clock includes time spent asleep and is unaffected by wall clock changes.
        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();
    }
}
