using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

// Allocate a new console for the process if necessary
// Ensure the application is compiled as WinExe (In Visual Studio , right-click the project > Properties > Application > Output type: Windows Application)
// This allows it to run purely in background mode without a console window.
// When run through arguments it exits immediately after sending the command to the Stream Deck, so that prevents a flicker of the console window.

namespace StreamDeckBrightnessControl;
internal static class ConsoleHandler
{
    internal static class WindowsNativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AllocConsole();
    }

    /// <summary>
    /// Manages console allocation based on whether the application should run in background mode.
    /// On Windows, controls whether a console window is allocated for the process.
    /// </summary>
    /// <param name="runInBackgroundArg">If true, attempts to run in background mode without a console window (Windows only).</param>
    /// <returns>True if the application is running in background mode, false otherwise.</returns>
    /// <remarks>
    /// Background mode is only supported on Windows. App must be compiled as WinExe, not a console app to work, otherwise it will always show a console window.
    /// On non-Windows platforms, this method will display an error message if background mode is requested.
    /// 
    /// </remarks>
    public static bool HandleBackgroundMode(bool runInBackgroundArg)
    {
        // Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Default is to NOT run in background mode, so allocate console
            if (runInBackgroundArg == false)
            {
                // Allocate a new console for the process.
                bool allocated = WindowsNativeMethods.AllocConsole();
                return !allocated; // If we allocated a console, we are not in background mode.

            }
            else // To run in background mode we do nothing
            {
                return true;
            }
        }
        // Not windows
        else
        {
            if (runInBackgroundArg)
            {
                Console.WriteLine("Error: Can only use \"background\" mode (without console window) on Windows.");
            }
            return false; // Not Windows, so return false always
        }
    }
}
