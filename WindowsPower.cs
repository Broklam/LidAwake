using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LidAwake
{
    internal static class WindowsPower
    {
        private static Guid buttons = new Guid("4f971e89-eebd-4455-a8de-9e59040e7347");
        private static Guid lid = new Guid("5ca83367-6e45-459f-a27b-476b1d01c936");

        internal static Guid ActivePlan()
        {
            IntPtr pointer;
            Check(PowerGetActiveScheme(IntPtr.Zero, out pointer), "Read the active power plan");
            try { return (Guid)Marshal.PtrToStructure(pointer, typeof(Guid)); }
            finally { LocalFree(pointer); }
        }

        internal static bool PlanExists(Guid plan)
        {
            uint size = 0;
            uint result = PowerReadFriendlyName(IntPtr.Zero, ref plan, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
            if (result == 2) return false; // ERROR_FILE_NOT_FOUND
            if (result != 234) Check(result, "Find the saved power plan"); // ERROR_MORE_DATA
            return true;
        }

        internal static void CheckLidPolicy()
        {
            Check(PowerSettingAccessCheck(0, ref lid), "Change the plugged-in lid setting");
            Check(PowerSettingAccessCheck(1, ref lid), "Change the battery lid setting");
        }

        internal static uint ReadLid(Guid plan, bool pluggedIn)
        {
            uint value;
            uint result = pluggedIn
                ? PowerReadACValueIndex(IntPtr.Zero, ref plan, ref buttons, ref lid, out value)
                : PowerReadDCValueIndex(IntPtr.Zero, ref plan, ref buttons, ref lid, out value);
            Check(result, "Read the " + Source(pluggedIn) + " lid setting");
            if (value > 3) throw new InvalidOperationException("Windows returned an unrecognized lid setting.");
            return value;
        }

        internal static void WriteLid(Guid plan, bool pluggedIn, uint value)
        {
            uint result = pluggedIn
                ? PowerWriteACValueIndex(IntPtr.Zero, ref plan, ref buttons, ref lid, value)
                : PowerWriteDCValueIndex(IntPtr.Zero, ref plan, ref buttons, ref lid, value);
            Check(result, "Change the " + Source(pluggedIn) + " lid setting");
        }

        internal static void ApplyIfActive(Guid plan)
        {
            // Reapply modified settings without intentionally switching to a different plan.
            if (ActivePlan() == plan)
                Check(PowerSetActiveScheme(IntPtr.Zero, ref plan), "Apply the lid settings");
        }

        private static string Source(bool pluggedIn) { return pluggedIn ? "plugged-in" : "battery"; }

        private static void Check(uint result, string operation)
        {
            if (result != 0)
            {
                int code = (int)result;
                throw new Win32Exception(code, operation + ": " + new Win32Exception(code).Message);
            }
        }

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr plan);
        [DllImport("powrprof.dll")]
        private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid plan, IntPtr subgroup,
            IntPtr setting, IntPtr buffer, ref uint size);
        [DllImport("powrprof.dll")]
        private static extern uint PowerSettingAccessCheck(uint accessor, ref Guid setting);
        [DllImport("powrprof.dll")]
        private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid plan, ref Guid subgroup,
            ref Guid setting, out uint value);
        [DllImport("powrprof.dll")]
        private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid plan, ref Guid subgroup,
            ref Guid setting, out uint value);
        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid plan, ref Guid subgroup,
            ref Guid setting, uint value);
        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid plan, ref Guid subgroup,
            ref Guid setting, uint value);
        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid plan);
        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
