namespace Interceptor
{
    using System.Runtime.InteropServices;
    using Interceptor.Enums;

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyStroke
    {
        public InterceptionKey Code;
        public KeyState State;
        public uint Information;
    }
}