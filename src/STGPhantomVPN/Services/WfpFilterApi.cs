using System;
using System.Runtime.InteropServices;

namespace STGPhantomVPN.Services
{
    /// <summary>Small WFP binding kept separate from the engine's filter marshaling.</summary>
    internal static class WfpFilterApi
    {
        internal static uint DeleteById(IntPtr engine, ulong id) => FwpmFilterDeleteById0(engine, id);

        [DllImport("fwpuclnt.dll")]
        static extern uint FwpmFilterDeleteById0(IntPtr engineHandle, ulong id);
    }
}
