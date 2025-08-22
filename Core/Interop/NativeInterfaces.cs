using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace dockus.Core.Interop;

/// <summary>
/// Provides methods to activate UWP applications.
/// </summary>
[ComImport]
[Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApplicationActivationManager
{
    IntPtr ActivateApplication(
        [In] string appUserModelId,
        [In] string arguments,
        [In] int options, // ACTIVATION_OPTIONS
        [Out] out uint processId);
}

/// <summary>
/// CLSID for the ApplicationActivationManager class.
/// </summary>
[ComImport]
[Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
internal class ApplicationActivationManager : IApplicationActivationManager
{
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    public extern IntPtr ActivateApplication(
        [In] string appUserModelId,
        [In] string arguments,
        [In] int options,
        [Out] out uint processId);
}